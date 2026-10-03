# Project Comet: agent context

Handoff notes for agent sessions on Project Comet. Last updated 2026-10-03.

## Working agreement

- **Discuss before acting.** Propose a plan and wait for agreement before writing docs, pushing, or changing branches. Don't fill in content or structure that hasn't been discussed.
- **Don't present agent suggestions as decisions.** An earlier session's context mixed agent suggestions in with the project lead's decisions. Keep three categories apart: *decided*, *considering*, and *open*.
- **Human-readable docs are the goal of this phase.** Design docs are written for people. Agent-oriented material (like this file) lives under `.claude/`.

## What this phase is

Planning only. The project lead is drafting plans for fun, and eventually to present to a small indie team they've joined. No hiring, no development yet.

## Project lead

Experienced web developer with shipped production apps; strong in deployments, orchestration and database design. Hobbyist game developer with little experience in real-time game networking. C# / .NET is the only language they're confident maintaining a critical backend in.

## Decided

- **Human-made creative content only.** Agents help with code; no AI-generated models, textures, readable in-game text, music or sound.
- **Low-poly art.**
- **Content is roughly an even split** between community-driven play (economy, crafting, trading, housing, guilds, player events) and developer-made content. Smaller in scope than an MMO from a larger team.
- **Action combat, not tab-target.** Skills have real hitboxes; there's an optional lock-on. Should be more forgiving of latency than an FPS. (Detailed design in the project lead's notes: 2D hitboxes with "height zones".)
- **Seamless world with no loading screens.** A firm requirement and the project's biggest challenge. Zone shards and dungeon instances still exist on the server side; only teleportation hides loading.
- **Private servers should be easy to self-host**, without a large proprietary dependency.
- **First milestone (adopted from an agent suggestion):** a vertical slice with one zone, the core loop, bot clients load-testing 100+ simulated players, and simulated latency from day one.

### Combat (decided 2026-10-03)

- **PvE only for the first major version.** No PvP system is planned, so no PvP netcode.
- **Dodge and invulnerability come from skills**, not universally. Defenders have invulnerability levels (none / dodge / a rarer "true" invuln); attacks have pierce levels, and some attacks hit through normal invuln.
- **Height zones are separate from invulnerability.** Zones (digging, crouching, standing, jumping, flying) are vertical position; invulnerability is defensive state. Both feed one shared "is this hit valid?" check.
- **Real height check:** height thresholds between attacker and defender add or subtract an integer offset to the zone check. Can be disabled for flat areas like boss arenas as an optimization.
- **Frame data in 30 Hz simulation ticks:** explicit startup, active and recovery frames, run in parallel with (not driven by) animations. Consistent timing matters more than visual match.
- **Fight sizes:** dedicated raids up to ~30 players; open-world bosses aim for ~100 in the worst case (revisit if not feasible).
- **No body blocking** in the initial design. "Nudging" (moving slower through other bodies) is under consideration.
- **Most attacks don't move anyone**, to keep some tab-target feel; a limited set of skills include movement.
- **Physicality without physics** is one of the game's largest goals, despite the simple graphics.

### World structure (decided 2026-10-03)

- **Zone borders: chokepoint handoff with a little overlap.** Borders can be wide (e.g. a valley between mountains) but each border joins exactly two zones; if more meet by accident, only the closest transition is synced. Near a border, players see a read-only view of the neighbouring zone.
- **No effects across borders:** attacks, AoEs, heals and buffs only affect entities on the same server.
- **Monsters stay in their zone;** border areas are designed with little or no combat.
- **Many zones are reached only by teleport or a single door** (e.g. dungeon entrances) and need no border sync.
- **Flying can cross zone borders.** Little flying-to-ground interaction and no aerial combat; far-away players are culled.
- **Hierarchy:** Region › Zone › Shard, and Region › Dungeon › Instance. **Worlds** are labels inside a region: they decide default shard placement and organize guilds. Several Worlds share shards in a region. (A private server would be one region.)
- **Shard preference when entering a zone:** party's shard, then home World's shard, then any shard with room.
- **Shard size:** 150–300 players would be an impressive upper limit; may be forced lower.
- **Vehicles:** boats and airships cross zones on fixed routes. The only player-steered vehicles are 2–4 seat mounts with near-normal movement. Vehicles and their riders cross borders as one group.

