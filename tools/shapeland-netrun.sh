#!/usr/bin/env bash
# Runs ShapeLand's game server under a simulated network profile with headless bots, for 3c's tuning, and
# prints what they measured: false snap-backs on honest bots, cheaters caught, ping, the interpolation delay
# and its target, and holds; then the server's side (rule violations, and airborne players gone silent whose
# fall it finished, which reach the bot as snap-backs too). Everything also goes to a results file stamped with the profile and commit.
#
#   tools/shapeland-netrun.sh PROFILE [--seconds 180] [--bots 10] [--cheaters 1] [--cheat-speed 2.5] [--cheat-jump 1]
#                             [--seed 1] [--warmup 10]
#                             [--bots-net PROFILE] [--far N] [--far-net bad] [--port 5091]
#
#   PROFILE        none, good, bad or awful (tools/shapeland-net-profiles.sh), on the server's link
#   --seconds S    how long the bots run, warmup included (default 180)
#   --bots N       honest bots, whose network numbers are recorded (default 10)
#   --cheaters N   speed-cheating bots, which should all be caught (default 1)
#   --cheat-speed F, --cheat-jump F
#                  how much faster cheaters move and jump than allowed (default 2.5 and 1); small values make
#                  subtle cheaters, for tuning the tolerances
#   --seed N       the bots' seed, so runs before and after a change are comparable (default 1)
#   --warmup S     seconds after joining before numbers are recorded, while the delay settles (default 10)
#   --bots-net P   a profile the bots get on top, e.g. for high-ping bots; they then run in their own container
#   --far N        also N far players: honest bots named "Far", in their own container with --far-net on top,
#                  to see how they affect the others (the summary is the others'; theirs follows)
#   --far-net P    the far players' extra profile (default bad)
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
  ""|-*) sed -n '2,23p' "$0" | sed 's/^# \{0,1\}//'; exit 2 ;;
esac
shift
seconds=180
bots=10
cheaters=1
cheat_speed=2.5
cheat_jump=1
seed=1
warmup=10
bots_net=none
far=0
far_net=bad
port=5091
while [ $# -gt 0 ]; do
  case "$1" in
    --seconds) seconds="$2"; shift 2 ;;
    --bots) bots="$2"; shift 2 ;;
    --cheaters) cheaters="$2"; shift 2 ;;
    --cheat-speed) cheat_speed="$2"; shift 2 ;;
    --cheat-jump) cheat_jump="$2"; shift 2 ;;
    --seed) seed="$2"; shift 2 ;;
    --warmup) warmup="$2"; shift 2 ;;
    --bots-net) bots_net="$2"; shift 2 ;;
    --far) far="$2"; shift 2 ;;
    --far-net) far_net="$2"; shift 2 ;;
    --port) port="$2"; shift 2 ;;
    -h|--help) sed -n '2,23p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option $1 (see --help)" >&2; exit 2 ;;
  esac
done

net_profile NETEM "$profile"
if [ "$far" -gt 0 ]; then
  [ "$bots_net" = none ] || { echo "--far needs the bots on this machine (no --bots-net)." >&2; exit 2; }
  net_profile BOTS_NETEM "$far_net"
else
  net_profile BOTS_NETEM "$bots_net"
fi
check_net_modules

commit=$(git -C "$repo" rev-parse --short HEAD)
[ -z "$(git -C "$repo" status --porcelain)" ] || commit="$commit-dirty"
runs="$repo/artifacts/netruns"
mkdir -p "$runs"
name="$(date +%Y%m%d-%H%M%S)-$profile"
[ "$bots_net" = none ] || name="$name-bots-$bots_net"
[ "$far" = 0 ] || name="$name-far-$far-$far_net"
results="$runs/$name.json"

echo "== Building"
quietly dotnet run --project "$repo/shapeland/src/ShapeLand.ContentBuild" -- build
quietly dotnet build -v quiet -nologo "$repo/shapeland/src/ShapeLand.GameServer"
quietly dotnet build -v quiet -nologo "$repo/shapeland/src/ShapeLand.Bots"

