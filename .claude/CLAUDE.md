# Comet and Project Anima: agent context

Handoff notes for agent sessions. Last updated 2026-10-05.

**Names:** *Comet* is the shared base (servers, libraries, Unity package, tools); *Project Anima* is the working name of the game built on it; *ShapeLand* is Comet's tiny demo and reference game. The repository is still called `ProjectComet`.

## Working agreement

- **Discuss before acting.** Propose a plan and wait for agreement before writing docs, pushing, or changing branches. Don't fill in content or structure that hasn't been discussed.
- **Keep decided, considering and open apart.** Don't present agent suggestions as decisions.
- **Confirm adoptions with prompts.** When it seems we agree on something explicit (often an agent suggestion), ask the project lead to adopt it with a prompt (the question tool), one question per feature, before recording it as decided. Decisions the project lead states directly can be recorded as stated.
- **Human-readable docs are the goal of this phase.** They're written for people and live in `docs/`. Agent-oriented material (like this file) lives under `.claude/`.
- **Authorship.** Technical notes (setup, tooling, data models) may be written by agents. Overarching project descriptions and prose for readers of the proposal (pitch, pillar wording, what makes it different, the Comet overview, the setting) are the project lead's. Agents only draft them when asked, and never the setting. The final database schema is the project lead's; agent table designs are input.
- **Once human-readable docs exist, they are the source of truth,** not chat context or `.claude/` notes. Agent drafts survive only if the project lead carries them over into their own pages. When the docs and these notes disagree, the docs win; update the notes to match.

## What this phase is

Planning, for a proposal the project lead will present to a small indie team they've joined. No hiring and no development yet. Don't hash out precise UI and control details. Multiple characters per account will exist, but this phase ignores multi-character flows.

The project lead may build small prototypes (for example the two-stage load test in `backend.md`); don't start building anything without discussing it first.

## Project lead

Experienced web developer with shipped production apps; strong in deployments, orchestration and database design. Hobbyist game developer with little experience in real-time game networking. C# / .NET is the only language they're confident maintaining a critical backend in.

## Core decisions

- **Two layers:** Comet, a shared base for a family of similar MMOs, and Project Anima as one game on it. Comet keeps thin mechanisms; game policy is game code. Details in `proposal.md`.
- **Human-made creative content only.** Agents help with code; no AI-generated models, textures, readable in-game text, music or sound.
- **Low-poly art,** PS1-to-GameCube fidelity.
- **Content is roughly an even split** between community-driven play (economy, crafting, trading, housing, guilds, player events) and developer-made content.
- **Action combat** with real hitboxes, height zones and an optional lock-on; forgiving of latency; PvE only.
- **Seamless world with no loading screens;** only teleports hide loading. The project's biggest technical challenge.
- **Private servers are easy to self-host,** without a large proprietary dependency.
- **Unity client with web export as a main goal;** plain .NET servers; WebSocket for every client.
- **First milestone:** a vertical slice of ShapeLand in one zone, with 100+ bot clients and simulated latency from day one.

## Design notes

Detailed agent notes live in `.claude/design/`. Read the relevant file before discussing a topic, and record new decisions there (not here).

**Notes describe the current state only.** Superseded text, old names and "replaces earlier…" stories are removed; git keeps history. Each file keeps **Decided**, **Considering**, **Rejected** and **Open** apart; Rejected is one line plus the reason, so dropped ideas aren't re-proposed. Decided items carry no dates or "adopted from an agent suggestion" tags; "(project lead)" marks stay only where authorship still matters, and Considering items may say whose idea they are. Each file starts with a **Layer** line (base, game or mixed).

