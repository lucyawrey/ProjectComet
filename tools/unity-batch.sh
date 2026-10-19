#!/usr/bin/env bash
# Runs a Unity build or tests and returns once the work is done: in the Unity editor that has the project open, when
# it's reachable through the Unity CLI (the project has the Unity Pipeline package), else in a batch-mode editor. A
# batch-mode editor often hangs while quitting (seen on Linux after both -executeMethod and -runTests), so this
# watches the log for the line that ends the run, gives the editor a short grace period to exit, then kills it.
#
#   tools/unity-batch.sh [-p project] [-l logfile] [-t minutes] build <profile> <output>
#   tools/unity-batch.sh [-p project] [-l logfile] [-t minutes] test [PlayMode|EditMode] [results]
#   tools/unity-batch.sh [-p project] [-l logfile] [-t minutes] -- <Unity arguments>
#
#   tools/unity-batch.sh build Web ../../artifacts/unity/shapeland/web
#   tools/unity-batch.sh test PlayMode
#
# build builds a profile in Assets/Settings/Build Profiles/. An open editor must already be on the profile's
# platform: switching would reimport the whole project. test runs a test platform (default PlayMode) and writes its
# results (default Logs/results.xml in batch mode, Logs/results.json in an open editor). Everything after -- goes
# to a batch-mode editor as it is.
#
# The project defaults to shapeland/ShapeLand.Unity, the log to Logs/batch.log and the time limit to 30 minutes. Relative
# paths, in the log, the output, the results and Unity arguments, are relative to the project. Exit code: 0 done
# (tests passed), 2 tests failed, 1 if it failed or gave no result, 124 on the time limit. Runs tools/unity-restore.sh
# first when the project's NuGet packages were never restored or packages.config changed since.
set -euo pipefail

repo=$(cd "$(dirname "$0")/.." && pwd -P)
project="$repo/shapeland/ShapeLand.Unity"
log=""
minutes=30
while [ $# -gt 0 ]; do
  case "$1" in
    -p) project=$(cd "$2" && pwd -P); shift 2 ;;
    -l) log="$2"; shift 2 ;;
    -t) minutes="$2"; shift 2 ;;
    --|build|test) break ;;
    *) echo "Unknown option $1 (Unity arguments go after --)" >&2; exit 2 ;;
  esac
