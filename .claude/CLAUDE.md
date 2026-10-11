# Comet and Anima: agent context

Handoff notes for agent sessions. Last updated 2026-10-11.

**Names:** *Comet* is the shared base (servers, libraries, Unity package, tools); *Anima* is the working name of the game built on it; *ShapeLand* is Comet's tiny demo and reference game. *ProjectComet* (also the repository's name) is the working name of the whole project, covering Comet and Anima.

## Working agreement

- **Discuss before acting.** Propose a plan and wait for agreement before writing docs, pushing, or changing branches. Don't fill in content or structure that hasn't been discussed.
- **Keep decided, considering and open apart.** Don't present agent suggestions as decisions.
- **Confirm adoptions with prompts.** When it seems we agree on something explicit (often an agent suggestion), ask the project lead to adopt it with a prompt (the question tool), one question per feature, before recording it as decided. Decisions the project lead states directly can be recorded as stated.
- **Human-readable docs are the goal of this phase.** They're written for people and live in `docs/`. Agent-oriented material (like this file) lives under `.claude/`.
- **Authorship.** Technical notes (setup, tooling, data models) may be written by agents. Overarching project descriptions and prose for readers of the proposal (pitch, pillar wording, what makes it different, the Comet overview, the setting) are the project lead's. Agents only draft them when asked, and never the setting. The final database schema is the project lead's; agent table designs are input.
- **Once human-readable docs exist, they are the source of truth,** not chat context or `.claude/` notes. Agent drafts survive only if the project lead carries them over into their own pages. When the docs and these notes disagree, the docs win; update the notes to match.

## What this phase is

Planning, for a proposal written for any small indie team. ProjectComet, including the proposal, is the project lead's personal project; there is no established team yet. The project lead plans to present it to people they know with game development skills. No hiring yet. The early prototype's phase 0 (ShapeLand) is done and playable at the public test build; the focus is back on the proposal. Don't hash out precise UI and control details. Multiple characters per account will exist, but this phase ignores multi-character flows.

The project lead may build more of the prototype (the full load test, todos below); don't start building anything without discussing it first.

## Project lead

Experienced web developer with shipped production apps; strong in deployments, orchestration and database design. Hobbyist game developer with little experience in real-time game networking. C# / .NET is the only language they're confident maintaining a critical backend in.

## Core decisions

- **Two layers:** Comet, a shared base for a family of similar MMOs, and Anima as one game on it. Comet keeps thin mechanisms; game policy is game code. Details in `proposal.md`.
- **Human-made creative content only.** Agents help with code; no AI-generated models, textures, readable in-game text, music or sound (plain interface labels and messages excepted).
- **Low-poly art,** PS1-to-GameCube fidelity.
- **Content is roughly an even split** between community-driven play (economy, crafting, trading, housing, guilds, player events) and developer-made content.
- **Action combat** with real hitboxes, height zones and an optional lock-on; forgiving of latency; PvE only.
- **Seamless world with no loading screens;** only teleports hide loading. The project's biggest technical challenge.
- **Private servers are easy to self-host,** without a large proprietary dependency.
- **Unity client with web export as a main goal;** plain .NET servers; WebSocket for every client. Later, high-quality web play with WebGPU and WebTransport if possible.
- **Phase 1, the vertical slice:** ShapeLand in one zone, with 100+ bot clients and simulated latency from day one. It opens with level editing (chunked zone files and the first editor tools), then interest management, then the full game load test; the texture filtering comparison runs alongside. Before it, **phase 0** (`prototype.md`) is done: the stack benchmark passed and ShapeLand slides, jumps and chats under simulated latency.

## Design notes

Detailed agent notes live in `.claude/design/`. Read the relevant file before discussing a topic, and record new decisions there (not here).

**Notes describe the current state only.** Superseded text, old names and "replaces earlier…" stories are removed; git keeps history. Each file keeps **Decided**, **Considering**, **Rejected** and **Open** apart; Rejected is one line plus the reason, so dropped ideas aren't re-proposed. Decided items carry no dates or "adopted from an agent suggestion" tags; "(project lead)" marks stay only where authorship still matters, and Considering items may say whose idea they are. Each file starts with a **Layer** line (base, game or mixed).

| File | Covers | Open items |
| --- | --- | --- |
| `glossary.md` | Working terms | — |
| `proposal.md` | Two layers, ShapeLand, vision, business model, audience, roadmap, core loop, economy, new players, character creation, age rating, licences, accessibility, privacy, security, comparables | — |
| `backend.md` | Services, handoff, transport, libraries, load testing, zones, content pipeline, database architecture, moderation and operations; research | Terrain pieces and edges, client mods, WebTransport, deferred ops items |
| `prototype.md` | The early prototype: scope, plan, stack benchmark, ShapeLand phase 0 as built, the public test build | Stage 2 load-test thresholds |
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
| `ui.md` | Drafting game UI: HTML mockups, who drafts, where they live | — |
| `database.md` | Draft tables per area, with a Layer column | The project lead's final schema |

## Docs and drafts

