#!/usr/bin/env bash
# Runs the stack benchmark on two AWS VMs (server and bots) and checks the results against
# thresholds.json. Everything it creates is tagged stackbench and deleted when it exits, even on
# failure or Ctrl-C; cleanup.sh deletes anything left over if it was killed hard.
#
#   tools/StackBench/aws/run.sh clean|impaired
#
# Uses the AWS CLI's current profile and region (AWS_PROFILE, AWS_REGION to override).
# Optional: BENCH_BOTS (300), BENCH_WARMUP (60), BENCH_DURATION (600) seconds,
# INSTANCE_TYPE (c7i-flex.large).
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
results="$repo/tools/StackBench/results/aws-$profile"

if [ -n "$(git -C "$repo" status --porcelain -- comet/src tools/StackBench)" ]; then
  echo "Note: uncommitted changes in the benchmark code aren't included; the VMs run HEAD." >&2
fi

work=$(mktemp -d)
instances=()
sg=
key_created=

cleanup() {
  set +e
  echo "Deleting AWS resources…"
  if [ ${#instances[@]} -gt 0 ]; then
    aws ec2 terminate-instances --instance-ids "${instances[@]}" > /dev/null
    aws ec2 wait instance-terminated --instance-ids "${instances[@]}"
  fi
  [ -n "$sg" ] && aws ec2 delete-security-group --group-id "$sg"
  [ -n "$key_created" ] && aws ec2 delete-key-pair --key-name "$run_id" > /dev/null
  rm -rf "$work"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

tag_spec() { # resource type → tag specification for this run
  echo "ResourceType=$1,Tags=[{Key=stackbench,Value=$run_id},{Key=Name,Value=$run_id}]"
}

echo "Run $run_id: $profile profile on $instance_type in $(aws configure get region || echo "$AWS_REGION")"

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

ami=$(aws ssm get-parameter --name /aws/service/canonical/ubuntu/server/24.04/stable/current/amd64/hvm/ebs-gp3/ami-id \
  --query Parameter.Value --output text)
launch() { # role → instance ID
  aws ec2 run-instances --image-id "$ami" --instance-type "$instance_type" --count 1 \
    --key-name "$run_id" --security-group-ids "$sg" --user-data "file://$here/cloud-init.yaml" \
    --block-device-mappings 'DeviceName=/dev/sda1,Ebs={VolumeSize=16,VolumeType=gp3}' \
    --tag-specifications "ResourceType=instance,Tags=[{Key=stackbench,Value=$run_id},{Key=Name,Value=$run_id-$1}]" \
    --query 'Instances[0].InstanceId' --output text
}
server_id=$(launch server); instances+=("$server_id")
bots_id=$(launch bots); instances+=("$bots_id")
aws ec2 wait instance-running --instance-ids "${instances[@]}"
address() { # instance ID, field → address
  aws ec2 describe-instances --instance-ids "$1" --query "Reservations[0].Instances[0].$2" --output text
}
server_ip=$(address "$server_id" PublicIpAddress)
server_private=$(address "$server_id" PrivateIpAddress)
bots_ip=$(address "$bots_id" PublicIpAddress)

ssh_opts=(-i "$work/key" -o StrictHostKeyChecking=accept-new -o UserKnownHostsFile="$work/known_hosts" -o ConnectTimeout=5 -o LogLevel=ERROR)
on() { local host=$1; shift; ssh "${ssh_opts[@]}" "ubuntu@$host" "$@"; }

echo "Waiting for both VMs to finish setup…"
for host in "$server_ip" "$bots_ip"; do
  until on "$host" true 2> /dev/null; do sleep 3; done
  on "$host" 'cloud-init status --wait > /dev/null; for m in ifb sch_netem sch_ingress act_mirred cls_matchall; do
    [ -d /sys/module/$m ] || { echo "Kernel module $m isn'\''t loaded" >&2; exit 1; }; done'
done

echo "Copying HEAD to both VMs and building…"
for host in "$server_ip" "$bots_ip"; do
  git -C "$repo" archive HEAD global.json Directory.Build.props Directory.Packages.props ProjectComet.slnx comet/src tools/StackBench \
    | on "$host" 'mkdir -p bench && tar -x -C bench && mkdir -p bench/tools/StackBench/results'
done

# Each VM runs one service from the same compose file. Both have 2 vCPUs, the two threads of one
# core: the server is pinned to one thread (the OS has the other), the bots get both.
common="BENCH_WARMUP=$warmup BENCH_DURATION=$duration BENCH_BOTS=$bots"
server_compose="cd bench && $common SERVER_CPUS=1 BOT_CPUS=0 SERVER_PUBLISH=0.0.0.0:5080 docker compose -f tools/StackBench/docker/compose.yaml"
bots_compose="cd bench && $common SERVER_CPUS=0 BOT_CPUS=0,1 BENCH_URL=ws://$server_private:5080/ws \
  NETEM_DELAY=$delay NETEM_JITTER=$jitter NETEM_LOSS=1% docker compose -f tools/StackBench/docker/compose.yaml"
on "$server_ip" "$server_compose build -q server" & server_build=$!
on "$bots_ip" "$bots_compose build -q bots" & bots_build=$!
wait "$server_build"; wait "$bots_build"

echo "Running ($warmup s warmup, $duration s window)…"
on "$server_ip" "$server_compose up -d --wait server"
on "$bots_ip" "$bots_compose up --no-deps --abort-on-container-failure bots"
on "$server_ip" 'docker wait $(docker ps -aqf name=server) > /dev/null'

mkdir -p "$results"
scp "${ssh_opts[@]}" "ubuntu@$server_ip:bench/tools/StackBench/results/server.json" "$results/"
scp "${ssh_opts[@]}" "ubuntu@$bots_ip:bench/tools/StackBench/results/bots.json" "$results/"

cd "$repo"
dotnet run -c Release --project tools/StackBench/StackBench.Report -- \
  "$results/server.json" "$results/bots.json" tools/StackBench/thresholds.json "$profile"
