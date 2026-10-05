# Proposal-level topics

**Layer: mixed.** Base (Comet): the two-layer section, team roles, roadmap phases 0–2, ShapeLand. Game (Project Anima): vision and pillars, business model, audience, comparables, setting.

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
  | 1. Vertical slice | One zone, core loop, 100+ bots, simulated latency (the decided first milestone) | Movement and combat feel responsive under simulated latency, and performance meets thresholds (2026-10-04, adopted from an agent suggestion; replaces "the core loop is fun", since the slice is ShapeLand; whether Project Anima is fun is tested at phase 3) |
  | 2. Seamless-world proof | Two or more zones with border handoff, channels, a dungeon instance, a teleport | Crossing borders is invisible under simulated latency |
  | 3. Closed alpha | The early part playable: accounts, persistence, classes, items, crafting, basic moderation tools, private-server packaging | Stable with real players, and they come back |
  | 4. Open beta | Early part complete, late part in progress, membership and bonds, ops tooling | Scale and economy hold up with a real population |
  | 5. Launch | Early part (free) and late part (members) | |
  | 6. Expansions | New parts; free players move up a part | |

  Phase 2 proves the biggest risk (the seamless world) before content production.
- **Budget: hosting costs only** (project lead, 2026-10-04); development budget is left to the team. Cost categories (adopted from an agent suggestion): game servers (most of it), Region server processes, PostgreSQL with backups, Valkey for larger regions, and CDN bandwidth for web client, asset and content downloads (every new browser player downloads them). *Placeholder: numbers after phase 0 (players per CPU core from the load test).*
- **Two layers: an "engine" base and the game on top** (project lead, 2026-10-04). The base runs a family of similar MMOs that share most systems; Project Anima is one game built on it. If the indie team doesn't want this exact game vision, the base is still useful for a similar MMO that fits their goals better. **The base carries a lot of assumptions about the game** (project lead): how combat and transport work in general, and more, so everything built on it will feel similar in many ways. Don't describe it as a base for "any" game. Specific gameplay can vary: another game might not have Class Crystals, for example.
  - **The proposal is one document in two parts** (2026-10-04, adopted from an agent suggestion): the base, then the game. The team can take the first part on its own.
  - **Games plug in as C# code modules plus their own content** (2026-10-04, adopted from an agent suggestion), on top of the base's libraries and servers. Systems like crystals can't be expressed by content data alone.
  - **In the base** (2026-10-04, picked by the project lead from an agent-proposed list), beyond the obviously shared parts (seamless world, zones, channels and instances, transport, netcode, action-combat core, content pipeline, accounts, ops and moderation tools, self-hosting): items and economy (inventory, storage, trading, currency, item ledger; the market later moved to the game); a basic crafting framework (inputs and outputs, recipe storage, crafter signatures; quality scores and other details are game-specific, project lead 2026-10-04); social and housing (guilds, parties, chat, friends, house instances; housing features later moved to the game); a progression framework (XP, levels, unlock flags, and a basic skill implementation that isn't necessarily slots, so a game can build more traditional MMO skill advancement; project lead, 2026-10-04). Class Crystals, Soul XP, Anima and Runes sit on top as Project Anima's game layer.
  - **Roadmap: phases 0–2 are marked as base milestones** (2026-10-04, adopted from an agent suggestion). Prototypes, the vertical slice and the seamless-world proof mostly build the base.
  - **Early demos don't need to be Project Anima** (project lead, 2026-10-04): they can be very simple expressions of the base. **Phases 0–2 use a minimal demo game built on the base** (2026-10-04, adopted from an agent suggestion), e.g. fight, loot, craft and trade in one zone; Project Anima's own systems (crystals, Anima…) start at phase 3, the closed alpha. The early work stays useful whatever game the team picks.
  - **The demo game is ShapeLand** (project lead, 2026-10-04): players are basic coloured shapes (cubes, pills, pyramids) in similarly basic shape environments, like prototype level geometry, who slide around, chat and engage in basic magical combat. Needs no art assets or animation. Details (2026-10-04):
    - **Shapes are classes** (project lead's pick): each shape has its own spells and hitbox, a small test of the progression framework and combat variety.
    - **It grows with the phases** (adopted from an agent suggestion): phase 0, sliding and chat; phase 1, combat plus a little loot and persistence, so the slice tests database writes; phase 2, several zones, a border, a channel, a dungeon instance and a teleport.
    - **It stays as the base's reference game** (adopted from an agent suggestion): shown in the base part of the proposal, kept working as the base evolves, and shipped as the sample game for anyone building on the base or testing a private server.
  - **Naming** (project lead, 2026-10-04): **the base is called Comet**; **the game's working name is Project Anima** (picked from agent-brainstormed names), replacing "Project Comet" as the game's placeholder. Earlier notes that say "Project Comet" or "Comet" for the game mean Project Anima. (The repo keeps the name `ProjectComet`. Comet won't be a region name, although the project lead's Quick Notes used it for one; project lead, 2026-10-04.)
  - **Games extend Protocol and Simulation through registry keys and extension points** (2026-10-04, adopted from an agent suggestion): games ship their own assemblies next to Protocol and Simulation; game message types get numbers from the registry, as for database rows (`backend.md`); Simulation exposes extension points (effect, buff and skill-behaviour handlers registered by key). Everything is compiled in, so it works on web and IL2CPP builds.
  - **Business model: game code** (2026-10-04, adopted from an agent suggestion; replaces "base mechanisms, game policy" from earlier the same day): membership, content parts and their gating, free-account limits and bonds are Project Anima code, since ShapeLand won't use them. Comet keeps at most a membership flag on accounts.
  - **Design notes are tagged in place** (2026-10-04, adopted from an agent suggestion): each topic file gets a layer line (base, game or mixed) and mixed sections are tagged. The real split happens when writing the human-readable docs.
  - **Keeping the split cheap** (2026-10-04, all adopted from agent suggestions after an agent estimate: clean seams in one codebase cost roughly 10–20% more up front, a true product engine 1.5–2×; the main risk is guessing abstractions with only one real game):
    - **Extract, don't pre-build:** a system enters Comet only once both ShapeLand and Project Anima use it; Anima-only systems stay in Anima until another game needs them. The layer tags are expectations, not build orders.
    - **One repo, one solution, no API stability:** Comet isn't a product until a second real game exists. Breaking changes are fine and both games are fixed in the same commit; no versioned packages.
    - **Extension points only when needed:** plain C# interfaces and registration by key; no plugin loader or modding framework, and no hook without a current caller.
    - **ShapeLand stays tiny:** a test fixture that happens to be playable (still the reference game); every system it adopts is maintenance.
    - **Thin skill base:** Comet defines only what executes an action (frame data, hitboxes, cooldowns, effects); how skills are acquired, slotted and advanced is entirely game code.
    - **The base part of the proposal is short:** framed as how the game is built, plus a section on reuse, not a second product pitch.
  - **Gray areas sorted** (project lead, 2026-10-04): flying and vehicles are base; attunements (with Anima Capacity) are game. Companions, gear and outfit sets and the item collection log were first put in the base, then moved to the game (below). Each design file starts with its layer line.
  - **Policy moves to the game; the base keeps thin mechanisms** (2026-10-04, all adopted from agent suggestions; duplication in ShapeLand is accepted, e.g. its own simple mounts or basic equip):
    - **Companions:** the base keeps movement and attachment (mounts as a movement mode, passengers, an entity following its owner). Companion records, capture, tasks, levelling and stables are game code.
    - **Soulbound:** the base keeps an owner or binding field on items and ledger entries for moves. Recall, the 24-hour return, lending, no-return trades and re-obtaining are game code.
    - **Gear and outfits:** the base keeps containers, slot rules and "something is equipped". Gear sets as references into Storage, the outfit overlay and Outfit Magic are game code.
    - **Dungeons and teleports:** the base keeps instance lifecycle, entry and exit hooks and teleport handoff. Loading rooms needing the whole party, re-entry votes, replacements, no lockouts, the teleport scene and cast rules are game code.
    - **Channels:** the base keeps placement with a pluggable preference policy and soft and hard caps. Constellations and the party-then-guild preference order are game code.
    - **Market:** player trading and the ledger stay in the base; the market itself (Grand Exchange, local markets, player shops…) is game code until another game wants the same kind.
    - **Housing:** the base keeps the house instance kind; placement, furniture, guild halls and temporary structures are game code.
    - **Smaller item systems:** wear and repair, dyes, the item collection log and the coin purse are game code.
  - **How the code is laid out** (2026-10-04):
    - **Client:** Comet is a Unity package (networking, entity replication, prediction, zone streaming, input plumbing); **each game is its own Unity project**, with its own UI, rendering and art, **all in the one monorepo** (project lead, adjusting an agent suggestion).
    - **Servers:** each game builds its own server programs (e.g. `Anima.GameServer`, `ShapeLand.GameServer`) that reference Comet's libraries; there are no generic Comet servers loading game modules (adopted from an agent suggestion; fits "no plugin loader").
    - **Content and bots:** each game has its own content folder and ID registry, and the base has its own keys; the content build merges the base's content with one game's. The bot framework is in the base, with bot behaviours per game (adopted from an agent suggestion).
  - **Deliberately kept in the base as structural** (2026-10-04, adopted from an agent suggestion): currency as items (the ledger depends on it), learned flags, tradeability tiers, flying and fixed-route vehicles, height zones and frame data.

### Core loop, endgame and content delivery

- **Two intertwined loops** (2026-10-04, adopted from an agent suggestion): an adventure loop (quests, dungeons, bosses → drops and XP) and a life loop (gather, craft, trade, house), each feeding the other through materials and gear. Matches the even content split.
- **Endgame** (project lead, 2026-10-04): raids and hard bosses; more classes (every class on one character, promotions, Soul XP unlocks); crafting and economy mastery; collection and housing; and **social systems**: multiple guilds, robust chat, lots of emotes, encouraging roleplay, and housing for building community spaces.
- **Gear: a middle ground** (2026-10-04, adopted from an agent suggestion): new tiers each expansion, but older gear keeps uses (outfits, Runes, niche stats) and isn't instantly worthless. Protects "every item is real".
- **RP profiles: simple** (2026-10-04, adopted from an agent suggestion): a short player-written profile (bio, RP status such as "in character" or "looking for RP", a few fields) others see by inspecting a character; reported and moderated like chat.
- **Minigames: both kinds, but few in the initial scope** (project lead, 2026-10-04): activity minigames (OSRS-style instanced activities such as team games, fishing contests and races) and casual side games (Gold Saucer-style card and arcade games).
- **Older content stays alive through level sync and fill bonuses** (2026-10-04, adopted from an agent suggestion based on the project lead's notes): players sync down to a dungeon's level, with XP bonuses for roulette or content fill and for helping new players.
- **Player events: an event board and venues** (2026-10-04, adopted from an agent suggestion): an in-game event calendar or board (including guild events), with housing and guild halls as venues; hosts get no special powers.
- **A content guide that helps players find everything** (project lead, 2026-10-04): not knowing where content is unlocked is frustrating. Details (2026-10-04):
  - **Covers all unlockable content** (adopted from an agent suggestion): dungeons, raids, classes, crafts, minigames, attunements, companion capture, side story chains; anything gated behind a quest or condition, with where and how to unlock it.
  - **Everything is listed, with details on demand** (project lead): every entry exists in the guide from the start; requirements and the starting NPC's location show on demand, with story spoilers hidden until reached. **The default view is closer to reveal-as-you-go**, so it isn't overwhelming.
  - **Map markers and tracking** (adopted from an agent suggestion): choosing an entry puts its next step on the map and in the quest tracker, and shows what's blocking it (level, a prior quest, an attunement).
- **No permanently missable rewards** (2026-10-04, adopted from an agent suggestion): seasonal events recur yearly and older rewards come back, in later runs or from a vendor. Friendly to casual players.
- **Daily and weekly bonuses are optional and catch-up friendly** (2026-10-04, adopted from an agent suggestion): missed ones accumulate or don't matter much; no log-in-or-fall-behind pressure. Anima regeneration already paces play.
- **Content delivery: expansions plus regular patches** (2026-10-04, adopted from an agent suggestion): expansions add parts; smaller patches every few months add story, dungeons, a raid tier and seasonal events. Fits maintenance-window deploys.

### Economy design

- **Market: a region-wide exchange with taxes** (project lead, 2026-10-04): one order-book market per region, reached at market boards in settlements. **Player stalls are a later feature.**
- **Gear comes from a mix of crafting and drops** (project lead, 2026-10-04), with **co-crafting**: a player who has the right materials but not the crafting stats can have the item crafted with another player or an NPC.
  - **With an NPC: fixed fee, capped quality** (2026-10-04, adopted from an agent suggestion): the NPC crafts for a coin fee (a sink) at a fixed, middling quality and can't make top-tier items; players remain the way to high quality and endgame pieces.
  - **With a player: both credited** (project lead's pick, 2026-10-04): the signature shows both names and craft XP is split.
- **Money faucets: between modest and classic** (project lead, 2026-10-04): quests, NPC selling and some monster coin, more generous than a mostly player-driven economy but less than a classic coin-drop MMO.
- **Exchange buy limits on some items** (2026-10-04, adopted from an agent suggestion): per-item buy limits over a time window for scarce or volatile items, set in content data; most items have none.
- **Economy monitoring: dashboards only** (project lead's pick, 2026-10-04): faucets, sinks and money supply per region, charted from the ledger for designers; no automated alerts.
- **Sinks** (project lead, 2026-10-04, from an agent-proposed list): market tax; repairs and NPC services (re-obtaining fees, housing upkeep, guild hall costs); crafting consumption, salvage and breakdown; cosmetic and prestige purchases from NPCs for coin (cosmetics, furniture, housing plots, titles).

### New-player experience

- **Guest play, upgrade later** (2026-10-05, adopted from an agent suggestion): new players play the prologue as a guest straight from a link; making an account keeps the character. Guests get the free-account limits plus no trading and no public chat.
- **A short prologue teaches the basics and introduces Class Crystals with Freelancer** (project lead, 2026-10-05): movement, dodging and basic combat, then crystals through Freelancer, the only tier 1 class. **At the end of the prologue the player promotes Freelancer to a tier 2 class.** The prologue opens in the unclassed state (the hidden crystal, `classes.md`) and grants Freelancer partway through (project lead).
- **The prologue happens where the character will stay** (project lead, 2026-10-05): choosing a class doesn't move them, but they may be encouraged to travel to a class-specific quest's location. (The notes' "starting location suited to class" no longer applies as such, since the class is chosen at the end of the prologue.)
- **Systems are mostly unlocked by side quests, not the main story** (project lead, 2026-10-05): each part has a main story, but it rarely gates systems. Most systems sit behind a quest the main story brings you near, and players can travel to those quests to unlock a system early. The content guide shows where they are.
- **New players are shown with a visible marker, and helping them gives a bonus** (project lead's pick, 2026-10-05); no dedicated new-player channel or mentor system.

### Age rating and minors

- **Aim for a teen rating (PEGI 12 / ESRB T)** (2026-10-05, adopted from an agent suggestion): fantasy violence, mild language, player interaction; room for moody zones and darker story moments. Bonds-only monetisation with no loot boxes avoids the main rating and regulatory issue.
- **Accounts are 13+** (2026-10-05, adopted from an agent suggestion): avoids COPPA's under-13 rules in the US. EU countries set the digital consent age between 13 and 16, so some players there need parental consent or a higher local minimum (to check with a lawyer before launch).
- **The rating applies to public player text** (2026-10-05, adopted from an agent suggestion): public chat, names, RP profiles and shared spaces stay within the rating; private chats aren't monitored, but can be reported and are judged by the same rules of conduct.
- **Safety defaults** (2026-10-05, adopted from an agent suggestion): the profanity filter is on by default and adults can turn it off. Players aged 13–17, from a neutral age prompt, also default to friends-only private messages. *Agent note:* guests would answer the same prompt when starting the prologue.

### Private-server licence

- **Comet and ShapeLand are MIT-licensed** (project lead, 2026-10-05; MIT picked over Apache-2.0 and dual licensing from agent options): the shortest licence, and the most common in the .NET ecosystem.
- **Project Anima's code is source-available, non-commercial** (project lead, 2026-10-05). The exact licence (e.g. PolyForm Noncommercial or a custom one) is chosen with legal advice before release (adopted from an agent suggestion).
- **Private servers may run Anima's official content and art non-commercially** (2026-10-05, adopted from an agent suggestion): free to host with official content and the official client, as long as they don't charge or sell items, and don't present themselves as official.
- **Private servers may host only the free (non-member) parts in the initial version** (project lead, 2026-10-05). **Enforced by free-only packages plus the licence** (adopted from an agent suggestion): private-server packages ship only the free parts' content (content builds can already be split per part), and the licence forbids hosting member content.

### Accessibility and translation

- **Launch in English, ready for more languages** (2026-10-05, adopted from an agent suggestion): the translation pipeline (`backend.md`) is in place at launch, and the UI is built for longer text and other scripts; languages are added by demand.
- **Community translations are welcome and reviewed** (2026-10-05, adopted from an agent suggestion): volunteers translate through a contributor workflow (string files in git or a translation platform), with official review before shipping. Also useful for private servers.
- **Accessibility baseline** (project lead, 2026-10-05, all four agent-proposed areas):
  - **Controls:** full rebinding on keyboard and controller, hold or toggle options, one-handed-friendly layouts.
  - **Vision:** colourblind-safe design (shapes and icons alongside colour on Rune slots, telegraphs and markers), UI scale and text size, high-contrast telegraphs.
  - **Hearing:** subtitles and visual cues for important audio (boss cues, nearby threats).
  - **Motion and flashing:** options to reduce camera shake, motion and flashing effects.
- **Difficulty scaling for content everyone must clear** (project lead, 2026-10-05): the main story is several interconnected quest chains, so any battle content meant to be cleared by all players can be marked as difficulty-scalable (an easier setting or NPC support). Dungeons, raids and hard bosses keep one difficulty (adopted from an agent suggestion).

### Privacy and data protection

Added 2026-10-05 as a 13th topic. Mostly base (Comet): these are mechanisms every game on it gets. To check with a lawyer before launch.

- **Account deletion: a grace period, then anonymisation** (2026-10-05, adopted from an agent suggestion): deletion takes effect after about 30 days and can be cancelled until then. Personal data (email, logins, profiles, chat) is deleted; ledger and moderation rows keep only an anonymous ID, so economy history and protection against ban evasion stay intact.
- **Guest characters are deleted after about 30 days without play** (2026-10-05, adopted from an agent suggestion); guests are told this up front.
- **Minimal data, no third parties** (2026-10-05, adopted from an agent suggestion): email and login data, IP addresses for security with short retention, an age bracket rather than a birthdate, no real names. First-party analytics only; no ad or tracking SDKs, so no cookie consent beyond essentials.
- **Private-server hosts handle their own players' data, with good defaults** (2026-10-05, adopted from an agent suggestion): Comet ships with retention jobs and deletion tools switched on by default, and the hosting docs say so.

### Security and anti-cheat

Added 2026-10-05 as a 14th topic. Base (Comet).

- **Two-factor authentication is optional and encouraged, with no reward** (project lead, 2026-10-05): authenticator apps and passkeys. **Required for staff accounts** (adopted from an agent suggestion).
- **Logins: email and password, and passkeys only** (project lead's pick, 2026-10-05): no third-party login providers; simplest and fully self-hostable.
- **Anti-cheat is server authority only** (2026-10-05, adopted from an agent suggestion): no client-side anti-cheat, since the web client can't run one and the client source is open anyway. The server validates everything that matters (movement, hits, cooldowns, items); PvE-only play keeps client hacks low-stakes.
- **Bots and automation: heuristic flags, human review** (2026-10-05, adopted from an agent suggestion): server-side heuristics (play patterns, inhuman timing, ledger flows to known sellers) flag accounts for moderators, as with movement violations; never auto-banned. Bonds and free-account limits already reduce the payoff.

### Comparable games

- **Comparables in the proposal** (project lead, 2026-10-04): Old School RuneScape, FFXIV, Classic WoW, Rabbit and Steel. Art inspirations (PSO, Crystal Chronicles, Signalis) are in `art.md`.
- **Candidates to research before deciding** (project lead, 2026-10-04; the project lead doesn't know them personally): Albion Online (player-driven economy; looks like an interesting comparable), Brighter Shores, Mabinogi, TERA and Blue Protocol. A comparable-research pass is a todo.
- *Placeholder: what makes this game different (project lead).*

### Setting and story

- **The project lead writes the lore themselves** (2026-10-04). The agent distilled what gameplay already implies into `lore-hooks.md` as raw material. *Placeholder: setting (project lead).*

## Considering

## Open

- Nothing open right now.
