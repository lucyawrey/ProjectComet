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
- **Teleport casts can be cancelled, and teleporting in combat is allowed** (2026-10-04), but cast times are long enough that trying it mid-fight will usually get you killed.
- **Dungeon loading rooms** (2026-10-04): the whole party has to be physically present (in the spirit of classic MMOs). Once the whole party is in the loading room and one member steps into the dungeon proper, the loading room's entrance closes and the dungeon starts. Everyone else still walks in on their own.
- **Re-entering an instance** (2026-10-04): a player who disconnects or leaves can re-enter the same instance.
  - Instances with no player limit: re-entry is allowed any time until the instance no longer exists.
  - Instances with a fixed player limit: the player's place is held, but the party can vote to remove them. Removal is for disconnected players and for troublesome ones alike, and carries no negative connotation. Problematic players are reported through the separate report feature. (Avoid the term "kick".)
- **When an instance closes** (2026-10-04): if everyone has left through gameplay (walking out of an exit, teleporting away, or clearing the dungeon and leaving), it closes instantly. If any player is missing because of a disconnect, it stays open for a few minutes so they can return. The game server can tell the two apart: leaving is a handoff, a disconnect is a dropped connection.
- **Dungeon exits vary by design** (2026-10-04): a separate exit, a tunnel back to the entrance, or a teleport at the end (to the entrance or elsewhere).
- **Leaving a dungeon into an outdoor zone uses the shard preference rule** (2026-10-04), preferring the shard the party came from when it has room.
- **No dungeon lockouts** (2026-10-04): the Anima cost of locked content and gathering the party are the only limits on reruns.
- **If a game server crashes mid-dungeon** (2026-10-04), everyone is placed back at the dungeon entrance, the Anima spent on entry is refunded, and loot already taken stays taken (it was saved immediately).
- **Loading rooms need not look like rooms** (2026-10-04): they can appear to be part of the open world (a mountain top, a grove past a narrow gap in the trees). Their static scenery may show up in open-world LODs, but players and other live entities are only synced across the official entrance, never across the rest of the room's edge.
- **Vehicles:** boats and airships cross zones on fixed routes. The only player-steered vehicles are 2–4 seat mounts with near-normal movement. Vehicles and their riders cross borders as one group.

## Considering

- **Unstable teleport stations** (project lead idea, 2026-10-04): a cheap way to gather a party at a dungeon, with consent. Unlike the stable stations in settlements, they start unstable; one player stabilises the station, then every party member is prompted to leyline-teleport there.
- **Data model (agent suggestion):** Region is the real unit (one database scope, owns shards and instances; a private server is one region). World is an attribute: each character has a home World, guilds belong to a World, and shard placement uses it as a preference. Changing World is a cheap data update, not a server transfer.
