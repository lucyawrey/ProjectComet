#!/usr/bin/env bash
# Runs the stack benchmark in Docker and checks the results against thresholds.json.
#
#   tools/StackBench/docker/run.sh clean|impaired
#
# Optional: BENCH_BOTS (300), BENCH_RAMP (10), BENCH_WARMUP (60), BENCH_DURATION (600) seconds,
# BENCH_GC_LATENCY_MODE (e.g. SustainedLowLatency; default unset),
# SERVER_CPU (2): the CPU the server is pinned to; bots get every CPU except that core.
set -euo pipefail

profile=${1:-}
case "$profile" in
  clean)    export NETEM_DELAY=0ms  NETEM_JITTER=0ms ;;
  impaired) export NETEM_DELAY=40ms NETEM_JITTER=10ms ;;
  *) echo "usage: $0 clean|impaired" >&2; exit 2 ;;
esac
export NETEM_LOSS=1%

here=$(cd "$(dirname "$0")" && pwd)
bench=$(dirname "$here")
repo=$(cd "$bench/../.." && pwd)

for module in ifb sch_netem sch_ingress act_mirred cls_matchall; do
  if [ ! -d "/sys/module/$module" ]; then
    echo "Kernel module $module isn't loaded; see tools/StackBench/README.md (Docker runs)." >&2
    exit 1
  fi
done

# Pin the server to one CPU, and keep the bots off that whole physical core (its SMT sibling too).
server_cpu=${SERVER_CPU:-2}
siblings=$(cat "/sys/devices/system/cpu/cpu$server_cpu/topology/thread_siblings_list")
online=$(cat /sys/devices/system/cpu/online)
expand() { # "0-3,8" -> "0 1 2 3 8"
  local part
  for part in ${1//,/ }; do
    if [[ $part == *-* ]]; then seq "${part%-*}" "${part#*-}"; else echo "$part"; fi
  done
}
excluded=" $(expand "$siblings" | tr '\n' ' ') "
bot_cpus=()
for cpu in $(expand "$online"); do
  [[ $excluded == *" $cpu "* ]] || bot_cpus+=("$cpu")
done
export SERVER_CPUS=$server_cpu
export BOT_CPUS=$(IFS=,; echo "${bot_cpus[*]}")

echo "Profile: $profile (delay ${NETEM_DELAY} ± ${NETEM_JITTER} each way, loss ${NETEM_LOSS} each way)"
echo "Server on CPU $SERVER_CPUS; bots on CPUs $BOT_CPUS"

mkdir -p "$bench/results" # as you, before Docker would create it as root
rm -f "$bench/results/server.json" "$bench/results/bots.json"
compose=(docker compose -f "$here/compose.yaml")
# Containers and the network go even if the run fails or is interrupted.
trap '"${compose[@]}" down > /dev/null 2>&1 || true' EXIT
"${compose[@]}" up --build --abort-on-container-failure

cd "$repo"
dotnet run -c Release --project tools/StackBench/StackBench.Report -- \
  tools/StackBench/results/server.json tools/StackBench/results/bots.json tools/StackBench/thresholds.json "$profile"
