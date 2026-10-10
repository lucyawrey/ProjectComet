# Proposal-level topics

**Layer: mixed.** Base (Comet): the two-layer section, team roles, roadmap phases 0–2, ShapeLand, privacy, security. Game (Anima): vision and pillars, business model, audience, core loop, economy, new players, character creation, age rating, licences for Anima, accessibility, comparables, setting.

Agent notes on what a game proposal covers beyond systems. **The pitch, pillar wording, "what makes it different", the Comet overview prose and the setting are the project lead's to write**; agent drafts of them exist in `.claude/drafts/` only because the project lead asked, and the setting has no draft.

## Decided

### Vision and pillars

- **Five pillars** (final wording is the project lead's):
  - *One world, anywhere:* instant browser play and one seamless world.
  - *A real, player-shaped world:* every item is real, and the economy, crafting, trading, housing, guilds and player events carry half the game.
  - *Be anything:* every class and craft on one character through crystals, with soft pressure to specialise.
  - *Approachable action:* forgiving action combat, a little like ARPGs and a little like tab-target games, but extremely approachable for casual players.
  - *Low-fi charm:* cute but cool, PS1-to-GameCube fidelity, a storybook world.
- **Self-hosting is a commitment that sits under the pillars:** it shapes the tech and business model more than how the game feels.

*Placeholder: elevator pitch (project lead).*

### Two layers: Comet and Anima

- **Comet is the base; Anima is the game built on it.** Comet runs a family of similar MMOs that share most systems. If the indie team doesn't want this exact game vision, Comet is still useful for a similar MMO that fits their goals better. **Comet carries a lot of assumptions about the game:** how combat and transport work in general, and more, so everything built on it will feel similar in many ways. Don't describe it as a base for "any" game. Specific gameplay can vary: another game might not have Class Crystals, for example.
- **Naming:** "Anima" is the game's working name. The repository keeps the name `ProjectComet`.
- **One proposal document in two parts:** Comet, then Anima. The team can take the first part on its own. **The Comet part is short:** framed as how the game is built, plus a section on reuse, not a second product pitch.
- **Games plug in as C# code modules plus their own content,** on top of Comet's libraries and servers. Systems like crystals can't be expressed by content data alone.
- **Games extend Protocol and Simulation through registry keys and extension points:** games ship their own assemblies next to Protocol and Simulation; game message types get numbers from the registry, as for database rows (`backend.md`); Simulation exposes extension points (effect, buff and skill-behaviour handlers registered by key). Everything is compiled in, so it works on web and IL2CPP builds.
- **What's in Comet,** beyond the obviously shared parts (seamless world, zones, channels and instances, transport, netcode, action-combat core, content pipeline, accounts, ops and moderation tools, privacy and security, self-hosting):
  - items and economy basics: inventory, Storage and bags as containers, slot rules, player trading, currency as items, the item ledger, an owner or binding field on items, tradeability tiers;
  - a basic crafting framework: inputs and outputs, recipe storage, crafter signatures, gathering nodes;
  - social basics: guilds, parties, chat, friends, the house instance kind;
  - a progression framework: XP, levels, learned flags, and a thin skill base that only executes actions (frame data, hitboxes, cooldowns, effects), not necessarily slots, so a game can build more traditional MMO skill advancement;
  - movement and attachment for companions: mounts as a movement mode, passengers, an entity following its owner;
  - instance lifecycle with entry and exit hooks, and teleport handoff;
  - channel placement with a pluggable preference policy, soft and hard caps;
  - flying and fixed-route vehicles; height zones and frame data.
- **Policy is game code; Comet keeps thin mechanisms.** Anima's own code covers: Class Crystals, Soul XP, Anima, Runes and how skills are acquired and slotted; companion records, capture, tasks, levelling and stables; soulbound rules (recall, the 24-hour return, lending, no-return trades, re-obtaining); gear sets as references into Storage, the outfit overlay and Outfit Magic; dungeon and teleport rules (loading rooms, re-entry votes, replacements, no lockouts, the teleport scene, cast rules); constellations and the channel preference order; the market; housing placement, furniture, guild halls and temporary structures; wear and repair, dyes, the item collection log and the coin purse; attunements and Anima Capacity; the business model (membership, parts and their gating, free-account limits, bonds). Comet keeps at most a membership flag on accounts. Duplication in ShapeLand is accepted (e.g. its own simple mounts or basic equip).
- **Keeping the split cheap** (a clean split in one codebase costs roughly 10–20% more up front; a true product engine 1.5–2×; the main risk is guessing abstractions with only one real game):
  - **Extract, don't pre-build:** a system enters Comet only once both ShapeLand and Anima use it; Anima-only systems stay in Anima until another game needs them. The layer tags in the design notes are expectations, not build orders.
  - **One repo, one solution, no API stability:** Comet isn't a product until a second real game exists. Breaking changes are fine and both games are fixed in the same commit; no versioned packages.
  - **Extension points only when needed:** plain C# interfaces and registration by key, and no hook without a current caller.
  - **ShapeLand stays tiny:** a test fixture that happens to be playable (still the reference game); every system it adopts is maintenance.
- **How the code is laid out:**
  - **Client:** Comet is a Unity package (networking, entity replication, prediction, zone streaming, input plumbing); **each game is its own Unity project**, with its own UI, rendering and art, **all in the one monorepo**.
  - **Servers:** each game builds its own server programs (e.g. `Anima.GameServer`, `ShapeLand.GameServer`) that reference Comet's libraries.
  - **Content and bots:** each game has its own content folder and ID registry, and Comet has its own keys; the content build merges Comet's content with one game's. The bot framework is in Comet, with bot behaviours per game.
- **Design notes carry a Layer line** (base, game or mixed); the real split happens in the human-readable docs.

### ShapeLand

- **ShapeLand is Comet's demo game for phases 0–2,** and stays as the reference game: shown in the Comet part of the proposal, kept working as Comet evolves, and shipped as the sample game for anyone building on Comet or testing a private server. Early demos don't need to be Anima; Anima's own systems start at phase 3. The early work stays useful whatever game the team picks.
- **Concept:** players are basic coloured shapes in similarly basic shape environments, who slide around, chat and engage in basic magical combat. Needs no art assets or animation.
- **All combat is magic** because melee animations would be very awkward on shapes.
- **Players and monsters never share shapes.** Players are faceted (flat-faced) shapes: a cube, a **diamond** (a tall square bipyramid) and a pyramid. Monsters are curved: spheres, capsules, cylinders and tori, with no cones (a cone's silhouette matches the pyramid).
- **Shapes are classes** with four spells and the same small hitbox as every player (`combat.md`): the cube is wide and sturdy with short range; the diamond tall, thin and fast with mid range; the pyramid low and wide and ranged. Each has a projectile, an area spell, a dash and one signature spell. All can jump and dodge, so height zones and invulnerability get exercised.
- **Enemies: shape monsters and one boss:** simple curved enemy shapes with a few attack patterns, plus one big boss for the named 100-player boss load test. PvE, like Comet.
- **It grows with the phases:** phase 0, sliding, jumping and chat (`prototype.md`); phase 1, combat plus paint items that recolour your shape (items, inventory, the ledger, trading) and XP and levels (the progression framework), all persisted; phase 2, several zones, a border, a channel, a dungeon instance and a teleport.
- **Props are built from primitives in Unity** by the project lead: a prop tool in the editor combines primitive shapes (box, wedge, cylinder, sphere…) with colours into a saved prop, and generates its mesh and collision. No external modelling.
- **Feel reference** (project lead): NieR: Automata's hacking minigames, but multiplayer.
- **World look: a playful toy box of floating geometric islands:** bright primary colours and simple props; terrain sits on islands such as large floating discs that drop off abruptly into open sky.

### Business model

- **Free-to-play with membership.**
- **The game is split into parts; free players are always locked to an earlier part than members.** The initial release has two parts (early and late game); future parts are expansions. Free players get each part eventually, as long as new parts keep coming. This means lower level caps for free players, classes added in later parts locked, and so on. **No overall features are barred from free players.**
- **Free accounts have anti-abuse limits until earned** (an exception to "no features barred"): new free accounts have limited trading, market and public chat until they pass a playtime or level threshold, or have ever held membership. Blocks throwaway bot accounts without walling off honest free players.
- **Lapsed membership keeps everything, capped:** nothing is lost; level is capped to the free part's cap and later-part classes are locked; anyone in a later-part zone is moved to the nearest free location on login. Everything returns on renewal.
- **Part borders in the seamless world:** an in-world barrier with a clear membership message, plus story gates. Most areas can still be reached without the story, through less convenient routes.
- **Free and paid players group freely in free-part content only;** free players can't enter later-part content, even with a member.
- **Items aren't restricted by part:** free players can buy, trade and hold any item. Use is limited only by level and class requirements.
- **Expansions are included in membership;** nothing is sold separately.
- **A tradeable membership item (like Old School RuneScape's bonds) is the only thing bought with real money:** bought with money, sold on the market for in-game currency. Sometimes disliked, but better than a cash shop or loot boxes, and one of the better ways to fund the game.
- **No item shop:** nothing else sold for money becomes an item, protecting "every item is real" and the economy.
- **No loot boxes or other paid randomness.**
- Anima is never sold for real money (`classes.md`).

### Audience and platforms

- **Audience:** cosy and social MMO players; MMO nostalgics in general, from browser RuneScape to classic WoW; and high-end raiders. The combat system is simple to understand and hard to master, with complex boss mechanics like FFXIV's or Rabbit and Steel's (see `combat.md`).
- **Platforms:** desktop browsers (the main goal) and a desktop app. Mobile browsers and consoles are not targeted.
- **Sessions: short and long:** meaningful progress in 15–30 minutes (crafting, gathering, a quick dungeon), with longer sessions for raids and events.

### Team, roadmap and budget

- **The proposal lists roles needed, not people** (in a small team one person covers several):

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

- **Prototype Comet + ShapeLand early,** before the human-readable proposal is done. Not started yet; planning the prototype starts now.
- **Roadmap: phases with goals and exit criteria, no dates.** Phases 0–2 build Comet using ShapeLand.

  | Phase | Goal | Done when |
  | --- | --- | --- |
  | 0. Prototypes | Two-stage load test; shared source packages in Unity; TOML 1.1 editor support; texture filtering comparison | The load test meets thresholds written down beforehand |
  | 1. Vertical slice | ShapeLand in one zone with its core loop, 100+ bots, simulated latency from day one | Movement and combat feel responsive under simulated latency, and performance meets thresholds |
  | 2. Seamless-world proof | Two or more zones with border handoff, channels, a dungeon instance, a teleport | Crossing borders is invisible under simulated latency |
  | 3. Closed alpha | Anima's early part playable: accounts, persistence, classes, items, crafting, basic moderation tools, private-server packaging | Stable with real players, and they come back |
  | 4. Open beta | Early part complete, late part in progress, membership and bonds, ops tooling | Scale and economy hold up with a real population |
  | 5. Launch | Early part (free) and late part (members) | |
  | 6. Expansions | New parts; free players move up a part | |

  Phase 2 proves the biggest risk (the seamless world) before content production. Whether Anima is fun is tested at phase 3.
- **Budget: hosting costs only;** development budget is left to the team. Cost categories: game servers (most of it), Region server processes, PostgreSQL with backups, Valkey for larger regions, and CDN bandwidth for web client, asset and content downloads (every new browser player downloads them). *Placeholder: numbers after phase 0 (players per CPU core from the load test).*

### Core loop, endgame and content delivery

- **Two intertwined loops:** an adventure loop (quests, dungeons, bosses → drops and XP) and a life loop (gather, craft, trade, house), each feeding the other through materials and gear. Matches the even content split.
- **Endgame:** raids and hard bosses; more classes (every class on one character, promotions, Soul XP unlocks); crafting and economy mastery; collection and housing; and **social systems**: multiple guilds, robust chat, lots of emotes, encouraging roleplay, and housing for building community spaces.
- **Gear: a middle ground:** new tiers each expansion, but older gear keeps uses (outfits, Runes, niche stats) and isn't instantly worthless. Protects "every item is real".
- **RP profiles: simple:** a short player-written profile (bio, RP status such as "in character" or "looking for RP", a few fields) others see by inspecting a character; reported and moderated like chat.
- **Minigames: both kinds, but few in the initial scope:** activity minigames (OSRS-style instanced activities such as team games, fishing contests and races) and casual side games (Gold Saucer-style card and arcade games).
- **Older content stays alive through level sync and fill bonuses:** players sync down to a dungeon's level, with XP bonuses for roulette or content fill and for helping new players.
- **Player events: an event board and venues:** an in-game event calendar or board (including guild events), with housing and guild halls as venues; hosts get no special powers.
- **A content guide that helps players find everything** (not knowing where content is unlocked is frustrating):
  - **Covers all unlockable content:** dungeons, raids, classes, crafts, minigames, attunements, companion capture, side story chains; anything gated behind a quest or condition, with where and how to unlock it.
  - **Everything is listed, with details on demand:** every entry exists in the guide from the start; requirements and the starting NPC's location show on demand, with story spoilers hidden until reached. **The default view is closer to reveal-as-you-go**, so it isn't overwhelming.
  - **Map markers and tracking:** choosing an entry puts its next step on the map and in the quest tracker, and shows what's blocking it (level, a prior quest, an attunement).
- **No permanently missable rewards:** seasonal events recur yearly and older rewards come back, in later runs or from a vendor.
- **Daily and weekly bonuses are optional and catch-up friendly:** missed ones accumulate or don't matter much. Anima regeneration already paces play.
- **Content delivery: expansions plus regular patches:** expansions add parts; smaller patches every few months add story, dungeons, a raid tier and seasonal events. Fits maintenance-window deploys.

### Economy design

- **Market: a region-wide exchange with taxes:** one order-book market per region, reached at market boards in settlements. **Player stalls are a later feature.**
- **Gear comes from a mix of crafting and drops,** with **co-crafting**: a player who has the right materials but not the crafting stats can have the item crafted with another player or an NPC.
  - **With an NPC: fixed fee, capped quality:** the NPC crafts for a coin fee (a sink) at a fixed, middling quality and can't make top-tier items.
  - **With a player: both credited:** the signature shows both names and craft XP is split.
- **Money faucets: between modest and classic:** quests, NPC selling and some monster coin.
- **Exchange buy limits on some items:** per-item buy limits over a time window for scarce or volatile items, set in content data; most items have none.
- **Economy monitoring: dashboards only:** faucets, sinks and money supply per region, charted from the ledger for designers; no automated alerts.
- **Sinks:** market tax; repairs and NPC services (re-obtaining fees, housing upkeep, guild hall costs); crafting consumption, salvage and breakdown; cosmetic and prestige purchases from NPCs for coin (cosmetics, furniture, housing plots, titles).

### New-player experience

- **Guest play, upgrade later:** new players play the prologue as a guest straight from a link; making an account keeps the character. Guests get the free-account limits plus no trading and no public chat.
- **A short prologue teaches the basics and introduces Class Crystals with Freelancer:** it opens in the unclassed state (`classes.md`), teaches movement, dodging and basic combat, grants Freelancer partway through, and **ends with promoting Freelancer to a tier 2 class.**
- **The prologue happens where the character will stay;** a class's quests may encourage travel elsewhere.
- **Systems are mostly unlocked by side quests, not the main story:** each part has a main story, but it rarely gates systems. Most systems sit behind a quest the main story brings you near, and players can travel to those quests to unlock a system early. The content guide shows where they are.
- **New players are shown with a visible marker, and helping them gives a bonus.**

### Character creation

- **Ancestry is cosmetic and story only:** it sets appearance options, the prologue location and some story flavour, with no stat differences, so no ancestry is wrong for a class (fits "Be anything").
- **The creator uses presets plus some sliders:** face, hair and body presets per ancestry, colours, and a handful of sliders (height, build). Fits low-poly art and keeps outfits fitting. Appearance storage is in `database.md`; body and ancestry art rules in `art.md`.
- **The prologue location is set by ancestry:** each ancestry has its own prologue area, which is also the character's home area.
- **Changing appearance later:**
  - **Barbers charge coin for minor changes** (hair, makeup). They simply exist; no quest needed.
  - **Magical changes cost Anima** and cover both minor and major changes: players use them for the expanded options, or to save coin at the cost of their limited Anima pool (Anima is usually for big magic). Unlocking magical changes takes a quest.
  - **Changing ancestry costs Anima and needs its own, separate unlock quest.**

### Age rating and minors

- **Aim for a teen rating (PEGI 12 / ESRB T):** fantasy violence, mild language, player interaction; room for moody zones and darker story moments. Bonds-only monetisation with no loot boxes avoids the main rating and regulatory issue.
- **Accounts are 13+:** avoids COPPA's under-13 rules in the US. EU countries set the digital consent age between 13 and 16, so some players there need parental consent or a higher local minimum (to check with a lawyer before launch).
- **The rating applies to public player text:** public chat, names, RP profiles and shared spaces stay within the rating; private chats aren't monitored, but can be reported and are judged by the same rules of conduct.
- **Safety defaults:** the profanity filter is on by default and adults can turn it off. Players aged 13–17, from a neutral age prompt (guests answer it when starting the prologue), also default to friends-only private messages.

### Licences and private servers

- **Comet and ShapeLand are MIT-licensed.**
- **Anima's code is source-available, non-commercial.** The exact licence (e.g. PolyForm Noncommercial or a custom one) is chosen with legal advice before release.
- **Private servers may run Anima's official content and art non-commercially:** free to host with official content and the official client, as long as they don't charge or sell items, and don't present themselves as official.
- **Private servers may host only the free (non-member) parts in the initial version.** Enforced by free-only packages plus the licence: private-server packages ship only the free parts' content (content builds can already be split per part), and the licence forbids hosting member content.

### Accessibility and translation

- **Launch in English, ready for more languages:** the translation pipeline (`backend.md`) is in place at launch, and the UI is built for longer text and other scripts; languages are added by demand.
- **Community translations are welcome and reviewed:** volunteers translate through a contributor workflow (string files in git or a translation platform), with official review before shipping. Also useful for private servers.
- **Accessibility baseline:**
  - **Controls:** full rebinding on keyboard and controller, hold or toggle options, one-handed-friendly layouts.
  - **Vision:** colourblind-safe design (shapes and icons alongside colour on Rune slots, telegraphs and markers), UI scale and text size, high-contrast telegraphs.
  - **Hearing:** subtitles and visual cues for important audio (boss cues, nearby threats).
  - **Motion and flashing:** options to reduce camera shake, motion and flashing effects.
- **Difficulty scaling for content everyone must clear:** the main story is several interconnected quest chains, so any battle content meant to be cleared by all players can be marked as difficulty-scalable (an easier setting or NPC support). Dungeons, raids and hard bosses keep one difficulty.

### Privacy and data protection

Comet mechanisms that every game gets. To check with a lawyer before launch.

- **Account deletion: a grace period, then anonymisation:** deletion takes effect after about 30 days and can be cancelled until then. Personal data (email, logins, profiles, chat) is deleted; ledger and moderation rows keep only an anonymous ID, so economy history and protection against ban evasion stay intact.
- **Guest characters are deleted after about 30 days without play;** guests are told this up front.
- **Minimal data, no third parties:** email and login data, IP addresses for security with short retention, an age bracket rather than a birthdate, no real names. First-party analytics only; no ad or tracking SDKs, so no cookie consent beyond essentials.
- **Private-server hosts handle their own players' data, with good defaults:** Comet ships with retention jobs and deletion tools switched on by default, and the hosting docs say so.

### Security and anti-cheat

Comet mechanisms.

- **Two-factor authentication is optional and encouraged, with no reward:** authenticator apps and passkeys. **Required for staff accounts.**
- **Logins: email and password, and passkeys only:** simplest and fully self-hostable.
- **Anti-cheat is server authority only:** the web client can't run client-side anti-cheat and the client source is open anyway. The server validates everything that matters (movement, hits, cooldowns, items); PvE-only play keeps client hacks low-stakes.
- **Bots and automation: heuristic flags, human review:** server-side heuristics (play patterns, inhuman timing, ledger flows to known sellers) flag accounts for moderators, as with movement violations; never auto-banned. Bonds and free-account limits already reduce the payoff.

### Comparable games

- **Comparables in the proposal:** Old School RuneScape, FFXIV, Classic WoW, Rabbit and Steel, Albion Online. Art inspirations (PSO, Crystal Chronicles, Signalis) are in `art.md`.
- **Researched candidates and inspirations** (Brighter Shores, TERA, Blue Protocol and the Quick Notes inspirations): see `.claude/research/comparables.md` for agent suggestions on using them as inspirations for single systems. Not yet decided.
- *Placeholder: what makes this game different (project lead).*

### Setting and story

- **The project lead writes the lore themselves.** `lore-hooks.md` collects what gameplay already implies, as raw material. *Placeholder: setting (project lead).*

## Considering

- Nothing being considered right now.

## Rejected

- **Mabinogi as a comparable** (project lead).
- **Paper Mario as a reference** (project lead).
- **Self-hosting as a pillar:** it's a commitment underneath them instead.
- **Business-model mechanisms in Comet:** ShapeLand would never use them, so membership, parts and bonds are game code.
- **Generic Comet servers that load game modules** (a plugin loader): each game builds its own server programs.
- **One Unity project building both games:** it would mix their assets.
- **Comet as a separately versioned product with API stability** before a second real game exists.
- **Anima as the early demo** (phases 0–2): ShapeLand is used instead.
- **A pill-shaped player shape** in ShapeLand: replaced by the diamond so players and monsters don't share shapes.
- **Ancestry stat effects,** including ancestry in Freelancer's attribute formula.
- **Third-party login providers;** **client-side anti-cheat;** **automatic bans** from heuristics.
- **Apache-2.0 or dual licensing** for Comet: MIT is shorter and the .NET norm.
- **A mentor system or new-player chat channel.**
- **Automated economy alerts.**
- **Rewards for enabling two-factor authentication.**

## Open

- Nothing open right now.
