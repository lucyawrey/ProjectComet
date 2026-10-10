#!/bin/sh
# Simulates network conditions on this container's link only, in both directions, then runs
# the given command.
#
# netem queues only outgoing packets, so incoming ones are redirected through an ifb device and
# "leave" it through a second netem queue. A pfifo child queue stops jitter reordering packets.
#
#   NETEM_DELAY   one-way delay, e.g. 40ms (default 0ms)
#   NETEM_JITTER  random variation on the delay, e.g. 10ms (default 0ms)
#   NETEM_LOSS    packet loss per direction, e.g. 1% (default 0%)
set -eu

dev=eth0
limit=10000 # packets; netem's default of 1000 is too small for 300 bots at 30 Hz with delay
netem="limit $limit delay ${NETEM_DELAY:-0ms} ${NETEM_JITTER:-0ms} loss ${NETEM_LOSS:-0%}"

# Outgoing.
tc qdisc add dev "$dev" root handle 1: netem $netem
tc qdisc add dev "$dev" parent 1:1 handle 10: pfifo limit "$limit"

# Incoming, redirected through ifb0.
ip link add ifb0 type ifb
ip link set ifb0 up
tc qdisc add dev "$dev" handle ffff: ingress
tc filter add dev "$dev" parent ffff: matchall action mirred egress redirect dev ifb0
tc qdisc add dev ifb0 root handle 1: netem $netem
tc qdisc add dev ifb0 parent 1:1 handle 10: pfifo limit "$limit"

echo "netem on $dev, both directions: $netem"
exec "$@"
