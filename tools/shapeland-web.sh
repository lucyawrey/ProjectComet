#!/usr/bin/env bash
# Builds ShapeLand's web client and runs it from the game server, for testing in a browser without opening
# the Unity editor. Builds the content, makes the Unity web build in batch mode (skipped when nothing it uses
# changed since the last one), starts the game server serving the build on the same port, optionally starts
# bots, and opens the page. Ctrl+C stops everything.
#
#   tools/shapeland-web.sh [--rebuild] [--no-build] [--bots N] [--cheaters N] [--port 5080] [--lan] [--no-open]
#                          [--net PROFILE] [--bots-net PROFILE] [--bots-chat]
#
#   --rebuild      make the Unity web build even if it looks up to date
#   --no-build     use the last web build as it is (content still rebuilds)
#   --bots N       start N honest bots (default 0)
#   --cheaters N   start N speed-cheating bots (default 0)
#   --bots-chat    let the bots chat now and then (they stay quiet otherwise)
#   --port P       the game server's port (default 5080)
#   --lan          listen on every network interface, so other devices on the network can join
#   --no-open      don't open the page in a browser
#   --net P        run the server and bots in Docker with simulated network conditions on the server's link, so
#                  every client gets them (shapeland/docker): none, good (about 80 ms round trip, 1% loss),
#                  bad (about 200 ms, 2% loss) or awful (about 200 ms, 5% loss). Desktop builds join it too.
#   --bots-net P   with --net, a profile the bots get on top of it (default none), e.g. for high-ping bots
#
# The profiles are in tools/shapeland-net-profiles.sh. Docker needs the traffic-shaping kernel modules loaded
# (tools/StackBench/README.md, Docker runs).
# The Unity build fails while the editor has the project open; close it first. ShapeLand only: Project Anima's
# page comes from the Login server.
set -euo pipefail

repo=$(cd "$(dirname "$0")/.." && pwd)
project="$repo/shapeland/unity"
web="$repo/artifacts/unity/shapeland/web-game"
content="$repo/artifacts/content/shapeland/content.bin"

rebuild=0
build=1
bots=0
cheaters=0
port=5080
host=localhost
open_page=1
net=""
bots_net=none
bots_chat=()
while [ $# -gt 0 ]; do
  case "$1" in
    --rebuild) rebuild=1; shift ;;
    --no-build) build=0; shift ;;
    --bots) bots="$2"; shift 2 ;;
    --cheaters) cheaters="$2"; shift 2 ;;
    --port) port="$2"; shift 2 ;;
    --lan) host=0.0.0.0; shift ;;
    --no-open) open_page=0; shift ;;
    --net) net="$2"; shift 2 ;;
    --bots-net) bots_net="$2"; shift 2 ;;
    --bots-chat) bots_chat=(--chat); shift ;;
    -h|--help) sed -n '2,26p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option $1 (see --help)" >&2; exit 2 ;;
  esac
done

# shellcheck source=tools/shapeland-net-profiles.sh
. "$repo/tools/shapeland-net-profiles.sh"
if [ -n "$net" ]; then
  net_profile NETEM "$net"
  net_profile BOTS_NETEM "$bots_net"
  check_net_modules
elif [ "$bots_net" != none ]; then
  echo "--bots-net needs --net." >&2
  exit 2
fi

echo "== Content"
dotnet run --project "$repo/shapeland/src/ShapeLand.ContentBuild" -- build

# The Unity build is up to date when no file it's made from is newer than its page.
stale() {
  [ -f "$web/index.html" ] || return 0
  [ -n "$(find "$project/Assets" "$project/Packages" "$project/ProjectSettings" \
    "$repo/comet/src/Comet.Client" "$repo/comet/src/Comet.Content" "$repo/comet/src/Comet.Protocol" \
    "$repo/comet/src/Comet.Simulation" "$repo/comet/unity" "$repo/shapeland/src/ShapeLand.Shared" \
    -type f -newer "$web/index.html" ! -name '*.meta' ! -path '*/StreamingAssets/*' -print 2>/dev/null | head -n 1)" ]
}

if [ "$build" = 1 ] && { [ "$rebuild" = 1 ] || stale; }; then
  echo "== Unity web build (a few minutes)"
  if [ -e "$project/Temp/UnityLockfile" ] && pgrep -fi "projectpath.*shapeland/unity" > /dev/null 2>&1; then
    echo "The Unity editor has the project open; close it first (or use --no-build)." >&2
    exit 1
  fi
  "$repo/tools/unity-batch.sh" -l Logs/web.log -- -buildTarget WebGL -executeMethod ShapeLand.Client.Editor.Builds.WebGame -quit
  # Marks the build as up to date, but only a build that made its page.
  [ -f "$web/index.html" ] || { echo "The Unity build made no page at $web/index.html (log: $project/Logs/web.log)" >&2; exit 1; }
  touch "$web/index.html"