done
[ $# -gt 0 ] || { echo "Say what to run: build, test or -- <Unity arguments>" >&2; exit 2; }
job="$1"
shift
log="${log:-Logs/batch.log}"
case "$log" in /*) ;; *) log="$project/$log" ;; esac
deadline=$(( $(date +%s) + minutes * 60 ))
cd "$project"

case "$job" in
  build)
    [ $# -eq 2 ] || { echo "build takes a profile and an output path" >&2; exit 2; }
    profile="$1" output="$2"
    asset="Assets/Settings/Build Profiles/$profile.asset"
    [ -f "$asset" ] || { echo "No build profile at $asset" >&2; exit 2; }
    ;;
  test)
    mode="${1:-PlayMode}" results="${2:-}"
    case "$mode" in PlayMode|EditMode) ;; *) echo "test takes PlayMode or EditMode" >&2; exit 2 ;; esac
    ;;
esac

# A fresh clone or worktree has no NuGet packages, and the editor can't compile without them.
if [ -f Assets/packages.config ] && ! [ Assets/Packages/.restored -nt Assets/packages.config ]; then
  "$repo/tools/unity-restore.sh" "$project"
fi

mkdir -p "$(dirname "$log")"
: > "$log"

# The open editor's Pipeline port, if one has this project open and ready. unity status fails when none is open.
editor_port() {
  command -v unity > /dev/null 2>&1 && command -v jq > /dev/null 2>&1 || return 0
  { unity status --json --non-interactive 2> /dev/null || true; } \
    | jq -r --arg p "$project" '.data.instances[]? | select(.project == $p and .state == "ready") | .port' | head -n 1
}

# Runs a command in the open editor; prints its result as JSON, or fails with its error. Build reports and test
# messages hold raw control characters (tabs, line breaks), which JSON strings don't allow, so they become spaces.
in_editor() {
  local out
  out=$(unity command "$@" --project-path "$project" --json --non-interactive --no-pager --no-banner 2>&1 \
    | LC_ALL=C tr '\000-\037' ' ') || {
    echo "$out" | jq -r '.errors[]?.message // empty' 2> /dev/null >&2 || echo "$out" >&2
    return 1
  }
  echo "$out" | jq '.data.result'
}

# Polls a status command until its status is one of the given ones, then prints its result.
await() {
  local command="$1" result status end
  shift
  while :; do
    result=$(in_editor "$command") || return 1
    status=$(echo "$result" | jq -r '.status')
    for end in "$@"; do [ "$status" = "$end" ] && { echo "$result"; return 0; }; done
    if [ "$(date +%s)" -ge "$deadline" ]; then
      echo "Unity ran past $minutes minutes; stopped waiting (it may still be running in the editor)" >&2
      return 124
    fi
    sleep 2
  done
}

port=""
[ "$job" = "--" ] || port=$(editor_port)

if [ -n "$port" ]; then
  echo "== In the open Unity editor (port $port)"
  case "$job" in
    build)
      want=$(sed -n 's/^  m_BuildTarget: *//p' "$asset")
      have=$(in_editor eval --code 'return ((int)EditorUserBuildSettings.activeBuildTarget).ToString();' | jq -r '.result')
      if [ "$want" != "$have" ]; then
        echo "The open editor is on another platform than the $profile profile; switch to the profile there (File › Build Profiles) or close the editor." >&2
        exit 1
      fi
      case "$output" in /*) ;; *) output="$project/$output" ;; esac
      in_editor build --confirm true --profileName "$profile" --outputPath "$output" > /dev/null
      set +e
      result=$(await build_status completed)
      code=$?
      set -e
      [ "$code" -eq 0 ] || exit "$code"
      echo "$result" > "$log"
      outcome=$(echo "$result" | jq -r '.result // "Unknown"')
      echo "$result" | jq -r '.errors[]? | tostring'
      echo "Unity: build $outcome (report: $log)"
      [ "$outcome" = "Succeeded" ] || exit 1
      ;;
    test)
      results="${results:-Logs/results.json}"
      case "$results" in /*) ;; *) results="$project/$results" ;; esac
      trap 'in_editor cancel_tests > /dev/null 2>&1; exit 130' INT TERM
      in_editor run_tests --mode "$mode" --async_tests true > /dev/null
      set +e
      result=$(await test_status completed failed)
      code=$?
      set -e
      [ "$code" -eq 0 ] || exit "$code"
      mkdir -p "$(dirname "$results")"
      echo "$result" > "$results"
      echo "$result" | jq -r '.results[]? | select(.Status == "Failed") | "Failed: \(.FullName)\n\(.Message // "")"'
      read -r total failed < <(echo "$result" | jq -r '"\(.summary.total // 0) \(.summary.failed // 0)"')
      echo "Unity: $total tests, $failed failed (results: $results)"
      [ "$total" -gt 0 ] || exit 1
      [ "$failed" -eq 0 ] || exit 2
      ;;
  esac
  exit 0
fi

if [ -e "Temp/UnityLockfile" ] && pgrep -f "[Pp]roject[Pp]ath.*$project" > /dev/null 2>&1; then
  echo "A Unity editor has the project open but can't be reached through the Unity CLI (installed, with jq?); close it first." >&2
  exit 1
fi

case "$job" in
  # -build alone goes on to open the project once the build is done.
  build) set -- -activeBuildProfile "$asset" -build "$output" -quit ;;
  test) set -- -runTests -testPlatform "$mode" -testResults "${results:-Logs/results.xml}" ;;
esac

version=$(sed -n 's/^m_EditorVersion: *//p' "$project/ProjectSettings/ProjectVersion.txt")
case "$(uname)" in
  Darwin) unity="/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity" ;;
  *) unity="$HOME/Unity/Hub/Editor/$version/Editor/Unity" ;;
esac
[ -x "$unity" ] || { echo "No Unity $version at $unity" >&2; exit 2; }
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

# The line that ends a run, and the exit code it means.
result() {
  local line
  line=$(grep -m 1 -E 'Test run completed\. Exiting with code [0-9]+|Build Finished, Result: |Batchmode quit successfully invoked|Aborting batchmode|Scripts have compiler errors' "$log" || true)
  case "$line" in
    *"Exiting with code"*) echo "$line" | sed -E 's/.*Exiting with code ([0-9]+).*/\1/' ;;
    *"Build Finished, Result: Success"*|*"Batchmode quit successfully"*) echo 0 ;;
    "") ;;
    *) echo 1 ;;
  esac
}

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
  # Exited before any result line (a crash, a license problem, a method that called Exit).
  set +e
  wait "$pid"
  code=$?
  set -e
  [ "$code" -ne 0 ] || code=1
  echo "Unity exited without a result line (log: $log)" >&2
fi

echo "Unity: exit $code (log: $log)"
exit "$code"
