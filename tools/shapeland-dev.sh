#!/usr/bin/env bash
# Runs ShapeLand locally for development: by default the web client served from the game server, for testing
# in a browser without opening the Unity editor; with --no-web, just the server, for the editor's play mode or
# a desktop build. Builds the content, makes the Unity web build in batch mode (uncompressed, for speed, into
# artifacts/unity/shapeland/web-dev; skipped when nothing it uses changed since the last one), starts the game server, optionally starts bots, and opens the page. Then it watches the content
# sources and restarts the server when they change. Ctrl+C stops everything.
#
#   tools/shapeland-dev.sh [--rebuild] [--no-build] [--no-web] [--bots N] [--cheaters N] [--port 5080] [--lan] [--no-open]
#                          [--net PROFILE] [--bots-net PROFILE] [--bots-chat] [--no-watch] [--spawn KEY]
#
#   --rebuild      make the Unity web build even if it looks up to date
#   --no-build     use the last web build as it is (content still rebuilds)
#   --no-web       no web build or page: just the server, for the Unity editor's play mode or a desktop build.
#                  Rerun it after changing server code. After a content change, restart play mode (which picks up
#                  the new content), so client and server use the same numbers
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
#   --no-watch     don't watch the content sources. Without it, saving a change under shapeland/content/ (content
#                  numbers, zones saved from Comet › Zones) rebuilds the content and restarts the server and bots;
#                  clients rejoin by themselves (a web page reloads first for the new content; play mode needs a restart)
#   --spawn KEY    start every player at this spawn point, e.g. spawn.tests.start, to try a level in any zone
#                  (default: each at their shape's spawn)
#
# The profiles are in tools/shapeland-net-profiles.sh. Docker needs the traffic-shaping kernel modules loaded
# (tools/StackBench/README.md, Docker runs).
# With the Unity editor open on the project, the web build runs in it (tools/unity-batch.sh); it must be on the Web
# profile. ShapeLand only: Crystal's page comes from the Login server.
set -euo pipefail

repo=$(cd "$(dirname "$0")/.." && pwd)
project="$repo/shapeland/ShapeLand.Unity"
# Its own folder: these builds skip compression (SHAPELAND_FAST_WEB), and tools/shapeland-testbuild.sh publishes
# artifacts/unity/shapeland/web, so a playtest build never goes out by mistake.
web="$repo/artifacts/unity/shapeland/web-dev"
content="$repo/artifacts/content/shapeland/content.bin"

rebuild=0
build=1
serve_web=1
bots=0
cheaters=0
port=5080
host=localhost
open_page=1
net=""
bots_net=none
bots_chat=()
watch=1
spawn=""
while [ $# -gt 0 ]; do
  case "$1" in
    --rebuild) rebuild=1; shift ;;
    --no-build) build=0; shift ;;
    --no-web) serve_web=0; open_page=0; shift ;;
    --bots) bots="$2"; shift 2 ;;
    --cheaters) cheaters="$2"; shift 2 ;;
    --port) port="$2"; shift 2 ;;
    --lan) host=0.0.0.0; shift ;;
    --no-open) open_page=0; shift ;;
    --net) net="$2"; shift 2 ;;
    --bots-net) bots_net="$2"; shift 2 ;;
    --bots-chat) bots_chat=(--chat); shift ;;
    --no-watch) watch=0; shift ;;
    --spawn) spawn="$2"; shift 2 ;;
    -h|--help) awk 'NR > 1 && !/^#/ { exit } NR > 1 { sub(/^# ?/, ""); print }' "$0"; exit 0 ;;
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

build_content() {
  dotnet run --project "$repo/shapeland/src/ShapeLand.ContentBuild" -- build
}

# Changes to the content sources after this (made with the build below) start a rebuild, when watching.
stamp=$(mktemp)
echo "== Content"
build_content

# The Unity build is up to date when no file it's made from is newer than its page.
stale() {
  [ -f "$web/index.html" ] || return 0
  [ -n "$(find "$project/Assets" "$project/Packages" "$project/ProjectSettings" \
    "$repo/comet/src/Comet.Client" "$repo/comet/src/Comet.Content" "$repo/comet/src/Comet.Protocol" \
    "$repo/comet/src/Comet.Simulation" "$repo/comet/unity" "$repo/shapeland/src/ShapeLand.Shared" \
    "$repo/shapeland/content/strings" \
    -type f -newer "$web/index.html" ! -name '*.meta' ! -path '*/StreamingAssets/*' -print 2>/dev/null | head -n 1)" ]
}

if [ "$serve_web" = 0 ]; then
  # The editor copies the new content into the project itself as play mode starts.
  web=""
elif [ "$build" = 1 ] && { [ "$rebuild" = 1 ] || stale; }; then
  echo "== Unity web build (a few minutes)"
  SHAPELAND_FAST_WEB=1 "$repo/tools/unity-batch.sh" -l Logs/web.log build Web "$web"
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
copy_content() {
  [ -n "$web" ] || return 0
  mkdir -p "$web/StreamingAssets"
  cmp -s "$content" "$web/StreamingAssets/content.bin" || cp "$content" "$web/StreamingAssets/content.bin"
}
copy_content

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
if curl -sf -o /dev/null "${url}health"; then
  echo "A game server is already running on port $port: another run of this script, or the Unity editor's" >&2
  echo "(stop that one with Comet › Server › Stop). Stop it or use --port." >&2
  exit 1