# Its own Compose project, so a tools/shapeland-web.sh run alongside keeps its containers. No web build.
compose=(docker compose -p shapeland-netrun -f "$repo/shapeland/docker/compose.yaml")
export SHAPELAND_PUBLISH="127.0.0.1:$port" SHAPELAND_WEBROOT="" SHAPELAND_WEB_DIR="$repo/shapeland/docker"
cleanup() {
  trap - EXIT INT TERM
  "${compose[@]}" logs -t --no-log-prefix server > "$runs/$name.server.log" 2>&1 || true
  docker rm -f shapeland-netrun-bots shapeland-netrun-far > /dev/null 2>&1 || true
  "${compose[@]}" --profile bots down -t 2 > /dev/null 2>&1 || true
  # What the server saw, from each player's line on leaving: rule violations, and falls it finished itself.
  awk '/ left \(.*movement violations/ {
      v = $0; sub(/.*; /, "", v); split(v, n, " ")
      if ($0 ~ /Cheater |cheater /) { cv += n[1] } else if ($0 ~ /Far /) { fv += n[1]; ff += n[4]; far = 1 } else { hv += n[1]; hf += n[4] }; seen = 1 }
    END { if (seen) printf "  Server         honest bots: %d violations, %d falls finished by the server; cheaters: %d violations\n", hv, hf, cv
      if (far) printf "  Server         far players: %d violations, %d falls finished by the server\n", fv, ff }' \
    "$runs/$name.server.log"
  # How much of the movement tolerances honest players needed (MovementHeadroom), the worst of them: banked
  # movement in seconds for each speed factor, height over an exact jump's apex, and late arc start.
  awk '/ headroom: / && !/Cheater |cheater / {
      line = $0; sub(/.*burst seconds /, "", line); split(line, parts, ";")
      n = split(parts[1], bursts, " ")
      for (i = 1; i <= n; i++) { split(bursts[i], kv, "="); if (!(kv[1] in burst) || kv[2] + 0 > burst[kv[1]]) burst[kv[1]] = kv[2] + 0; order[i] = kv[1] }
      split(parts[2], r, " "); split(parts[3], a, " ")
      if (!seen || r[4] + 0 > rise) rise = r[4] + 0
      if (!seen || a[3] + 0 > air) air = a[3] + 0
      seen = 1 }
    END { if (seen) { printf "  Headroom       honest worst: burst s"; for (i = 1; i <= n; i++) printf " %s %.3f", order[i], burst[order[i]]
      printf "; rise over apex %.3f m; air slack %.3f s\n", rise, air } }' \
    "$runs/$name.server.log"
}
# Ctrl+C or a kill stops the run there, after cleaning up.
trap cleanup EXIT
trap 'cleanup; exit 130' INT
trap 'cleanup; exit 143' TERM

echo "== Server under $profile"
quietly "${compose[@]}" build -q server
"${compose[@]}" up -d --wait server > /dev/null 2>&1 || { echo "The game server didn't start; see docker compose logs." >&2; exit 1; }

echo "== $bots bots and $cheaters cheaters for $seconds s (warmup $warmup s)"
args=(--count "$bots" --cheaters "$cheaters" --cheat-speed "$cheat_speed" --cheat-jump "$cheat_jump"
  --seconds "$seconds" --seed "$seed" --warmup "$warmup" --meta "cheatSpeed=$cheat_speed" --meta "cheatJump=$cheat_jump"
  --meta "profile=$profile" --meta "botsNet=$bots_net" --meta "commit=$commit")
if [ "$far" -gt 0 ]; then
  # The far players run alongside, in their own container behind --far-net, and are read afterwards.
  args+=(--meta "far=$far" --meta "farNet=$far_net")
  "${compose[@]}" --profile bots run -d --name shapeland-netrun-far bots dotnet /app/bin/ShapeLand.Bots/debug/ShapeLand.Bots.dll \
    --url ws://server:5080/ws --content /app/content/content.bin --prefix Far --count "$far" --cheaters 0 \
    --seconds "$seconds" --seed "$((seed + 7))" --warmup "$warmup" --results /tmp/results.json > /dev/null
fi
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
if [ "$far" -gt 0 ]; then
  docker wait shapeland-netrun-far > /dev/null
  echo
  echo "== The far players' own view ($profile plus $far_net)"
  docker logs shapeland-netrun-far 2>&1 | sed -n '/^Network results/,$p' | { grep -v "Results in" || true; }
  docker cp shapeland-netrun-far:/tmp/results.json "$runs/$name.far.json" > /dev/null
  echo "  Results in $runs/$name.far.json"
fi