- **`docs/`** holds the human-readable proposal, in Markdown on `main`, one page per topic, **named by page title with spaces and capitals** (the docs don't follow the repo's lowercase convention). `docs/Proposal.md` is the front page; `docs/Roadmap.md`; `docs/Comet/` (Comet Overview, Architecture, Netcode, World and Zones, Combat Core, Content Pipeline, Operations, Self-Hosting, ShapeLand); `docs/Anima/` (Setting, Core Loop and Endgame, Classes and Skills, Combat, Items and Economy, Crafting and Housing, Companions, World and Travel, New Players, Community, Art and Audio, Accessibility and Translation, Business and Audience). Pages start as a title and an agent checklist (in an HTML comment marker) linking to `.claude/design/`, which the project lead deletes as they write.
- **`docs/+ Notes/`** holds the project lead's own notes. Agents don't edit it.
- **`.claude/drafts/`** holds agent drafts of every docs page, mirroring the layout (Setting has none), plus `Prototype Report.md` (the prototype report: what phase 0 built and measured, for the proposal) and `Prototype Results.md` (its raw material: the stack benchmark's numbers and 3c's tuning history). The project lead copies what they need into their own report in `docs/`. Agent drafts go here, never in `docs/`.
- **`.claude/drafts/ui/`** holds HTML mockups of game UI (`ui.md`), published as private claude.ai pages for review.
- **`.claude/research/`** holds agent research passes (`comparables.md`: comparable and inspiration games, with the project lead's reasons for each; `assets.md`: free low-poly asset sources and what to check, nothing chosen).
- **The repo is an Obsidian vault** (config in `.obsidian/` at the root, inline titles hidden), which the project lead uses to write the docs.

## Source material

The project lead's older Obsidian notes (Quick Notes, Class Ideas, Technical Notes, Story Snippets, Conversations) live in their own Obsidian vault, outside the repo; ask the project lead for anything needed from them. They are messy working notes, not decisions, and some of their ideas have since been rejected; check the design notes first.

## Risks

- **Big:** netcode (sharpened by action combat and the seamless world), moderation, economy integrity, more players than the game can support.
- **Not a current concern:** audio budget, low population.

## Repository

- **Agent work goes through feature branches and squash-merged PRs** into `main`. Branches can be as large as the work needs (one can cover many changes) to keep iteration fast. The repo allows only squash merges, uses the PR title and description as the commit message, and deletes branches on merge. The project lead's own docs and notes edits may go straight to `main`.
- At the start of a session, run `git fetch` and check for open PRs and remote branches ahead of `main` before trusting these notes.
- **Archive branches** hold earlier, abandoned attempts that can be mined for ideas (data models, schemas, architecture, networking experiments): `archive/dotnet-datacenter` (Godot + .NET "DataCenter" backend, including `docs/reference.sql`), `archive/nakama`, `archive/rust-bevy`, `archive/rust-bevy-sqlite-opfs`, `archive/rust-bevy-spacetimedb`, `archive/godot-client`.
- No tags are wanted.

## Next steps

1. **The project lead writes the docs pages,** using the checklists and the drafts in `.claude/drafts/`.
2. **Phase 0 is done** (2026-10-10; `prototype.md`): the stack benchmark (one known issue: a one-off GC pause of 10–20 ms on AWS as players join), the content pipeline and shared packages in Unity, ShapeLand's server, bots and Unity client, 3c's network tuning (every criterion passes), the code review (two rounds, fixed), the custom web page, graceful disconnects (PR #4) and the public test build. The full game load test opens phase 1. The custom web page and the public test build came in PR #5.
3. **Working facts:**
   - Run Unity batch jobs through `tools/unity-batch.sh` (the editor hangs while quitting). Unity on Linux: run `tools/unity-restore.sh` after a fresh clone, and see `prototype.md` for batch-mode quirks.
   - `tools/shapeland-web.sh` builds the web client and runs it from the game server (with optional bots and `--net` profiles) for the project lead to test in a browser without opening the editor; `tools/shapeland-netrun.sh` runs the headless network runs.
   - `TickLoopTests.RunsAtTheTickRate` can miss when every test suite runs at once (passes alone).
   - **The public test build:** ShapeLand plays at https://shapeland.lucyawrey.com: the page on Vercel, the game server on an on-demand AWS `t4g.micro` behind Caddy, all run by `tools/shapeland-testbuild.sh` (setup, up, down, status, log, page); how-to in `prototype.md` (Public test build). The server may stay up for long stretches, since it's cheap and people want to try it. This network's router still answers the old Google Domains wildcard for `server.shapeland` (public resolvers are right); restart the router or use DNS over HTTPS in the browser.
5a. **Todo** (project lead): a simple way for a player to choose their name, shape and colours again without reconnecting.
6. **Todo (later): playtest polish for phase 0:** movement and "animation" feel (the project lead found it needs work, perhaps partly the simple shapes), the camera clipping into block sides, the final body and eye colour sets (the project lead's), and the join screen's name box changing height between empty and filled in the Linux desktop build (not seen on the web). Collect more here as playtests find them.
7. **Todo (later):** make movement feel easy for the project lead to fine-tune: they're happy to edit code, so keep the numbers in one obvious place each (shape speeds, jumps and looks in content, each shape's `.toml`; gravity, acceleration and step height in `Comet.Simulation`'s `MovementRules`) and write down where they are and what each does. Adjusting them live while playing is optional.
7a. **Todo (later): a cleanup pass over the code** (project lead): make it as small, maintainable and well documented as possible, so human teams can actually work on it.
8. **Out of scope for now:** WebTransport on web, UDP on desktop, and supporting several transports behind one interface. WebTransport stays a goal down the line; background in `backend.md` (transport, WebTransport upgrade paths, Research).
9. **Todo:** try WebGPU in ShapeLand's web build as a test. Unity 6.6 supports it with an automatic WebGL 2 fallback; research in `backend.md` (Research, Web and Unity). Measure it against WebGL 2 on a few browsers; Unity 7's status is still unknown.
10. **Later (from phase 3; ShapeLand needs no assets):** pick placeholder packs from Kenney, Quaternius and KayKit (CC0; survey in `.claude/research/assets.md`).
11. **Maybe later:** an illustrated page explaining the 3D asset workflow (UV unwrapping, trim sheets, vertex colours, skinning to one skeleton).
12. Everything will likely be reviewed again later.
