# Netcode

Agent notes on networking. These were agent suggestions adopted by the project lead on 2026-10-03.

## Decided

- **Lenient PvE hit validation:** accept player hits on monsters as seen by the client, within sanity limits; honour dodges that started before a hit landed on the client, up to ~150–200 ms.
- Height zones as bitmasks; the terrain-height offset shifts the mask. Invuln vs pierce compared as integers.
- Sync actions as events ("player X started skill Y at tick T facing Z") rather than continuous state; prioritise updates by relevance; cap displayed characters.
- Movement skills and knockback as fixed curves in frame data, predicted by the client.
- Nudging: server-enforced against monsters/NPCs, client-only between players.
- Border visibility: near a border the client also listens to the neighbouring shard (ticket via the Gateway) but only sends input to the owner; handoff happens a few metres past the line to avoid ping-pong.
- Fixed-route vehicles are positioned from route + clock, so only passengers are handed off; riders' positions are relative to the vehicle.
- A named load test after the 100-bot milestone: one shard, 100 players, one boss.

## Considering

- **Context for the 100-player boss:** sending full state 30 times a second to 100 players would be roughly 0.5 Mbps down per player and ~50 Mbps up from the server. Event-based actions, relevance prioritisation and display caps are how this gets cut down.
