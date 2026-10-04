# World structure

Agent notes on world structure and server hierarchy. Decisions made with the project lead on 2026-10-03. Housing access is in `crafts.md`.

## Decided

- **Zone borders: chokepoint handoff with a little overlap.** Borders can be wide (e.g. a valley between mountains) but each border joins exactly two zones; if more meet by accident, only the closest transition is synced. Near a border, players see a read-only view of the neighbouring zone.
- **No effects across borders:** attacks, AoEs, heals and buffs only affect entities on the same server.
- **Monsters stay in their zone;** border areas are designed with little or no combat.
- **Many zones are reached only by teleport or a single door** (e.g. dungeon entrances) and need no border sync.
- **Flying can cross zone borders.** Little flying-to-ground interaction and no aerial combat; far-away players are culled.
- **Hierarchy:** Region › Zone › Shard, and Region › Dungeon › Instance. **Worlds** are labels inside a region: they decide default shard placement and organize guilds. Several Worlds share shards in a region. (A private server would be one region.)
- **Shard preference when entering a zone:** party's shard, then home World's shard, then any shard with room.
- **Shard size:** 150–300 players would be an impressive upper limit; may be forced lower.
- **Teleport scene** (2026-10-04): a fixed-length scene where the player's soul travels along the leylines on the world map to the destination. If the destination hasn't finished loading when the travel animation ends (likely on web), the soul pulses at the destination on the map until it has. It hides loading; its details are gameplay design, not backend (handoff in `backend.md`).
- **Vehicles:** boats and airships cross zones on fixed routes. The only player-steered vehicles are 2–4 seat mounts with near-normal movement. Vehicles and their riders cross borders as one group.

## Considering

- **Data model (agent suggestion):** Region is the real unit (one database scope, owns shards and instances; a private server is one region). World is an attribute: each character has a home World, guilds belong to a World, and shard placement uses it as a preference. Changing World is a cheap data update, not a server transfer.
