# Netcode

**Layer: base (Comet).**

Agent notes on networking.

## Decided

- **WebSocket is the worst case the design targets,** and every client starts on it (`backend.md`).
- **Lenient PvE hit validation:** accept player hits on monsters as seen by the client, within sanity limits; honour dodges that started before a hit landed on the client, up to ~150–200 ms.
- **Height zones as bitmasks;** the terrain-height offset shifts the mask. Invuln vs pierce compared as integers.
- **Actions sync as events** ("player X started skill Y at tick T facing Z") rather than continuous state; updates are prioritised by relevance; displayed characters are capped.
- **Movement skills and knockback are fixed curves in frame data,** predicted by the client.
- **Border visibility:** near a border the client also listens to the neighbouring channel (ticket via the Login server) but only sends input to the owner; handoff happens a few metres past the line to avoid ping-pong.
- **Fixed-route vehicles are positioned from route + clock,** so only passengers are handed off; riders' positions are relative to the vehicle.
- **A named load test after the 100-bot milestone:** one channel, 100 players, one boss.
- **Movement authority: the client sends its position and the server validates it** (WoW-style). The server rejects impossible moves (speed, walls) and snaps the player back. Chosen for PvE-only play, cheap server CPU at 100+ players, and tolerance of WebSocket stalls; cheat defence depends on good validation rules.
- **Three message kinds:** client *inputs*; server *events* (skill started, damage, spawns, items), never dropped or merged; server *state* (positions, HP), latest-only, so a newer update replaces an unsent older one in the send queue.
- **Entity replication: spawn, field deltas, despawn:** full state when an entity becomes visible to a client, then only changed fields, then a despawn.
- **State send rate: priority accumulator, capped at 30 Hz.** Each tick, every entity a client can see adds its priority (distance, threat, party membership…) to a running score; the server sends the highest scores that fit the client's budget and resets them. The cap is one update per tick (30 Hz, the simulation rate). Events go out every tick regardless. Prior art: Unreal's per-actor update frequency and Replication Graph, the Tribes/Torque networking model, Halo: Reach's prioritisation, Glenn Fiedler's priority accumulator (from memory, not verified).
- **Grid interest management with a per-client budget:** zones are split into cells of a few dozen metres (cells subdivide zone chunks; size tuned in the prototype); a client sees entities in its own and the surrounding cells; the priority accumulator fills its send budget. The client-side display cap draws cheap placeholders (or nothing) past the cap.
- **Clients track the server tick:** regular pings estimate the current server tick; inputs and events are stamped with ticks, so dodge grace and hit validation compare ticks rather than arrival times.
- **Reconnect is a fresh snapshot:** a reconnecting client gets full state for everything in view, the same path as entering a zone.
- **Framing:** one binary WebSocket frame per client per tick: the server tick, then a list of messages, each a numeric message ID, a length and a MessagePack payload. The length lets older clients skip unknown message types. State messages are keyed by message ID plus entity, so the send queue can replace superseded ones. Clients do the same: one frame per client tick, only when there's something to send.
- **Client position reports:** position, velocity, facing and the client's server-tick estimate, about 15 Hz while moving (tuned in the load test), sent immediately on starting, stopping or turning sharply, and not at all while standing still.
- **Movement validation** (tolerances tuned in the prototype): distance since the last report must fit max speed × elapsed ticks with ~20% tolerance, allowing for active movement skills, knockback curves and mounts; after a stall, a burst of reports is checked against the total elapsed time so hiccups don't cause snap-backs. The path between reports must not cross a collision volume, and the position must fit the player's state (grounded, or flying with mount and attunement). On failure the server sends a correction and the client blends to it over a few frames.
- **Repeated movement violations are logged and flagged for moderation,** never auto-banned.

## Considering

- **Techniques for WebSocket as the worst case:** server send queues that keep only the latest state; clients jump to the latest state after a stall instead of replaying; the server accepts timestamped inputs arriving in bursts, within limits; ~100–150 ms interpolation buffers for other players; `TCP_NODELAY` on the server.
- **Nudging (if adopted, `combat.md`):** server-enforced against monsters and NPCs, client-only between players.
- **Context for the 100-player boss:** sending full state 30 times a second to 100 players would be roughly 0.5 Mbps down per player and ~50 Mbps up from the server. Event-based actions, relevance prioritisation and display caps are how this gets cut down.

## Rejected

- **Server-authoritative movement from inputs** (the server simulates movement from client inputs): more server CPU at 100+ players and less tolerant of WebSocket stalls than client-reported positions with validation.

## Open

- Nothing open right now.
