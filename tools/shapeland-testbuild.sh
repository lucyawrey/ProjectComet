#!/usr/bin/env bash
# The public test build: ShapeLand's page on Vercel (https://shapeland.lucyawrey.com) and its game server on an
# on-demand AWS instance (wss://server.shapeland.lucyawrey.com/ws), brought up for a session or a few days and
# terminated after. Run it from your own machine; nothing here needs an agent.
#
#   tools/shapeland-testbuild.sh setup                 once per AWS account: the S3 bucket and the firewall rules
#   tools/shapeland-testbuild.sh up [--hours 24]       build and start the game server, and point its hostname at it
#   tools/shapeland-testbuild.sh down                  terminate the game server and park its hostname
#   tools/shapeland-testbuild.sh status                what's running, and whether the server answers
#   tools/shapeland-testbuild.sh log                   the server's boot log (from its console)
#   tools/shapeland-testbuild.sh page [--no-build]     build the web page and publish it on Vercel
#
#   --hours H      the server shuts itself down (and is terminated) after H hours, so a forgotten one stops
#                  costing money; 0 for never (default 24). `down` ends it sooner.
#   --no-build     publish the last web build as it is
#
# Needs: the AWS CLI logged in (`aws login`), the Vercel CLI logged in (`vercel login`), Docker isn't needed.
# Settings that aren't in the repo live in ~/.config/shapeland-testbuild/env (created on first use):
#   ACME_EMAIL     the contact address Caddy gives Let's Encrypt and ZeroSSL for the server's certificate
# Costs: a t4g.micro is about 20 cents a day while up (paid from the account's credits first); the page is on
# Vercel's free plan. When the server is down, the page says the game isn't open for testing.
set -euo pipefail

repo=$(cd "$(dirname "$0")/.." && pwd)
work="$repo/artifacts/testbuild"
web="$repo/artifacts/unity/shapeland/web-game"
content="$repo/artifacts/content/shapeland/content.bin"
config_dir="$HOME/.config/shapeland-testbuild"

domain=lucyawrey.com
page_host="shapeland.$domain"
server_name=server.shapeland # under $domain
server_host="$server_name.$domain"
instance_type=t4g.micro
tag=shapeland-testbuild
# Where the server's name points while it's down: an address reserved for documentation, which nothing answers.
# Removing the record instead would let the domain's wildcard answer for the name, and resolvers keep that
# answer for 30 minutes, past the next up; the record's own cache time is a minute.
parked=192.0.2.1
vercel_project=shapeland

. "$repo/tools/shapeland-net-profiles.sh" # for quietly

command=${1:-}
[ -n "$command" ] || { sed -n '2,23p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
shift
hours=24
build=1
while [ $# -gt 0 ]; do
  case "$1" in
    --hours) hours="$2"; shift 2 ;;
    --no-build) build=0; shift ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

region=$(aws configure get region || true)
[ -n "$region" ] || { echo "No AWS region set: run aws configure set region us-east-2" >&2; exit 2; }

account() {
  aws sts get-caller-identity --query Account --output text 2> /dev/null \
    || { echo "The AWS CLI isn't logged in: run aws login" >&2; exit 2; }
}

bucket() { echo "$tag-$(account)-$region"; }

settings() {
  mkdir -p "$config_dir"
  if [ ! -f "$config_dir/env" ]; then
    printf 'ACME_EMAIL=\n' > "$config_dir/env"
    echo "Fill in $config_dir/env (ACME_EMAIL, the certificate contact address) and run this again." >&2
    exit 2
  fi
  . "$config_dir/env"
  [ -n "${ACME_EMAIL:-}" ] || { echo "Set ACME_EMAIL in $config_dir/env." >&2; exit 2; }
}

# The running (or starting) game server instances' ids.
instances() {
  aws ec2 describe-instances --filters "Name=tag:$tag,Values=server" \
    Name=instance-state-name,Values=pending,running,stopping,stopped \
    --query 'Reservations[].Instances[].InstanceId' --output text
}

security_group() {
  aws ec2 describe-security-groups --filters "Name=group-name,Values=$tag" \
    --query 'SecurityGroups[0].GroupId' --output text
}

# Vercel DNS: the ids of the server's records, and setting it to one address.
dns_ids() {
  vercel dns ls "$domain" 2> /dev/null | awk -v name="$server_name" '$2 == name { print $1 }'
}

dns_clear() {
  local id
  for id in $(dns_ids); do
    vercel dns rm "$id" --yes > /dev/null
  done
}

dns_set() {
  dns_clear
  vercel dns add "$domain" "$server_name" A "$1" > /dev/null
}

