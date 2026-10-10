# Comet and Project Anima: agent context

Handoff notes for agent sessions. Last updated 2026-10-09.

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
- **Human-made creative content only.** Agents help with code; no AI-generated models, textures, readable in-game text, music or sound (plain interface labels and messages excepted).
- **Low-poly art,** PS1-to-GameCube fidelity.
- **Content is roughly an even split** between community-driven play (economy, crafting, trading, housing, guilds, player events) and developer-made content.
- **Action combat** with real hitboxes, height zones and an optional lock-on; forgiving of latency; PvE only.
- **Seamless world with no loading screens;** only teleports hide loading. The project's biggest technical challenge.
- **Private servers are easy to self-host,** without a large proprietary dependency.
- **Unity client with web export as a main goal;** plain .NET servers; WebSocket for every client. Later, high-quality web play with WebGPU and WebTransport if possible.
- **Phase 1, the vertical slice:** ShapeLand in one zone, with 100+ bot clients and simulated latency from day one. Before it, **the prototype's first milestone** is the stack benchmark passing (`prototype.md`).

## Design notes

Detailed agent notes live in `.claude/design/`. Read the relevant file before discussing a topic, and record new decisions there (not here).

**Notes describe the current state only.** Superseded text, old names and "replaces earlier…" stories are removed; git keeps history. Each file keeps **Decided**, **Considering**, **Rejected** and **Open** apart; Rejected is one line plus the reason, so dropped ideas aren't re-proposed. Decided items carry no dates or "adopted from an agent suggestion" tags; "(project lead)" marks stay only where authorship still matters, and Considering items may say whose idea they are. Each file starts with a **Layer** line (base, game or mixed).

| File | Covers | Open items |
| --- | --- | --- |
| `glossary.md` | Working terms | — |
| `proposal.md` | Two layers, ShapeLand, vision, business model, audience, roadmap, core loop, economy, new players, character creation, age rating, licences, accessibility, privacy, security, comparables | — |
| `backend.md` | Services, handoff, transport, libraries, load testing, zones, content pipeline, database architecture, moderation and operations; research | Client mods, WebTransport, deferred ops items |
| `prototype.md` | The early prototype: scope, who builds it, plan | Stage 2 load-test thresholds |
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
| `lore-hooks.md` | What gameplay implies about the setting: raw material for the project lead's lore, not lore | — |

## Docs and drafts

