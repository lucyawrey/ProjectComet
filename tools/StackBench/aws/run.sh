#!/usr/bin/env bash
# Runs the stack benchmark on two AWS VMs (server and bots) and checks the results against
# thresholds.json. Everything it creates is tagged stackbench and deleted when it exits, even on
# failure or Ctrl-C; cleanup.sh deletes anything left over if it was killed hard.
#
#   tools/StackBench/aws/run.sh clean|impaired
#
# Uses the AWS CLI's current profile and region (AWS_PROFILE, AWS_REGION to override).
# Optional: BENCH_BOTS (300), BENCH_RAMP (10), BENCH_WARMUP (60), BENCH_DURATION (600) seconds,
# BENCH_SETTLE_HEAP (true), BENCH_GC_LATENCY_MODE (e.g. SustainedLowLatency; default unset),
# INSTANCE_TYPE (c7i-flex.large), BENCH_RESULTS (results folder name, default aws-<profile>).
set -euo pipefail

profile=${1:-}
case "$profile" in
  clean)    delay=0ms  jitter=0ms ;;
  impaired) delay=40ms jitter=10ms ;;
  *) echo "usage: $0 clean|impaired" >&2; exit 2 ;;
esac

here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../.." && pwd)
instance_type=${INSTANCE_TYPE:-c7i-flex.large}
warmup=${BENCH_WARMUP:-60}
duration=${BENCH_DURATION:-600}
bots=${BENCH_BOTS:-300}
run_id=stackbench-$(date +%Y%m%d-%H%M%S)
results="$repo/tools/StackBench/results/${BENCH_RESULTS:-aws-$profile}"

if [ -n "$(git -C "$repo" status --porcelain -- comet/src tools/StackBench)" ]; then
  echo "Note: uncommitted changes in the benchmark code aren't included; the VMs run HEAD." >&2
fi

work=$(mktemp -d)
sg=
key_created=

