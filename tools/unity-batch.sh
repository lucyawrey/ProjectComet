#!/usr/bin/env bash
# Runs a Unity project's editor in batch mode and returns once its work is done. A batch-mode editor often hangs
# while quitting (seen on Linux after both -executeMethod and -runTests), so this watches the log for the line
# that ends the run, gives the editor a short grace period to exit, then kills it.
#
#   tools/unity-batch.sh [-p project] [-l logfile] [-t minutes] -- <Unity arguments>
#
#   tools/unity-batch.sh -- -runTests -testPlatform PlayMode -testResults Logs/results.xml
#   tools/unity-batch.sh -- -buildTarget WebGL -executeMethod ShapeLand.Client.Editor.Builds.WebGame -quit
#
# The project defaults to shapeland/unity, the log to Logs/batch.log and the time limit to 30 minutes. Relative
# paths, in the log and in Unity arguments, are relative to the project. Exit code: the editor's (tests: 0 passed,
# 2 failed), 1 if it failed or gave no result, 124 on the time limit.
set -euo pipefail

repo=$(cd "$(dirname "$0")/.." && pwd)
project="$repo/shapeland/unity"
log=""
minutes=30
while [ $# -gt 0 ]; do
  case "$1" in
    -p) project=$(cd "$2" && pwd); shift 2 ;;
    -l) log="$2"; shift 2 ;;
    -t) minutes="$2"; shift 2 ;;
    --) shift; break ;;
    *) echo "Unknown option $1 (Unity arguments go after --)" >&2; exit 2 ;;
  esac
done
log="${log:-Logs/batch.log}"
case "$log" in /*) ;; *) log="$project/$log" ;; esac

version=$(sed -n 's/^m_EditorVersion: *//p' "$project/ProjectSettings/ProjectVersion.txt")
case "$(uname)" in
  Darwin) unity="/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity" ;;
  *) unity="$HOME/Unity/Hub/Editor/$version/Editor/Unity" ;;
esac
[ -x "$unity" ] || { echo "No Unity $version at $unity" >&2; exit 2; }

mkdir -p "$(dirname "$log")"
: > "$log"
cd "$project"
pid=

# Stops the editor: politely, then for good if it hangs (as its quit does).
stop_unity() {
  [ -n "$pid" ] || return 0
  kill "$pid" 2> /dev/null || return 0
  for _ in 1 2 3; do
    kill -0 "$pid" 2> /dev/null || return 0
    sleep 1
  done
  kill -9 "$pid" 2> /dev/null || true
}
# A background job ignores Ctrl+C in a script, so stopping this script must stop the editor too, or it keeps the
# project locked.
# Set before it starts, and on any exit (an unexpected error too).
trap 'stop_unity; exit 130' INT
trap 'stop_unity; exit 143' TERM
trap stop_unity EXIT

# Output goes to the log only: the editor's helper processes would otherwise hold the caller's pipes open.
"$unity" -batchmode -projectPath "$project" -logFile "$log" "$@" < /dev/null > /dev/null 2>&1 &
pid=$!

# The line that ends a run, and the exit code it means. "Build Failed:" is the Builds class's own line: a failed
# build exits through EditorApplication.Exit, which logs none of the others, and can hang there.
result() {
  local line
  line=$(grep -m 1 -E 'Test run completed\. Exiting with code [0-9]+|Batchmode quit successfully invoked|Aborting batchmode|Scripts have compiler errors|^Build Failed: ' "$log" || true)
  case "$line" in
    *"Exiting with code"*) echo "$line" | sed -E 's/.*Exiting with code ([0-9]+).*/\1/' ;;
    *"Batchmode quit successfully"*) echo 0 ;;
    "") ;;
    *) echo 1 ;;
  esac
}

deadline=$(( $(date +%s) + minutes * 60 ))
code=""
while kill -0 "$pid" 2> /dev/null; do
  code=$(result)
  [ -z "$code" ] || break
  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "Unity ran past $minutes minutes; stopping it (log: $log)" >&2
    stop_unity
    exit 124
  fi
  sleep 1
done

if [ -n "$code" ]; then
  # Done: allow a normal exit, then stop a hung quit.
  for _ in $(seq 20); do
    kill -0 "$pid" 2> /dev/null || break
    sleep 1
  done
  if kill -0 "$pid" 2> /dev/null; then
    echo "Unity finished but hung while quitting; stopping it" >&2
    stop_unity
  fi
  wait "$pid" 2> /dev/null || true
else
  # Exited before any result line (a crash, a licence problem, a method that called Exit).
  set +e
  wait "$pid"
  code=$?
  set -e
  [ "$code" -ne 0 ] || code=1
  echo "Unity exited without a result line (log: $log)" >&2
fi

echo "Unity: exit $code (log: $log)"
exit "$code"
