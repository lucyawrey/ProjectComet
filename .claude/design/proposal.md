# Proposal-level topics

Agent notes on the things a game proposal usually covers beyond systems: vision and pillars, business model, audience and platforms, scope and roadmap, comparable games, setting, core loop, economy, onboarding, age rating, private-server licence, accessibility and translation. **Pitch, pillar wording, setting and other proposal prose are written by the project lead**; this file records decisions and leaves placeholders.

Discussion order (agreed 2026-10-04, working down until the project lead decides it's enough): 1 vision and pillars; 2 business model; 3 audience and platforms; 4 scope, team, roadmap and budget; 5 comparable games; 6 setting and story; 7 core loop, endgame and content delivery; 8 economy design; 9 new-player experience; 10 age rating and minors; 11 private-server licence; 12 accessibility and translation.

## Decided

### Vision and pillars

- **Pillars: about five, grouped from eight themes** (project lead, 2026-10-04; themes picked from ones the agent observed in earlier decisions, grouping sketched by the agent; **final wording is the project lead's**):
  - *One world, anywhere:* instant browser play and one seamless world.
  - *A real, player-shaped world:* every item is real, and the economy, crafting, trading, housing, guilds and player events carry half the game.
  - *Be anything:* every class and craft on one character through crystals, with soft pressure to specialise.
  - *Approachable action:* forgiving action combat, a little like ARPGs and a little like tab-target games, but extremely approachable for casual players.
  - *Low-fi charm:* cute but cool, PS1-to-GameCube fidelity, a storybook world.
- **Self-hosting is a commitment, not a pillar** (project lead, 2026-10-04): it shapes the tech and business model more than how the game feels.

*Placeholder: elevator pitch (project lead).*

### Business model

- **Free-to-play with membership** (project lead, 2026-10-04).
- **The game is split into parts; free players are always locked to an earlier part than members** (project lead, 2026-10-04). The initial release has two parts (early and late game); future parts are expansions. Free players get each part eventually, as long as new parts keep coming. This means lower level caps for free players, classes added in later parts locked, and so on. **No overall features are barred from free players.** To discuss further (see Open).
- **Free accounts have anti-abuse limits until earned** (2026-10-04, adopted from an agent suggestion; an exception to "no features barred"): new free accounts have limited trading, market and public chat until they pass a playtime or level threshold, or have ever held membership. Blocks throwaway bot accounts without walling off honest free players.
- **Lapsed membership keeps everything, capped** (2026-10-04, adopted from an agent suggestion): nothing is lost; level is capped to the free part's cap and later-part classes are locked; anyone in a later-part zone is moved to the nearest free location on login. Everything returns on renewal.
- **Part borders in the seamless world** (project lead, 2026-10-04): an in-world barrier with a clear membership message, plus story gates. Most areas can still be reached without the story, through less convenient routes (e.g. a very long walk instead of a story carriage ride).
- **Free and paid players group freely in free-part content only** (2026-10-04, adopted from an agent suggestion); free players can't enter later-part content, even with a member.
- **Items aren't restricted by part** (project lead, 2026-10-04): free players can buy, trade and hold any item. Use is limited only by level and class requirements, which free players may not meet because of their part's level cap and locked classes.
- **Expansions are included in membership** (project lead, 2026-10-04); nothing is sold separately.
- **A tradeable membership item (like Old School RuneScape's bonds) is the only thing bought with real money** (project lead, 2026-10-04): bought with money, sold on the market for in-game currency. Sometimes disliked, but better than a cash shop or loot boxes, and one of the better ways to fund the game.
- **No item shop** (project lead, 2026-10-04): nothing else sold for money becomes an item, protecting "every item is real" and the economy.
- **No loot boxes or other paid randomness** (project lead, 2026-10-04).
- Already decided elsewhere: Anima is never sold for real money (`classes.md`).

### Audience and platforms

- **Audience** (project lead, 2026-10-04): cosy and social MMO players; MMO nostalgics in general, from browser RuneScape to classic WoW; and high-end raiders. The combat system is simple to understand and hard to master, with complex boss mechanics like FFXIV's or Rabbit and Steel's (see `combat.md`).
- **Platforms** (project lead, 2026-10-04): desktop browsers (the main goal) and a desktop app. Mobile browsers and consoles are not targeted.
- **Sessions: short and long** (2026-10-04, adopted from an agent suggestion): meaningful progress in 15–30 minutes (crafting, gathering, a quick dungeon), with longer sessions for raids and events.

### Scope, team, roadmap and budget

- **Team: the proposal lists roles needed, not people** (project lead, 2026-10-04). Roles (adopted from an agent draft, with the project lead's correction; in a small team one person covers several):

  | Area | Roles |
  | --- | --- |
  | Programming | Unity client and gameplay; server and networking; tools (zone editor, content build, admin panel) |
  | Art | Low-poly character artist (modelling and texturing); environment artist; animator for the shared skeleton |
  | Design | Systems and economy designer; combat and boss-encounter designer; UI/UX designer |
  | Writing | Narrative and quest writer (human-written content only) |
  | Audio | Composer and sound designer (later) |
  | Community | Community manager and moderation lead (coordinates volunteer moderators) |
  | QA | Testing and playtesting |
  | Operations | Deployments, databases, monitoring (the project lead's strength) |

- **Roadmap: phases with goals and exit criteria, no dates** (2026-10-04, adopted from an agent draft):

  | Phase | Goal | Done when |
  | --- | --- | --- |
  | 0. Prototypes | Two-stage load test; shared source packages in Unity; TOML 1.1 editor support; texture filtering comparison | The load test meets thresholds written down beforehand |
  | 1. Vertical slice | One zone, core loop, 100+ bots, simulated latency (the decided first milestone) | The core loop is fun under latency and performance meets thresholds |
  | 2. Seamless-world proof | Two or more zones with border handoff, channels, a dungeon instance, a teleport | Crossing borders is invisible under simulated latency |
  | 3. Closed alpha | The early part playable: accounts, persistence, classes, items, crafting, basic moderation tools, private-server packaging | Stable with real players, and they come back |
  | 4. Open beta | Early part complete, late part in progress, membership and bonds, ops tooling | Scale and economy hold up with a real population |
  | 5. Launch | Early part (free) and late part (members) | |
  | 6. Expansions | New parts; free players move up a part | |

  Phase 2 proves the biggest risk (the seamless world) before content production.
- **Budget: hosting costs only** (project lead, 2026-10-04); development budget is left to the team. Cost categories (adopted from an agent suggestion): game servers (most of it), Region server processes, PostgreSQL with backups, Valkey for larger regions, and CDN bandwidth for web client, asset and content downloads (every new browser player downloads them). *Placeholder: numbers after phase 0 (players per CPU core from the load test).*
- **Two layers: an "engine" base and the game on top** (project lead, 2026-10-04). The base runs a family of similar MMOs that share most systems; Project Comet is one game built on it. If the indie team doesn't want this exact game vision, the base is still useful for a similar MMO that fits their goals better. **The base carries a lot of assumptions about the game** (project lead): how combat and transport work in general, and more, so everything built on it will feel similar in many ways. Don't describe it as a base for "any" game. Specific gameplay can vary: another game might not have Class Crystals, for example.
  - **The proposal is one document in two parts** (2026-10-04, adopted from an agent suggestion): the base, then the game. The team can take the first part on its own.
  - **Games plug in as C# code modules plus their own content** (2026-10-04, adopted from an agent suggestion), on top of the base's libraries and servers. Systems like crystals can't be expressed by content data alone.
  - **In the base** (2026-10-04, picked by the project lead from an agent-proposed list), beyond the obviously shared parts (seamless world, zones, channels and instances, transport, netcode, action-combat core, content pipeline, accounts, ops and moderation tools, self-hosting): items and economy (inventory, storage, trading, market, currency, item ledger); a crafting and gathering framework (which crafts exist is game content); social and housing (guilds, parties, chat, friends, house instances); a progression framework (XP, levels, unlock flags, skill slots). Class Crystals, Soul XP, Anima and Runes sit on top as Comet's game layer.
  - **Roadmap: phases 0–2 are marked as base milestones** (2026-10-04, adopted from an agent suggestion). Prototypes, the vertical slice and the seamless-world proof mostly build the base.
  - **Early demos don't need to be Project Comet** (project lead, 2026-10-04): they can be very simple expressions of the base. **Phases 0–2 use a minimal demo game built on the base** (2026-10-04, adopted from an agent suggestion), e.g. fight, loot, craft and trade in one zone; Comet's own systems (crystals, Anima…) start at phase 3, the closed alpha. The early work stays useful whatever game the team picks.
  - **The demo game is ShapeLand** (project lead, 2026-10-04): players are basic coloured shapes (cubes, pills, pyramids) in similarly basic shape environments, like prototype level geometry, who slide around, chat and engage in basic magical combat. Needs no art assets or animation. Details (2026-10-04):
    - **Shapes are classes** (project lead's pick): each shape has its own spells and hitbox, a small test of the progression framework and combat variety.
    - **It grows with the phases** (adopted from an agent suggestion): phase 0, sliding and chat; phase 1, combat plus a little loot and persistence, so the slice tests database writes; phase 2, several zones, a border, a channel, a dungeon instance and a teleport.
    - **It stays as the base's reference game** (adopted from an agent suggestion): shown in the base part of the proposal, kept working as the base evolves, and shipped as the sample game for anyone building on the base or testing a private server.
  - **Naming** (project lead, 2026-10-04): "Project Comet" is a placeholder for the game. If the game gets an official name, "Comet" might become the name of the base.

### Comparable games

- **Comparables in the proposal** (project lead, 2026-10-04): Old School RuneScape, FFXIV, Classic WoW, Rabbit and Steel. Art inspirations (PSO, Crystal Chronicles, Signalis) are in `art.md`.
- **Candidates to research before deciding** (project lead, 2026-10-04; the project lead doesn't know them personally): Albion Online (player-driven economy; looks like an interesting comparable), Brighter Shores, Mabinogi, TERA and Blue Protocol. A comparable-research pass is a todo.
- *Placeholder: what makes this game different (project lead).*

### Setting and story

- **The project lead writes the lore themselves** (2026-10-04). The agent distilled what gameplay already implies into `lore-hooks.md` as raw material. *Placeholder: setting (project lead).*

## Considering

## Open

- **Two-layer follow-ups** (raised by the agent, 2026-10-04, not yet discussed):
  - A name for the base. "Engine" clashes with Unity being the engine; "Comet" is a candidate once the game has its own name (see Decided).
  - Sorting the existing design notes into base and game. Most of `backend.md` and `netcode.md` is base already. Comet-specific details that would move to the game layer include Anima escrow in instances (`backend.md`) and the crystal, Anima and Rune tables (`database.md`).
  - How game modules extend the base: database extension is decided (`backend.md`); still open are their own Simulation rules and their own message types in Protocol.
  - Which business-model rules (parts, membership, bonds) belong to the base and which to the game.