| File | Covers | Open items |
| --- | --- | --- |
| `glossary.md` | Working terms | — |
| `proposal.md` | Two layers, ShapeLand, vision, business model, audience, roadmap, core loop, economy, new players, character creation, age rating, licences, accessibility, privacy, security, comparables | Prototyping early (considering) |
| `backend.md` | Services, handoff, transport, libraries, load testing, zones, content pipeline, database architecture, moderation and operations; research | Client mods, WebTransport, deferred ops items |
| `netcode.md` | Messages, replication, movement validation, interest management | — |
| `world.md` | Hierarchy, borders, channels, constellations, teleports, dungeons | — |
| `combat.md` | Hit checks, height zones, frame data, attack patterns, fight sizes | Nudging (considering) |
| `classes.md` | Class Crystals, promotion, XP and Soul XP, Anima | — |
| `skills.md` | Skills, Abilities, Runes, Outfit Magic | Core slot types |
| `items.md` | Containers, gear and outfit sets, soulbound items, tradeability, flags, currency | — |
| `crafts.md` | Crafting and gathering | — |
| `housing.md` | Housing, guild halls, temporary structures | Housing plots |
| `companions.md` | Companion identity, locations, tasks, levelling, mounts | Capture details (content) |
| `unlockables.md` | Unlockables as flags, attunements, Anima Capacity | — |
| `art.md` | Art direction, references | Texture approach, outfit fitting |
| `database.md` | Draft tables per area, with a Layer column | The project lead's final schema |
| `lore-hooks.md` | What gameplay implies about the setting: raw material for the project lead's lore, not lore | — |

## Docs and drafts

- **`docs/`** holds the human-readable proposal, in Markdown on `main`, one page per topic, **named by page title with spaces and capitals** (the docs don't follow the repo's lowercase convention). `docs/Proposal.md` is the front page; `docs/Roadmap.md`; `docs/Comet/` (Comet Overview, Architecture, Netcode, World and Zones, Combat Core, Content Pipeline, Operations, Self-Hosting, ShapeLand); `docs/Project Anima/` (Setting, Core Loop and Endgame, Classes and Skills, Combat, Items and Economy, Crafting and Housing, Companions, World and Travel, New Players, Community, Art and Audio, Accessibility and Translation, Business and Audience). Pages start as a title and an agent checklist (in an HTML comment marker) linking to `.claude/design/`, which the project lead deletes as they write.
- **`docs/+ Notes/`** holds the project lead's own notes. Agents don't edit it.
- **`.claude/drafts/`** holds agent drafts of every docs page, mirroring the layout (Setting has none). Agent drafts go here, never in `docs/`.
- **`.claude/research/`** holds agent research passes (`comparables.md`: comparable and inspiration games, with the project lead's reasons for each; `assets.md`: free low-poly asset sources and what to check, nothing chosen).
- **The repo is an Obsidian vault** (config in `.obsidian/` at the root, inline titles hidden), which the project lead uses to write the docs.

## Source material

The project lead's older Obsidian notes (Quick Notes, Class Ideas, Technical Notes, Story Snippets, Conversations) are copied raw into `.claude/notes/`. They are messy working notes, not decisions, and some of their ideas have since been rejected; check the design notes first.

## Risks

- **Big:** netcode (sharpened by action combat and the seamless world), moderation, economy integrity, more players than the game can support.
- **Not a current concern:** audio budget, low population.

## Repository

- **Work lands on `main`.** At the start of a session, run `git fetch` and check for remote branches ahead of `main` before trusting these notes.
- **Archive branches** hold earlier, abandoned attempts that can be mined for ideas (data models, schemas, architecture, networking experiments): `archive/dotnet-datacenter` (Godot + .NET "DataCenter" backend, including `docs/reference.sql`), `archive/nakama`, `archive/rust-bevy`, `archive/rust-bevy-sqlite-opfs`, `archive/rust-bevy-spacetimedb`, `archive/godot-client`.
- No tags are wanted.

## Next steps

1. **The project lead writes the docs pages,** using the checklists and the drafts in `.claude/drafts/`.
2. **Considering:** prototyping a basic Comet + ShapeLand before the human-readable proposal is done.
3. **Later (from phase 3; ShapeLand needs no assets):** pick placeholder packs from Kenney, Quaternius and KayKit (CC0; survey in `.claude/research/assets.md`).
4. **Maybe later:** an illustrated page explaining the 3D asset workflow (UV unwrapping, trim sheets, vertex colours, skinning to one skeleton).
5. Everything will likely be reviewed again later.
