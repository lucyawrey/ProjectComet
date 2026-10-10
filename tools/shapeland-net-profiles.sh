# ShapeLand's simulated network profiles, and a build helper, sourced by tools/shapeland-web.sh and
# tools/shapeland-netrun.sh.
# Each is netem's delay and jitter each way plus loss each way, on the game server's link (shapeland/docker).
#
#   none   nothing added
#   good   40 ms ± 10 ms and 1% loss each way (about 80 ms round trip)
#   bad    100 ms ± 25 ms and 2% loss each way (about 200 ms round trip)
#   awful  100 ms ± 25 ms and 5% loss each way

# Sets PREFIX_DELAY, PREFIX_JITTER and PREFIX_LOSS for a profile.
net_profile() {
  local delay jitter loss
  case "$2" in
    none)  delay=0ms   jitter=0ms  loss=0% ;;
    good)  delay=40ms  jitter=10ms loss=1% ;;
    bad)   delay=100ms jitter=25ms loss=2% ;;
    awful) delay=100ms jitter=25ms loss=5% ;;
    *) echo "Unknown network profile $2 (none, good, bad or awful)." >&2; exit 2 ;;
  esac
  export "$1_DELAY=$delay" "$1_JITTER=$jitter" "$1_LOSS=$loss"
}

# Exits unless the kernel modules netem needs are loaded (Linux; Docker Desktop's VM has its own).
check_net_modules() {
  local module
  [ "$(uname)" = Darwin ] && return 0
  for module in ifb sch_netem sch_ingress act_mirred cls_matchall; do
    if [ ! -d "/sys/module/$module" ]; then
      echo "Kernel module $module isn't loaded; see tools/StackBench/README.md (Docker runs)." >&2
      exit 1
    fi
  done
}

# Runs a build step quietly, showing its output only if it fails (dotnet prints compile errors to stdout).
quietly() {
  local out
  out=$("$@" 2>&1) || { printf '%s\n' "$out" >&2; echo "Failed: $*" >&2; return 1; }
}
