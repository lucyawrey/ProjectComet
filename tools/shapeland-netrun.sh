#!/usr/bin/env bash
# Runs ShapeLand's game server under a simulated network profile with headless bots, for 3c's tuning, and
# prints what they measured: false snap-backs on honest bots, cheaters caught, ping, the interpolation delay
# and its target, and holds; then the server's side (rule violations, and airborne players gone silent whose
# fall it finished, which reach the bot as snap-backs too). Everything also goes to a results file stamped with the profile and commit.
#
#   tools/shapeland-netrun.sh PROFILE [--seconds 180] [--bots 10] [--cheaters 1] [--seed 1] [--warmup 10]
#                             [--bots-net PROFILE] [--port 5091]
#
#   PROFILE        none, good, bad or awful (tools/shapeland-net-profiles.sh), on the server's link
#   --seconds S    how long the bots run, warmup included (default 180)
#   --bots N       honest bots, whose network numbers are recorded (default 10)
#   --cheaters N   speed-cheating bots, which should all be caught (default 1)
#   --seed N       the bots' seed, so runs before and after a change are comparable (default 1)
#   --warmup S     seconds after joining before numbers are recorded, while the delay settles (default 10)
#   --bots-net P   a profile the bots get on top, e.g. for high-ping bots; they then run in their own container
#   --port P       the server's port on this machine (default 5091, so a tools/shapeland-web.sh run can go on)
#
# The bots run on this machine and reach the server through its published port, as a browser does, unless
# --bots-net is given. Results go to artifacts/netruns/<time>-<profile>.json, with the server's log beside it.
set -euo pipefail

repo=$(cd "$(dirname "$0")/.." && pwd)
# shellcheck source=tools/shapeland-net-profiles.sh
. "$repo/tools/shapeland-net-profiles.sh"

profile="${1:-}"
case "$profile" in
  ""|-*) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 2 ;;
esac
shift
seconds=180
bots=10
cheaters=1
seed=1
warmup=10
bots_net=none
port=5091
while [ $# -gt 0 ]; do
  case "$1" in
    --seconds) seconds="$2"; shift 2 ;;
    --bots) bots="$2"; shift 2 ;;
    --cheaters) cheaters="$2"; shift 2 ;;
    --seed) seed="$2"; shift 2 ;;
    --warmup) warmup="$2"; shift 2 ;;
    --bots-net) bots_net="$2"; shift 2 ;;
    --port) port="$2"; shift 2 ;;
    -h|--help) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option $1 (see --help)" >&2; exit 2 ;;
  esac
done

net_profile NETEM "$profile"
net_profile BOTS_NETEM "$bots_net"
check_net_modules

commit=$(git -C "$repo" rev-parse --short HEAD)
[ -z "$(git -C "$repo" status --porcelain)" ] || commit="$commit-dirty"
runs="$repo/artifacts/netruns"
mkdir -p "$runs"
name="$(date +%Y%m%d-%H%M%S)-$profile"
[ "$bots_net" = none ] || name="$name-bots-$bots_net"
results="$runs/$name.json"

echo "== Building"
dotnet run --project "$repo/shapeland/src/ShapeLand.ContentBuild" -- build > /dev/null
dotnet build -v quiet -nologo "$repo/shapeland/src/ShapeLand.GameServer" > /dev/null
dotnet build -v quiet -nologo "$repo/shapeland/src/ShapeLand.Bots" > /dev/null

# Its own Compose project, so a tools/shapeland-web.sh run alongside keeps its containers. No web build.
compose=(docker compose -p shapeland-netrun -f "$repo/shapeland/docker/compose.yaml")
export SHAPELAND_PUBLISH="127.0.0.1:$port" SHAPELAND_WEBROOT="" SHAPELAND_WEB_DIR="$repo/shapeland/docker"
cleanup() {
  trap - EXIT INT TERM
  "${compose[@]}" logs --no-log-prefix server > "$runs/$name.server.log" 2>&1 || true
  "${compose[@]}" --profile bots down -t 2 > /dev/null 2>&1 || true
  # What the server saw, from each player's line on leaving: rule violations, and falls it finished itself.
  awk '/ left \(.*movement violations/ {
      cheater = ($0 ~ /Cheater /); v = $0; sub(/.*; /, "", v); split(v, n, " ")
      if (cheater) { cv += n[1]; cf += n[4] } else { hv += n[1]; hf += n[4] }; seen = 1 }
    END { if (seen) printf "  Server         honest bots: %d violations, %d falls finished by the server; cheaters: %d violations\n", hv, hf, cv }' \
    "$runs/$name.server.log"
}
trap cleanup EXIT INT TERM

echo "== Server under $profile"
"${compose[@]}" build -q server > /dev/null 2>&1
"${compose[@]}" up -d --wait server > /dev/null 2>&1 || { echo "The game server didn't start; see docker compose logs." >&2; exit 1; }

echo "== $bots bots and $cheaters cheaters for $seconds s (warmup $warmup s)"
args=(--count "$bots" --cheaters "$cheaters" --seconds "$seconds" --seed "$seed" --warmup "$warmup"
  --meta "profile=$profile" --meta "botsNet=$bots_net" --meta "commit=$commit")
if [ "$bots_net" = none ]; then
  dotnet "$repo/artifacts/bin/ShapeLand.Bots/debug/ShapeLand.Bots.dll" --url "ws://127.0.0.1:$port/ws" \
    "${args[@]}" --results "$results"
else
  # In the bots' container, behind their own netem as well; the results file is copied out afterwards.
  container=shapeland-netrun-bots
  docker rm -f "$container" > /dev/null 2>&1 || true
  "${compose[@]}" --profile bots run --name "$container" -T bots dotnet /app/bin/ShapeLand.Bots/debug/ShapeLand.Bots.dll \
    --url ws://server:5080/ws --content /app/content/content.bin "${args[@]}" --results /tmp/results.json
  docker cp "$container:/tmp/results.json" "$results" > /dev/null
  docker rm "$container" > /dev/null
  echo "  Results in $results"
fi
