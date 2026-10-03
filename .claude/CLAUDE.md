# Project Comet: agent context

Handoff notes for agent sessions on Project Comet. Last updated 2026-10-03.

## Working agreement

- **Discuss before acting.** Propose a plan and wait for agreement before writing docs, pushing, or changing branches. Don't fill in content or structure that hasn't been discussed.
- **Don't present agent suggestions as decisions.** An earlier session's context mixed agent suggestions in with the project lead's decisions. Keep three categories apart: *decided*, *considering*, and *open*.
- **Human-readable docs are the goal of this phase.** Design docs are written for people. Agent-oriented material (like this file) lives under `.claude/`.
- **Once human-readable docs exist, they are the source of truth**, not chat context or `.claude/` notes. They will be in the project lead's voice and partly written by them directly. Agents can write drafts; some draft text may survive, but only by the project lead carrying it over manually into their own next draft. When the docs and this file disagree, the docs win; update this file to match.

## What this phase is

Planning only. Don't hash out precise UI and control details yet. Multiple characters per account will exist, but this design phase ignores multi-character flows for simplicity. The project lead is drafting plans for fun, and eventually to present to a small indie team they've joined. No hiring, no development yet.

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

## Design notes by topic

Detailed agent notes live in `.claude/design/`. Each file keeps **Decided**, **Considering** and **Open** apart. Read the relevant file before discussing a topic, and record new decisions there (not here).

| File | Covers | Status |
| --- | --- | --- |
| `glossary.md` | Working terms (Skills, Abilities, Runes, Anima…) | Agreed |
| `combat.md` | Hit checks, height zones, invulnerability, frame data, fight sizes | Done |
| `world.md` | Zone borders, hierarchy, shards, flying, vehicles | Done |
| `classes.md` | Class Crystals, promotion, XP and Soul XP, Anima | Done; a few open questions |
| `skills.md` | Skills, Abilities, Runes, slots, gear Runes, Outfit Magic | Done; a few open questions |
| `crafts.md` | Crafting, gathering, housing | Done |
| `items.md` | Inventory, Storage, bags, currency, gear and outfit sets, soulbound items, tradeability, collection | Done; a few open questions |
| `netcode.md` | Adopted netcode approach | Done |
| `art.md` | Art direction (not yet discussed) | Not started |
| `backend.md` | Backend options, data-model suggestions, open architecture questions | Mostly open |

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

1. Go through the notes together, one area at a time. **Combat, world structure, classes, skills, crafts and items are done** (see `.claude/design/`); everything will likely be reviewed again later. **Resume with companions.** Remaining areas after that: unlockables, aesthetic, database tables.
2. Research documented real-world MMO backends to sanity-check the architecture, including the Gateway/Data Center split.
3. Agree on the doc structure, then write human-readable docs.