cleanup() {
  trap '' INT TERM # a second Ctrl-C mustn't cut cleanup short
  set +e
  echo "Deleting AWS resources…"
  # By tag, so an instance launched just before an interruption is included.
  local ids
  ids=$(aws ec2 describe-instances --filters "Name=tag:stackbench,Values=$run_id" \
    Name=instance-state-name,Values=pending,running,stopping,stopped \
    --query 'Reservations[].Instances[].InstanceId' --output text)
  if [ -n "$ids" ]; then
    aws ec2 terminate-instances --instance-ids $ids > /dev/null
    aws ec2 wait instance-terminated --instance-ids $ids
  fi
  if [ -n "$sg" ]; then # its network interfaces can take a moment to detach
    for _ in 1 2 3 4 5 6; do
      aws ec2 delete-security-group --group-id "$sg" > /dev/null 2>&1 && break
      sleep 10
    done
  fi
  [ -n "$key_created" ] && aws ec2 delete-key-pair --key-name "$run_id" > /dev/null
  rm -rf "$work"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

tag_spec() { # resource type → tag specification for this run
  echo "ResourceType=$1,Tags=[{Key=stackbench,Value=$run_id},{Key=Name,Value=$run_id}]"
}

echo "Run $run_id: $profile profile on $instance_type in ${AWS_REGION:-${AWS_DEFAULT_REGION:-$(aws configure get region)}}"

# A key for this run only, and SSH from this machine's public IP only.
ssh-keygen -q -t ed25519 -N '' -f "$work/key"
aws ec2 import-key-pair --key-name "$run_id" --public-key-material "fileb://$work/key.pub" \
  --tag-specifications "$(tag_spec key-pair)" > /dev/null
key_created=1

vpc=$(aws ec2 describe-vpcs --filters Name=is-default,Values=true --query 'Vpcs[0].VpcId' --output text)
sg=$(aws ec2 create-security-group --group-name "$run_id" --description "Stack benchmark $run_id" \
  --vpc-id "$vpc" --tag-specifications "$(tag_spec security-group)" --query GroupId --output text)
my_ip=$(curl -fsS https://checkip.amazonaws.com)
aws ec2 authorize-security-group-ingress --group-id "$sg" --protocol tcp --port 22 --cidr "$my_ip/32" > /dev/null
aws ec2 authorize-security-group-ingress --group-id "$sg" --protocol tcp --port 5080 --source-group "$sg" > /dev/null

# Each VM also shuts itself down (and so terminates) after a hard maximum, in case this script dies
# without cleaning up: a closed laptop, a killed terminal.
max_minutes=$(( (warmup + duration) / 60 + 45 ))
{ cat "$here/cloud-init.yaml"; echo "  - shutdown -h +$max_minutes"; } > "$work/user-data.yaml"

ami=$(aws ssm get-parameter --name /aws/service/canonical/ubuntu/server/24.04/stable/current/amd64/hvm/ebs-gp3/ami-id \
  --query Parameter.Value --output text)
launch() { # role → instance ID
  aws ec2 run-instances --image-id "$ami" --instance-type "$instance_type" --count 1 \
    --key-name "$run_id" --security-group-ids "$sg" --user-data "file://$work/user-data.yaml" --instance-initiated-shutdown-behavior terminate \
    --block-device-mappings 'DeviceName=/dev/sda1,Ebs={VolumeSize=16,VolumeType=gp3}' \
    --tag-specifications "ResourceType=instance,Tags=[{Key=stackbench,Value=$run_id},{Key=Name,Value=$run_id-$1}]" \
    --query 'Instances[0].InstanceId' --output text
}
server_id=$(launch server)
bots_id=$(launch bots)
aws ec2 wait instance-running --instance-ids "$server_id" "$bots_id"
address() { # instance ID, field → address
  aws ec2 describe-instances --instance-ids "$1" --query "Reservations[0].Instances[0].$2" --output text
}
server_ip=$(address "$server_id" PublicIpAddress)
server_private=$(address "$server_id" PrivateIpAddress)
bots_ip=$(address "$bots_id" PublicIpAddress)

ssh_opts=(-i "$work/key" -o StrictHostKeyChecking=accept-new -o UserKnownHostsFile="$work/known_hosts" -o ConnectTimeout=5 -o ServerAliveInterval=15 -o ServerAliveCountMax=4 -o LogLevel=ERROR)
on() { local host=$1; shift; ssh "${ssh_opts[@]}" "ubuntu@$host" "$@"; }

echo "Waiting for both VMs to finish setup…"
for host in "$server_ip" "$bots_ip"; do
  tries=0
  until on "$host" true 2> /dev/null; do
    tries=$((tries + 1))
    [ $tries -lt 100 ] || { echo "Can't reach $host over SSH." >&2; exit 1; }
    sleep 3
  done
  on "$host" 'timeout 900 cloud-init status --wait > /dev/null; for m in ifb sch_netem sch_ingress act_mirred cls_matchall; do
    [ -d /sys/module/$m ] || { echo "Kernel module $m isn'\''t loaded" >&2; exit 1; }; done'
done

echo "Copying HEAD to both VMs and building…"
for host in "$server_ip" "$bots_ip"; do
  git -C "$repo" archive HEAD global.json Directory.Build.props Directory.Packages.props ProjectComet.slnx comet/src tools/StackBench \
    | on "$host" 'mkdir -p bench && tar -x -C bench && mkdir -p bench/tools/StackBench/results'
done

# Each VM runs one service from the same compose file. Both have 2 vCPUs, the two threads of one
# core: the server is pinned to one thread (the OS has the other), the bots get both.
common="BENCH_WARMUP=$warmup BENCH_DURATION=$duration BENCH_BOTS=$bots BENCH_RAMP=${BENCH_RAMP:-10}"
common+=" BENCH_SETTLE_HEAP=${BENCH_SETTLE_HEAP:-true} BENCH_GC_LATENCY_MODE=${BENCH_GC_LATENCY_MODE:-}"
server_compose="cd bench && $common SERVER_CPUS=1 BOT_CPUS=0 SERVER_PUBLISH=0.0.0.0:5080 docker compose -f tools/StackBench/docker/compose.yaml"
bots_compose="cd bench && $common SERVER_CPUS=0 BOT_CPUS=0,1 BENCH_URL=ws://$server_private:5080/ws \
  NETEM_DELAY=$delay NETEM_JITTER=$jitter NETEM_LOSS=1% docker compose -f tools/StackBench/docker/compose.yaml"
on "$server_ip" "$server_compose build -q server" & server_build=$!
on "$bots_ip" "$bots_compose build -q bots" & bots_build=$!
wait "$server_build"; wait "$bots_build"

echo "Running ($warmup s warmup, $duration s window)…"
on "$server_ip" "$server_compose up -d --wait server"
# A time limit, so a hang ends the run (and cleanup deletes the VMs) instead of leaving them running.
limit=$((warmup + duration + 300))
on "$bots_ip" "${bots_compose/docker compose/timeout -k 60 $limit docker compose} up --no-deps --abort-on-container-failure bots"
on "$server_ip" "timeout 120 docker wait \$(docker ps -aqf name=server) > /dev/null"

mkdir -p "$results"
rm -f "$results/server.json" "$results/bots.json"
scp "${ssh_opts[@]}" "ubuntu@$server_ip:bench/tools/StackBench/results/server.json" "$results/"
scp "${ssh_opts[@]}" "ubuntu@$bots_ip:bench/tools/StackBench/results/bots.json" "$results/"

cd "$repo"
dotnet run -c Release --project tools/StackBench/StackBench.Report -- \
  "$results/server.json" "$results/bots.json" tools/StackBench/thresholds.json "$profile"