elif [ ! -f "$web/index.html" ]; then
  echo "No web build yet; run without --no-build." >&2
  exit 1
else
  echo "== Unity web build is up to date"
fi

# Content changes need no Unity build: the page loads it from StreamingAssets.
mkdir -p "$web/StreamingAssets"
cmp -s "$content" "$web/StreamingAssets/content.bin" || cp "$content" "$web/StreamingAssets/content.bin"

# Runs a build step quietly, showing its output only if it fails (dotnet prints compile errors to stdout).
quietly() {
  local out
  out=$("$@" 2>&1) || { printf '%s\n' "$out" >&2; echo "Failed: $*" >&2; return 1; }
}

echo "== Game server"
quietly dotnet build -v quiet -nologo "$repo/shapeland/src/ShapeLand.GameServer"
if [ $((bots + cheaters)) -gt 0 ]; then
  quietly dotnet build -v quiet -nologo "$repo/shapeland/src/ShapeLand.Bots"
fi
compose=(docker compose -f "$repo/shapeland/docker/compose.yaml")
if [ -n "$net" ]; then
  echo "== Docker image"
  "${compose[@]}" build -q
fi

# This machine's address on the local network: the one its default route goes out on.
local_address() {
  case "$(uname)" in
    Darwin)
      local interface
      interface=$(route -n get default 2> /dev/null | awk '/interface:/ { print $2 }')
      [ -n "$interface" ] && ipconfig getifaddr "$interface" 2> /dev/null
      ;;
    *)
      ip route get 1.1.1.1 2> /dev/null | awk '{ for (i = 1; i < NF; i++) if ($i == "src") { print $(i + 1); exit } }' ||
        hostname -I 2> /dev/null | awk '{ print $1 }'
      ;;
  esac
}

url="http://localhost:$port/"
if curl -s -o /dev/null "$url"; then
  echo "Something is already using port $port (another run of this script?); stop it or use --port." >&2
  exit 1
fi

pids=()
cleanup() {
  trap - EXIT INT TERM
  if [ -n "$net" ]; then
    "${compose[@]}" --profile bots down -t 2 > /dev/null 2>&1 || true
  fi
  for pid in "${pids[@]+"${pids[@]}"}"; do
    kill "$pid" 2> /dev/null || true
  done
  wait 2> /dev/null || true
}
trap cleanup EXIT INT TERM

if [ -n "$net" ]; then
  # The server's logs show in this terminal; the bots' (and netem's note) only with docker compose logs.
  export SHAPELAND_PUBLISH="${host/localhost/127.0.0.1}:$port" BOTS_COUNT="$bots" BOTS_CHEATERS="$cheaters" BOTS_CHAT="${bots_chat:+30}"
  "${compose[@]}" up --no-log-prefix server &
  pids+=($!)
else
  # The built programs run directly: stopping `dotnet run` can leave the program it started running.
  dotnet "$repo/artifacts/bin/ShapeLand.GameServer/debug/ShapeLand.GameServer.dll" \
    --urls "http://$host:$port" --ShapeLand:WebRoot="$web" \
    --Comet:Connections:MaxConnectionsPerAddress=1000 & # the bots all come from this machine
  pids+=($!)
fi

for _ in $(seq 1 60); do
  kill -0 "${pids[0]}" 2> /dev/null || { echo "The game server stopped." >&2; exit 1; }
  curl -sf -o /dev/null "$url" && break
  sleep 0.5
done
# Answering, and still running (so it's this server, not one that started meanwhile).
curl -sf -o /dev/null "$url" && kill -0 "${pids[0]}" 2> /dev/null ||
  { echo "The game server didn't start at $url." >&2; exit 1; }

if [ $((bots + cheaters)) -gt 0 ] && [ -n "$net" ]; then
  "${compose[@]}" --profile bots up -d bots > /dev/null 2>&1
elif [ $((bots + cheaters)) -gt 0 ]; then
  dotnet "$repo/artifacts/bin/ShapeLand.Bots/debug/ShapeLand.Bots.dll" \
    --url "ws://localhost:$port/ws" --count "$bots" --cheaters "$cheaters" "${bots_chat[@]+"${bots_chat[@]}"}" > /dev/null &
  pids+=($!)
fi

echo "== Playing at $url (Ctrl+C stops)${net:+, network profile $net}"
if [ "$host" = 0.0.0.0 ]; then
  address=$(local_address || true)
  [ -n "$address" ] || address="<this machine's address>"
  echo "   Other devices: http://$address:$port/"
fi
if [ "$open_page" = 1 ]; then
  case "$(uname)" in
    Darwin) open "$url" ;;
    *) xdg-open "$url" > /dev/null 2>&1 || true ;;
  esac
fi

wait "${pids[0]}"