## Considering

- **Backend:** plan for two approaches:
  - A. A custom .NET stack (Gateway server, Data Center server with PostgreSQL, headless game server, game client).
  - B. A product like SpacetimeDB. The project lead is slightly biased against it because of the self-hosting requirement; verify current license terms before weighing it.
  - C (unlikely). A systems-language backend written with help from another developer, if neither A nor B works.
- **Art techniques:** palette/gradient texturing and a shared humanoid rig (agent suggestions).
- **Netcode suggestions (agent, not yet decided):**
  - Lenient PvE hit validation: accept player hits on monsters as seen by the client, within sanity limits; honour dodges that started before a hit landed on the client, up to ~150–200 ms.
  - Height zones as bitmasks; the terrain-height offset shifts the mask. Invuln vs pierce compared as integers.
  - Sync actions as events ("player X started skill Y at tick T facing Z") rather than continuous state; prioritise updates by relevance; cap displayed characters.
  - Movement skills and knockback as fixed curves in frame data, predicted by the client.
  - Nudging: server-enforced against monsters/NPCs, client-only between players.
  - Border visibility: near a border the client also listens to the neighbouring shard (ticket via the Gateway) but only sends input to the owner; handoff happens a few metres past the line to avoid ping-pong.
  - Fixed-route vehicles are positioned from route + clock, so only passengers are handed off; riders' positions are relative to the vehicle.
  - A named load test after the 100-bot milestone: one shard, 100 players, one boss.

## Open questions

- **Gateway and Data Center: one service or two?** In the custom .NET approach, should the Gateway server (public-facing, primarily HTTP API) and the Data Center server (database API layer) be the same thing or separate?
- **Are the plans sensible?** Find real-world examples of documented MMO backends (talks, postmortems, open-source servers, engineering blogs) and compare our architecture against them.

## Risks

- **Big:** netcode (sharpened by action combat and the seamless world), moderation, economy integrity.
- **New:** more players than the game can support.
- **Not a current concern:** audio budget (find a composer if this becomes real), low population (solve later; the goal now is to make the thing).

## Source material

The project lead's Obsidian notes (Quick Notes, Class Ideas, Technical Notes, Story Snippets, Conversations) cover game systems in detail: inventory and collection, companions, Class Crystals, Soul Experience, crafts, skills, world structure, aesthetic, server hierarchy, and database tables. Raw copies are in `.claude/notes/`. They are messy working notes, not decisions; check with the project lead before treating anything in them as final.

## Repository state

The three earlier repos were consolidated into `lucyawrey/ProjectComet` as archive branches with full history:

| Branch | Came from |
| --- | --- |
| `archive/dotnet-datacenter` | ProjectComet `main` (Godot + .NET "DataCenter" backend) |
| `archive/nakama` | ProjectComet `nakama` |
| `archive/rust-bevy` | `project_comet_rs` `main` |
| `archive/rust-bevy-sqlite-opfs` | `project_comet_rs` `client-sqlite-opfs` |
| `archive/rust-bevy-spacetimedb` | `project_comet_rs` `spacetimedb` |
| `archive/godot-client` | `project_comet_godot` `main` |

The archive branches are abandoned, but any of them can be mined for ideas: data models, schemas, architecture, networking experiments.

- `main` was reset on 2026-10-03 to a fresh history containing only `.claude/`. The old .NET code and its history live on `archive/dotnet-datacenter`. Human-readable design docs will be added to `main` as they're written.
- No tags are wanted.

## Next steps

1. Go through the notes together, one area at a time. **Combat and world structure are done** (see Decided). Remaining areas: classes and Class Crystals, skills, crafts, items/inventory/collection, companions, unlockables, aesthetic, database tables.
2. Research documented real-world MMO backends to sanity-check the architecture, including the Gateway/Data Center split.
3. Agree on the doc structure, then write human-readable docs.