# Whether the game server at address $1 answers over HTTPS with "ok" from /health. Asked at that address, so a
# stale DNS answer on this machine can't mislead it; the certificate is still checked for the name.
answers() {
  [ "$(curl -s --max-time 5 --resolve "$server_host:443:$1" "https://$server_host/health" || true)" = ok ]
}

# The running server's public address, if any.
server_ip() {
  local ids
  ids=$(instances)
  [ -z "$ids" ] || aws ec2 describe-instances --instance-ids $ids \
    --query 'Reservations[0].Instances[0].PublicIpAddress' --output text
}

setup() {
  local name sg vpc
  name=$(bucket)
  if ! aws s3api head-bucket --bucket "$name" 2> /dev/null; then
    echo "== Creating the bucket $name (private)"
    if [ "$region" = us-east-1 ]; then
      aws s3api create-bucket --bucket "$name" > /dev/null
    else
      aws s3api create-bucket --bucket "$name" --create-bucket-configuration "LocationConstraint=$region" > /dev/null
    fi
    aws s3api put-public-access-block --bucket "$name" --public-access-block-configuration \
      BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true
    # Old packages go after a day; each up uploads its own.
    aws s3api put-bucket-lifecycle-configuration --bucket "$name" --lifecycle-configuration \
      '{"Rules":[{"ID":"expire-packages","Status":"Enabled","Filter":{"Prefix":""},"Expiration":{"Days":1}}]}' > /dev/null
  fi

  sg=$(security_group)
  if [ "$sg" = None ]; then
    echo "== Creating the security group $tag (HTTP and HTTPS in; no SSH)"
    vpc=$(aws ec2 describe-vpcs --filters Name=isDefault,Values=true --query 'Vpcs[0].VpcId' --output text)
    sg=$(aws ec2 create-security-group --group-name "$tag" --description "ShapeLand public test build: Caddy on 80 and 443" \
      --vpc-id "$vpc" --query GroupId --output text)
    aws ec2 authorize-security-group-ingress --group-id "$sg" --ip-permissions \
      'IpProtocol=tcp,FromPort=80,ToPort=80,IpRanges=[{CidrIp=0.0.0.0/0}],Ipv6Ranges=[{CidrIpv6=::/0}]' \
      'IpProtocol=tcp,FromPort=443,ToPort=443,IpRanges=[{CidrIp=0.0.0.0/0}],Ipv6Ranges=[{CidrIpv6=::/0}]' > /dev/null
  fi
  echo "Set up: bucket $name, security group $sg."
}

# The server package: the game server for Linux on ARM (self-contained), its content and the Caddyfile.
package() {
  local out="$work/package"
  rm -rf "$out" && mkdir -p "$out"
  echo "== Building the game server package"
  quietly dotnet run --project "$repo/shapeland/src/ShapeLand.ContentBuild" -- build
  quietly dotnet publish "$repo/shapeland/src/ShapeLand.GameServer" -c Release -r linux-arm64 --self-contained -o "$out/server"
  cp "$content" "$out/content.bin"
  cp "$repo/shapeland/deploy/Caddyfile" "$out/Caddyfile"
  tar -czf "$work/package.tar.gz" -C "$out" .
}

up() {
  local existing name key url ami sg boot id ip
  settings
  existing=$(instances)
  [ -z "$existing" ] || { echo "A game server is already up ($existing); run down first." >&2; exit 1; }
  sg=$(security_group)
  [ "$sg" != None ] || { echo "Run setup first." >&2; exit 1; }

  package
  name=$(bucket)
  key="package-$(date +%Y%m%d-%H%M%S).tar.gz"
  aws s3 cp --quiet "$work/package.tar.gz" "s3://$name/$key"
  url=$(aws s3 presign "s3://$name/$key" --expires-in 3600)

  boot=$(cat "$repo/shapeland/deploy/boot.sh")
  # Quoted replacements: newer bash would otherwise read the link's & as "the matched text".
  boot=${boot//\{\{HOST\}\}/"$server_host"}
  boot=${boot//\{\{ACME_EMAIL\}\}/"$ACME_EMAIL"}
  boot=${boot//\{\{PACKAGE_URL\}\}/"$url"}
  boot=${boot//\{\{HOURS\}\}/"$hours"}
  printf '%s\n' "$boot" > "$work/boot.sh"

  echo "== Starting a $instance_type in $region"
  ami=$(aws ssm get-parameter --name /aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-arm64 \
    --query Parameter.Value --output text)
  id=$(aws ec2 run-instances --image-id "$ami" --instance-type "$instance_type" --security-group-ids "$sg" \
    --user-data "file://$work/boot.sh" --instance-initiated-shutdown-behavior terminate \
    --metadata-options HttpTokens=required \
    --tag-specifications "ResourceType=instance,Tags=[{Key=$tag,Value=server},{Key=Name,Value=$tag}]" \
    --query 'Instances[0].InstanceId' --output text)
  aws ec2 wait instance-running --instance-ids "$id"
  ip=$(aws ec2 describe-instances --instance-ids "$id" --query 'Reservations[0].Instances[0].PublicIpAddress' --output text)

  echo "== Pointing $server_host at $ip"
  dns_set "$ip"

  echo "== Waiting for the server to answer at https://$server_host (a few minutes: boot, then a certificate)"
  for _ in $(seq 1 60); do
    if answers "$ip"; then
      echo "Up: $id at $ip. Play at https://$page_host. It shuts down by itself in $hours hours (0: never); down ends it sooner."
      return
    fi
    sleep 10
  done
  echo "The server didn't answer within 10 minutes; see tools/shapeland-testbuild.sh log. It's still running: down ends it." >&2
  exit 1
}

down() {
  local ids
  ids=$(instances)
  echo "== Parking $server_host"
  dns_set "$parked"
  if [ -n "$ids" ]; then
    echo "== Terminating $ids"
    aws ec2 terminate-instances --instance-ids $ids > /dev/null
    aws ec2 wait instance-terminated --instance-ids $ids
  fi
  echo "Down."
}

status() {
  local ids
  ids=$(instances)
  if [ -z "$ids" ]; then
    echo "No game server is up."
  else
    aws ec2 describe-instances --instance-ids $ids \
      --query 'Reservations[].Instances[].[InstanceId,State.Name,PublicIpAddress,LaunchTime]' --output text
  fi
  echo "DNS: $server_host -> $(vercel dns ls "$domain" 2> /dev/null | awk -v name="$server_name" '$2 == name { print $4 }' | tr '\n' ' ')"
  local ip
  ip=$(server_ip)
  if [ -n "$ip" ] && answers "$ip"; then echo "The server at $ip answers."; else echo "No server answers."; fi
}

log() {
  local id
  id=$(instances | awk '{ print $1 }')
  [ -n "$id" ] || { echo "No game server is up." >&2; exit 1; }
  aws ec2 get-console-output --instance-id "$id" --latest --output text
}

page() {
  local out="$work/$vercel_project" url
  if [ "$build" -eq 1 ]; then
    echo "== Building the web page (Unity, a few minutes; close the editor first)"
    "$repo/tools/unity-batch.sh" -- -buildTarget WebGL -executeMethod ShapeLand.Client.Editor.Builds.WebGame -quit
    # A web build rewrites the project settings; those changes aren't kept.
    git -C "$repo" checkout -q -- shapeland/unity/ProjectSettings/ProjectSettings.asset 2> /dev/null || true
  fi
  [ -f "$web/index.html" ] || { echo "No web build at $web." >&2; exit 1; }

  quietly dotnet run --project "$repo/shapeland/src/ShapeLand.ContentBuild" -- build
  rm -rf "$out" && mkdir -p "$out"
  cp -R "$web/." "$out/"
  mkdir -p "$out/StreamingAssets" && cp "$content" "$out/StreamingAssets/content.bin"
  cp "$repo/shapeland/deploy/vercel.json" "$out/vercel.json"
  printf 'window.shapelandServer = "wss://%s/ws";\n' "$server_host" > "$out/config.js"

  # The page's name needs its own record: once server.shapeland exists, the domain's wildcard no longer covers
  # shapeland itself (it has a name under it).
  if ! vercel dns ls "$domain" 2> /dev/null | awk '$2 == "shapeland" { found = 1 } END { exit !found }'; then
    vercel dns add "$domain" shapeland CNAME cname.vercel-dns.com > /dev/null
    vercel domains add "$page_host" "$vercel_project" > /dev/null 2>&1 || true
  fi

  echo "== Publishing on Vercel"
  # The deployment's address is the last vercel.app URL in its output (the rest varies between CLI versions).
  url=$(cd "$out" && vercel deploy --prod --yes 2>&1 | grep -o 'https://[a-z0-9.-]*\.vercel\.app' | tail -n 1)
  [ -n "$url" ] || { echo "Vercel didn't report a deployment." >&2; exit 1; }
  vercel alias set "$url" "$page_host" > /dev/null
  echo "Published: https://$page_host"
}

case "$command" in
  setup | up | down | status | log | page) "$command" ;;
  *) echo "Unknown command: $command (setup, up, down, status, log or page)" >&2; exit 2 ;;
esac
