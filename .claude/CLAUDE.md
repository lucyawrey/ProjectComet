# Project Comet: agent context

Handoff notes for agent sessions on Project Comet. Last updated 2026-10-04.

## Working agreement

- **Discuss before acting.** Propose a plan and wait for agreement before writing docs, pushing, or changing branches. Don't fill in content or structure that hasn't been discussed.
- **Don't present agent suggestions as decisions.** An earlier session's context mixed agent suggestions in with the project lead's decisions. Keep three categories apart: *decided*, *considering*, and *open*.
- **Confirm adoptions with prompts.** When it seems we agree on something explicit (often an agent suggestion), ask the project lead to adopt it with a prompt (the question tool), one question per feature when there are several, before recording it as decided. Decisions the project lead states directly can be recorded as stated.
- **Human-readable docs are the goal of this phase.** Design docs are written for people. Agent-oriented material (like this file) lives under `.claude/`.
- **Authorship.** Technical notes (setup, tooling, data models) may be written by agents. Overarching project descriptions and any prose meant for the people reading the proposal are written by the project lead. Agents may leave placeholders for that prose but don't fill them.
- **Once human-readable docs exist, they are the source of truth**, not chat context or `.claude/` notes. They will be in the project lead's voice and partly written by them directly. Agents can write drafts; some draft text may survive, but only by the project lead carrying it over manually into their own next draft. When the docs and this file disagree, the docs win; update this file to match.

## What this phase is

Planning only. Don't hash out precise UI and control details yet. Multiple characters per account will exist, but this design phase ignores multi-character flows for simplicity. The project lead is drafting plans for fun, and eventually to present to a small indie team they've joined. No hiring, no development yet.

The goal of the whole project is a proposal, not a game. Even so, the project lead may want to actually build small vertical prototypes (for example, the two-stage 100-player load test in `backend.md`). Planning work should keep that in mind; don't start building anything without discussing it first.

## Project lead

Experienced web developer with shipped production apps; strong in deployments, orchestration and database design. Hobbyist game developer with little experience in real-time game networking. C# / .NET is the only language they're confident maintaining a critical backend in.

## Decided

- **Human-made creative content only.** Agents help with code; no AI-generated models, textures, readable in-game text, music or sound.
- **Low-poly art.**
- **Content is roughly an even split** between community-driven play (economy, crafting, trading, housing, guilds, player events) and developer-made content. Smaller in scope than an MMO from a larger team.
- **Action combat, not tab-target.** Skills have real hitboxes; there's an optional lock-on. Should be more forgiving of latency than an FPS. (Detailed design in the project lead's notes: 2D hitboxes with "height zones".)
- **Seamless world with no loading screens.** A firm requirement and the project's biggest challenge. Channels (copies of open zones) and dungeon instances still exist on the server side; only teleportation hides loading.
- **Private servers should be easy to self-host**, without a large proprietary dependency.
- **Unity is the engine, and web export is a main goal** (classic in-browser MMO play). No headless Unity server or Unity networking package for now (FishNet + headless Unity is the fallback); the game server will likely be pure C#. Netcode is designed for WebSocket, and all clients start on it. Dropping web was considered and rejected: browser play (think RuneScape's Java-applet days) is the project's main appeal.
- **First milestone (adopted from an agent suggestion):** a vertical slice with one zone, the core loop, bot clients load-testing 100+ simulated players, and simulated latency from day one.

## Design notes by topic

Detailed agent notes live in `.claude/design/`. Each file keeps **Decided**, **Considering** and **Open** apart. Read the relevant file before discussing a topic, and record new decisions there (not here).

| File | Covers | Status |
| --- | --- | --- |
| `glossary.md` | Working terms (Skills, Abilities, Runes, Anima…) | Agreed |
| `combat.md` | Hit checks, height zones, invulnerability, frame data, fight sizes | Done |
| `world.md` | Zone borders, hierarchy, channels and instances, dungeons, flying, vehicles | Done |
| `classes.md` | Class Crystals, promotion, XP and Soul XP, Anima | Done; a few open questions |
| `skills.md` | Skills, Abilities, Runes, slots, gear Runes, Outfit Magic | Done; a few open questions |
| `crafts.md` | Crafting, gathering, housing | Done |
| `items.md` | Inventory, Storage, bags, currency, gear and outfit sets, soulbound items, tradeability, collection | Done; a few open questions |
| `companions.md` | Companion identity, locations, tasks, levelling, mounts, trading | Done; a few open questions |
| `unlockables.md` | Unlockables as flags, attunements, Anima Capacity | Done |
| `netcode.md` | Adopted netcode approach | Done |
| `art.md` | Art direction, references | Done; texture approach leaning, a few prototype questions |
| `backend.md` | Engine, backend options, research on real MMO backends, data-model suggestions | In progress |

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
- **Work lands on `main`.** On 2026-10-04 a session's work sat unmerged on `claude/determined-cori-8mpjme` and the next session started from stale notes. At the start of a session, run `git fetch` and check for remote branches ahead of `main` before trusting these notes.

## Next steps

1. Go through the notes together, one area at a time. **Combat, world structure, classes, skills, crafts, items, companions and unlockables are done** (see `.claude/design/`); everything will likely be reviewed again later. Gameplay is paused for backend architecture (2026-10-04); **aesthetic is done for now** (2026-10-04, `art.md`; the texture approach leans towards a mix and waits on the project lead understanding the workflows better; outfit fitting and filtering wait for prototypes). Remaining area: database tables.
2. Backend architecture (see `backend.md`): research, service map, persistence, handoff, transport, library stack, the two-stage load test, shared libraries (Protocol, Content, Simulation, Data), content as files in git, and an engine-neutral zone format are decided. Teleports and instances are also done (gameplay rules in `world.md`, mechanics in `backend.md`). The game-server message layer is designed too (`netcode.md`). Region server scaling, channel lifecycle and channel social design are decided (2026-10-04): "layer" was renamed "channel", channels are named and visible, Worlds were dropped in favour of constellations, characters can join several guilds, and `world.md` has nothing open. Ops tooling is decided too (2026-10-04): moderation and admin tools (roles, admin panel in the Login server, reports, a 24-hour chat buffer instead of chat logs, an item and currency ledger, sanctions, rollbacks, safeguards against moderator abuse), load-test metrics, and maintenance-window deploys. Keep the number of services small at this stage. Content-file details are decided too (2026-10-04): TOML 1.1 with Tomlyn, one file per entity, string keys mapped to permanent numbers by a committed registry, C# types as the schema, hashed content downloads, inline text extracted by the build. Remaining backend topic: the parked zone-format details.
3. **Todo:** find open-source or royalty-free low-poly assets for prototypes. They must be human-made (no AI-generated assets): check each pack's licence and authorship. Starting points to check: Kenney, Quaternius and KayKit (CC0 packs), Poly Pizza and OpenGameArt (mixed licences, check per asset).
4. Agree on the doc structure, then write human-readable docs.
5. **Maybe later:** an illustrated page explaining the 3D asset workflow for the project lead (UV unwrapping, trim sheets, vertex colours, skinning to one skeleton). A plain-text walkthrough was given on 2026-10-04.