elif curl -s -o /dev/null "$url"; then
  echo "Something else is already using port $port; stop it or use --port." >&2
  # Which process, where the system says (lsof works on both Linux and macOS).
  lsof -nP -iTCP:"$port" -sTCP:LISTEN 2> /dev/null | sed 's/^/  /' >&2 || true
  exit 1
fi

server_pid=""
bots_pid=""
cleanup() {
  trap - EXIT INT TERM HUP
  if [ -n "$net" ]; then
    "${compose[@]}" --profile bots down -t 2 > /dev/null 2>&1 || true
  fi
  for pid in $server_pid $bots_pid; do
    kill "$pid" 2> /dev/null || true
  done
  wait 2> /dev/null || true
  rm -f "$stamp"
}
trap cleanup EXIT
trap 'cleanup; exit 130' INT
trap 'cleanup; exit 143' TERM
# Closing the terminal sends a hangup: stop everything then too, rather than leaving the server holding the port.
trap 'cleanup; exit 129' HUP

start_server() {
  if [ -n "$net" ]; then
    # The server's logs show in this terminal; the bots' (and netem's note) only with docker compose logs.
    export SHAPELAND_WEBROOT="${web:+/app/web}" SHAPELAND_PUBLISH="${host/localhost/127.0.0.1}:$port" BOTS_COUNT="$bots" BOTS_CHEATERS="$cheaters" BOTS_CHAT="${bots_chat:+30}" SHAPELAND_START_SPAWN="$spawn"
    "${compose[@]}" up --no-log-prefix server &
  else
    # The built programs run directly: stopping `dotnet run` can leave the program it started running.
    dotnet "$repo/artifacts/bin/ShapeLand.GameServer/debug/ShapeLand.GameServer.dll" \
      --urls "http://$host:$port" --ShapeLand:WebRoot="$web" --ShapeLand:StartSpawn="$spawn" \
      --Comet:Connections:MaxConnectionsPerAddress=1000 & # the bots all come from this machine
  fi
  server_pid=$!

  for _ in $(seq 1 60); do
    kill -0 "$server_pid" 2> /dev/null || { echo "The game server stopped." >&2; exit 1; }
    curl -sf -o /dev/null "${url}health" && break
    sleep 0.5
  done
  # Answering, and still running (so it's this server, not one that started meanwhile).
  curl -sf -o /dev/null "${url}health" && kill -0 "$server_pid" 2> /dev/null ||
    { echo "The game server didn't start at $url." >&2; exit 1; }

  if [ $((bots + cheaters)) -gt 0 ] && [ -n "$net" ]; then
    "${compose[@]}" --profile bots up -d bots > /dev/null 2>&1
  elif [ $((bots + cheaters)) -gt 0 ]; then
    dotnet "$repo/artifacts/bin/ShapeLand.Bots/debug/ShapeLand.Bots.dll" \
      --url "ws://localhost:$port/ws" --count "$bots" --cheaters "$cheaters" "${bots_chat[@]+"${bots_chat[@]}"}" > /dev/null &
    bots_pid=$!
  fi
}

stop_server() {
  if [ -n "$net" ]; then
    "${compose[@]}" --profile bots down -t 2 > /dev/null 2>&1 || true
  fi
  for pid in $bots_pid $server_pid; do
    kill "$pid" 2> /dev/null || true
    wait "$pid" 2> /dev/null || true
  done
  server_pid=""
  bots_pid=""
}

start_server

if [ -n "$web" ]; then
  echo "== Playing at $url (Ctrl+C stops)${spawn:+, starting at $spawn}${net:+, network profile $net}"
else
  echo "== Server at localhost:$port, for the editor's play mode (Ctrl+C stops)${spawn:+, starting at $spawn}${net:+, network profile $net}"
fi
if [ "$host" = 0.0.0.0 ]; then
  address=$(local_address || true)
  [ -n "$address" ] || address="<this machine's address>"
  echo "   Other devices: http://$address:$port/"
fi
if [ "$watch" = 1 ]; then
  echo "   Watching shapeland/content/: saving a change there rebuilds the content and restarts the server."
fi
if [ "$open_page" = 1 ]; then
  case "$(uname)" in
    Darwin) open "$url" ;;
    *) xdg-open "$url" > /dev/null 2>&1 || true ;;
  esac
fi

if [ "$watch" = 0 ]; then
  wait "$server_pid"
  exit
fi

# Polls rather than using a file-watching tool, so it needs nothing installed on Linux or macOS. Directories count
# too: theirs change when a file is added, removed or renamed.
while kill -0 "$server_pid" 2> /dev/null; do
  sleep 1
  [ -n "$(find "$repo/shapeland/content" -newer "$stamp" -print 2> /dev/null | head -n 1)" ] || continue
  sleep 0.5 # let an editor finish saving several files
  touch "$stamp" # changes from here on start another round
  echo "== Content changed; rebuilding"
  if ! build_content; then
    echo "== The content build failed; the server keeps the old content. Fix it and save again."
    continue
  fi
  copy_content
  echo "== Restarting the game server"
  stop_server
  start_server
  if [ -n "$web" ]; then
    echo "== Restarted with the new content. Browsers reload and rejoin by themselves."
  else
    echo "== Restarted with the new content. Restart play mode to play with it."
  fi
done
echo "The game server stopped." >&2
exit 1
