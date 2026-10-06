# Netcode

Comet's netcode is built for the worst case: a browser on a WebSocket connection. Every client, desktop included, starts on WebSocket, so there is one code path to build and load-test. Some networks block UDP anyway, so the game has to play acceptably over TCP; faster transports (UDP on desktop, WebTransport on the web) can be added later behind the same interface if load tests show they're needed.

## Authority

The server is the authority on everything that matters, with two deliberate allowances that suit PvE:

- **Movement:** the client sends its position and the server checks it, rejecting impossible moves (too fast, through walls) and snapping the player back smoothly. This keeps server cost low with 100+ players and tolerates connection hiccups. Movement skills and knockback follow fixed curves from frame data, which the client predicts.
- **Hits:** player hits on monsters are accepted as the client saw them, within sanity limits, and a dodge that started before a hit landed on the player's screen is honoured (up to about 150–200 ms).

Repeated movement violations are logged and flagged for moderators, never auto-banned.

## Messages

There are three kinds of message:

- **Inputs** from the client.
- **Events** from the server (a skill started, damage, a spawn, an item) which are never dropped or merged.
- **State** from the server (positions, health) which is latest-only: a newer update replaces an unsent older one, so a stalled connection catches up instead of replaying the past.

Actions are sent as events ("player X started skill Y at tick T facing Z") rather than as continuous state. Entities appear with a full snapshot, then send only changed fields, then disappear. A reconnecting client simply gets a fresh snapshot.

Each tick, the server sends one binary frame per client containing a list of messages. Clients send one frame per tick only when they have something to say; position reports go out about 15 times a second while moving, immediately on starting, stopping or turning sharply, and not at all while standing still.

## Time

The simulation runs at 30 ticks a second. Clients estimate the server's current tick from regular pings, and inputs and events carry tick numbers, so dodge windows and hit checks compare ticks rather than arrival times.

## Keeping bandwidth down

Sending everything about 100 players to 100 players 30 times a second would be far too much. Comet cuts it down in layers:

- **Interest management:** zones are divided into cells a few dozen metres across, and a client only hears about its own and neighbouring cells.
- **Priorities:** each visible entity builds up priority over time (closer, threatening or in your party ranks higher). Each tick the server sends the highest-priority updates that fit the client's budget, up to 30 a second.
- **Display caps:** past a certain number of nearby characters, the client draws cheap stand-ins or nothing.

## Testing it

The netcode stack is the project's biggest technical risk, so it is tested before anything else, in two stages:

1. **Stack only:** a bare server sending position-sized messages to 300 bots over WebSocket at 30 Hz, with no game logic. It runs twice: once with 1% packet loss, and once with about 80 ms of added latency as well. Passing means steady tick times and short pauses on one server core, no long gaps between updates for the bots, and under 10 KB/s per player.
2. **Full game:** the real simulation, interest management and message layer.

If the first stage fails, the stack or language is the problem; if only the second fails, our code is. Pass/fail thresholds are written down before each run, and the metrics (tick time, garbage-collection pauses, queue depth, bandwidth, round-trip time, gaps between updates) are collected from the first run. A named test follows the 100-bot vertical slice in phase 1: one channel, 100 players, one boss.

## Open questions

- The exact tick budget, send rates and tolerances, all tuned in the prototypes.
- Whether and when to add UDP or WebTransport.