- **`docs/`** holds the human-readable proposal, in Markdown on `main`, one page per topic, **named by page title with spaces and capitals** (the docs don't follow the repo's lowercase convention). `docs/Proposal.md` is the front page; `docs/Roadmap.md`; `docs/Comet/` (Comet Overview, Architecture, Netcode, World and Zones, Combat Core, Content Pipeline, Operations, Self-Hosting, ShapeLand); `docs/Project Anima/` (Setting, Core Loop and Endgame, Classes and Skills, Combat, Items and Economy, Crafting and Housing, Companions, World and Travel, New Players, Community, Art and Audio, Accessibility and Translation, Business and Audience). Pages start as a title and an agent checklist (in an HTML comment marker) linking to `.claude/design/`, which the project lead deletes as they write.
- **`docs/+ Notes/`** holds the project lead's own notes. Agents don't edit it.
- **`.claude/drafts/`** holds agent drafts of every docs page, mirroring the layout (Setting has none), plus `Prototype Results.md` (the prototype report, filled in during 3c; the project lead copies what they need into their own report in `docs/`). Agent drafts go here, never in `docs/`.
- **`.claude/drafts/ui/`** holds HTML mockups of game UI (`ui.md`), published as private claude.ai pages for review.
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
2. **Step 2 is done** (2a, the content pipeline; 2b, the shared packages in Unity; `prototype.md`). Next: finish ShapeLand phase 0 (3c; `prototype.md`), then the docs; the full load test waits. **3b:** `Comet.Client` (bots run on it), the Comet Unity package (`comet/unity`: browser transport, `CometConnection`), the bare join scene with a Play mode test that joins the real server, a web build of it that joined from headless Firefox, and the Game scene with the player's own movement (fixed 60 Hz step) and orbit camera (turning at a rate, eyes, chosen colours, hovering), tested from Play mode against the real server and tried by hand in the editor and the browser, and other players drawn with interpolation (bots tested from Play mode), with an adaptive interpolation delay for them that removes the measured jitter, which looks smooth in the web build, and corrections: a blended snap-back (a dev-only speed cheat, hold C) and the respawn fade, which starts while falling and also fades other players' shapes, tested from Play mode (`prototype.md`). The join screen (UI Toolkit, the plain look from the mockups, `ui.md`), shape looks in content, and chat with name tags and bubbles are in too. Development builds of the Game scene exist for macOS and Linux (ShapeLand > Build > macOS (Game Scene), Linux (Game Scene)), and the server now knows gravity: airborne reports must follow the gravity arc, and players silent in the air are dropped by the server (`netcode.md`, with the principle that movement is client-reported but must feel server-authoritative and always make sense, and a list of known movement gaps under Open). **3b is done:** the project lead checked its definition of done by hand (desktop and web clients with 5 bots and a cheater). **Next: 3c.** `TickLoopTests.RunsAtTheTickRate` can miss when every test suite runs at once (passes alone). Run Unity batch jobs through `tools/unity-batch.sh` (the editor hangs while quitting). `tools/shapeland-web.sh` builds the web client and runs it from the game server (with optional bots) for the project lead to test in a browser without opening the editor. Unity on Linux: run `tools/unity-restore.sh` after a fresh clone, and see `prototype.md` for batch-mode quirks. The stack benchmark (prototype milestone 1) passed, with one known issue: a one-off GC pause of 10–20 ms on AWS as players join (`prototype.md`, stack benchmark results).
3. **Todo:** a custom web page for ShapeLand's web build (a Unity WebGL template in place of Unity's default page). At the start it's a lightly themed single page, not a full website; the Login server serves the game's page later (`backend.md`).
3a. **Todo (once phase 0 is done): a large agent code review of everything so far** (project lead; none has been done yet), before the prototype report and the test build.
4. **Todo (after phase 0): a public test build** (project lead), so ShapeLand can be shown with the proposal without downloads: a game server on AWS, as cheap as possible and easy to spin up and down, and the web build hosted on GitHub Pages for this repo. When the server is down, the page says the game isn't open for testing right now. Builds on the custom web page (3). **The order from here** (project lead): 3c, then the prototype report draft (`.claude/drafts/Prototype Results.md`), then this test build last; after that it's all docs writing by the project lead, with agents helping draft.
5. **Todo:** when the server goes down, clients disconnect gracefully: the game notices, says the connection was lost and goes back to the join screen, rather than leaving the player in a frozen world.
6. **Todo (later): playtest polish for phase 0,** kept apart so it doesn't hold up 3b and 3c: movement and "animation" feel (the project lead found it needs work, perhaps partly the simple shapes), the camera clipping into block sides, the final body and eye colour sets (the project lead's), and the join screen's name box changing height between empty and filled in the Linux desktop build (not seen on the web). Collect more here as playtests find them.
7. **Todo (later):** make movement feel easy for the project lead to fine-tune: they're happy to edit code, so keep the numbers in one obvious place each (shape speeds, jumps and looks in content, each shape's `.toml`; gravity, acceleration and step height in `Comet.Simulation`'s `MovementRules`) and write down where they are and what each does. Adjusting them live while playing is optional.
8. **Todo:** ingest the project lead's story and setting notes (a lot of them, covering two separate settings that will need adjusting to fit the mechanics) and give them summaries, so they can write the final story overview more easily. Summaries describe their notes; they aren't lore, and the setting itself stays the project lead's. Compare against `lore-hooks.md` to show where each setting fits or clashes with the mechanics. **Never check the summaries (or the notes) into this repo;** they go in the project lead's notes, outside the repo. Waiting on the project lead to share the notes.
9. **Out of scope for now:** WebTransport on web, UDP on desktop, and supporting several transports behind one interface. WebTransport stays a goal down the line; background in `backend.md` (transport, WebTransport upgrade paths, Research).
10. **Todo (after phase 0):** try WebGPU in ShapeLand's web build as a test. Unity 6.6 supports it with an automatic WebGL 2 fallback; research in `backend.md` (Research, Web and Unity). Measure it against WebGL 2 on a few browsers; Unity 7's status is still unknown.
11. **Later (from phase 3; ShapeLand needs no assets):** pick placeholder packs from Kenney, Quaternius and KayKit (CC0; survey in `.claude/research/assets.md`).
12. **Maybe later:** an illustrated page explaining the 3D asset workflow (UV unwrapping, trim sheets, vertex colours, skinning to one skeleton).
13. Everything will likely be reviewed again later.
