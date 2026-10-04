# Backend and data model

Agent notes on backend architecture and data-model ideas. Almost everything here is still open or an agent suggestion.

## Decided

- **Private servers should be easy to self-host**, without a large proprietary dependency.
- **Unity is the engine** (decided 2026-10-04; reaffirmed the same day). Web export is a main project goal (classic in-browser MMO play), and Unity is the only engine where that isn't difficult or too limited.
- **No headless Unity server and no Unity networking package (Mirror, FishNet, Netcode…) for now** (2026-10-04). The server is plain .NET using a message-level library. Fallback if rolling our own proves too hard: FishNet with headless Unity servers (FishNet seems to scale well).
- **Three-part service map** (2026-10-04, for approach A):
  - **Login server** (renamed from Gateway): public HTTP. Accounts, auth, character select, signed tickets for joining a game server or listening across a border. Clients connect to game servers directly; the Login server doesn't relay game traffic.
  - **Region server** (renamed from Data Center): one logical Region server per region, which may run as more than one process for scaling (2026-10-04). Coordinates everything that spans instances: instance placement and lifecycle (channels and dungeon instances), parties, guilds, friends, chat routing, and the cross-channel economy (market, mail, trades between channels). Split pieces out only if load forces it.
  - **Game servers:** live simulation and in-memory state of one or more instances (channels, dungeon instances, house instances).
  - PostgreSQL and a static host for the web client and streamed assets sit alongside these.
- **No central data service; a shared database/model library instead** (2026-10-04, replacing the earlier Region-server-owns-the-database decision). Game servers and the Region server both use one C# model/data library and talk to PostgreSQL directly, as Albion, EVE and AzerothCore do. Trade-offs accepted: every process holds database credentials, and schema migrations must be coordinated across processes.
- **Seamless handoff: pre-warm + delta** (2026-10-04). When the client starts listening across a border, the new game server preloads the character from the database (the same path as login). At the crossing only the live-state delta moves. Items and currency never travel in a handoff because they're already persisted.
- **Character ownership is a versioned database row** (2026-10-04). At handoff the old game server freezes the character, then the new one claims it atomically (`UPDATE … SET owner = new, version = v + 1 WHERE version = v`). Every character write carries the version its server holds, so late writes from the old server fail (fencing). The Region server subscribes to ownership changes for presence (placement, parties, chat routing) but isn't on the handoff path.
- **The live-state delta travels directly** from the old game server to the new one (2026-10-04).
- **Teleports reuse the border handoff** (2026-10-04). When the cast starts, the destination server preloads the character and the client starts streaming the destination. When the cast finishes, ownership is claimed and the live-state delta moves, behind the teleport scene. The scene lasts a fixed time even if loading finishes early; the character may already be at the destination as far as the backend is concerned. Cancelling the cast drops the destination server's preload. A teleport carries the same state as a border crossing (below).
- **Dungeon loading rooms are part of the instance** (2026-10-04). The instance is created when the first party member reaches the dungeon entrance; the entrance acts as a small border into it, and the client streams the dungeon while the party gathers in the room. If everyone leaves the loading room before the dungeon starts, the instance is disposed of quickly.
- **Instances must be very cheap to create and dispose of** (2026-10-04): not a heavy concept like a Unity scene. One game server runs many instances.
- **Channels and dungeon or house instances are one concept in code** (2026-10-04): an instance is a running copy of a zone file on a game server. Channel, dungeon instance and house instance are policies on top of it: who may enter, placement, borders, lifetime.
- **A region is a separate world copy** (2026-10-04), like a classic MMO server: its own characters, economy and guilds. This holds especially for prototypes; a single shared world could be approached much later. **A region always shares one database**, even when it runs several Region server processes.
- **Plan for 5,000–10,000 peak concurrent players per region** (2026-10-04).
- **Region server scaling** (2026-10-04):
  - **Stateless Region server processes.** Durable state (parties, friends, guilds, market, mail, instance placement) lives in PostgreSQL; processes keep only caches, and presence is rebuilt from the character ownership rows. Any process can serve any client. (A single Region process per region is still worth considering; see Considering.)
  - **Singleton jobs run on whichever Region process holds a PostgreSQL advisory lock:** placing instances, cleaning up dead instances, refunding Anima escrow after a crash. If that process dies, another takes the lock. Placement is an atomic database write, so nothing is placed twice.
  - **Valkey** (the BSD-licensed Redis fork) is the SignalR backplane for multi-process regions. A single-process region (e.g. a private server) needs no backplane.
  - **One PostgreSQL primary per region;** read replicas are the first step if reads become a bottleneck.
- **Carried across a border:** cooldowns, and buffs and debuffs with their remaining duration. **Not carried:** monster aggro. **A skill in progress delays the handoff** until it finishes (invisible, since handoff already happens a few metres past the line).
- **Netcode is designed for WebSocket as the worst case** (2026-10-04). WebTransport would need a WebSocket fallback anyway (some networks block UDP), so the game must play acceptably over TCP; better transports only make hiccups rarer.
- **All clients start on WebSocket** (2026-10-04), desktop included: one code path to build and load-test. UDP for desktop and WebTransport for web are possible later upgrades behind one transport interface, added only if load tests show stalls hurt.
- **Libraries (2026-10-04):**
  - **Social traffic uses SignalR to the Region server.** Clients hold two connections: a raw WebSocket to their game server (gameplay only) and SignalR to the Region server (chat, parties, guilds, friends, presence), using its groups, reconnection and scale-out backplane (Valkey, 2026-10-04).
  - **MessagePack-CSharp for serialisation** (SignalR's own binary format; IL2CPP-safe via source generators). MemoryPack stays an option for hot paths later.
  - **gRPC (ASP.NET Core) for server-to-server calls** (Region ↔ game servers, the handoff delta between game servers).
  - **Rest of the stack, to try:** ASP.NET Core (Kestrel) WebSockets on game servers (proven as a web and WebSocket server, e.g. under SignalR, but no shipped real-time game server on it was found), with our own message layer (framing, latest-only send queues, dispatch; LiteEntitySystem's source as a possible reference); NativeWebSocket on the Unity client (web via its built-in `.jslib`; used by Colyseus's Unity SDK), plus the SignalR JavaScript client behind a `.jslib` on web; Npgsql + EF Core (Dapper for hot paths), with EF concurrency tokens on PostgreSQL's `xmin` for the versioned ownership row; ASP.NET Core minimal APIs with Identity or JWT bearer tokens for the Login server.
  - **The 100-player test is the gate for this stack** (project lead, 2026-10-04). If it fails, that may mean the tech isn't there yet for browser MMOs, or that we should consider switching languages (FishNet + headless Unity is also still the fallback).
  - **The test is split so a failure points at its cause** (decided 2026-10-04, from an agent suggestion; all experimental): first a stack-only benchmark (bare Kestrel sending position-sized MessagePack messages to 100–300 WebSocket bots at the planned tick rate, with simulated loss, no game logic), then the full game test (simulation, interest management, our message layer). Stack fails → the stack or language is the problem; only the full test fails → our code is. Watch garbage-collection pauses specifically and tune .NET (server GC, low-latency modes, fewer allocations in the send path) before blaming the language.
- **Bot load tests run over the baseline transport (currently WebSocket) with simulated packet loss** (2026-10-04), so TCP stalls show up early.
- **Shared libraries** (2026-10-04):
  - **Four libraries** (2026-10-04). Boundaries follow who needs the code, how often it changes, and whether it must compile under Unity.
    - *Protocol:* the network contract. Message types and IDs, MessagePack setup, the transport interface, SignalR hub interfaces for the Region server. Messages are plain data and never reference simulation types. Used by the client, game servers, Region server (hubs), bots and network test tools. Kept separate because changes need client/server version coordination, and bots and tools need it without the rules.
    - *Content:* types and readers for content definitions (items, skills, classes, monsters) and the zone format (chunks, placements, collision volumes). No dependencies beyond MessagePack and `System.Numerics`. Used by everything, including the Unity editor tools and the content build tool.
    - *Simulation:* rules that must match on client and server: frame-data execution, hitboxes and height-zone masks, hit checks, movement and knockback curves, cooldowns and buffs, tick maths. Depends on Content. Used by the client, game servers and bots.
    - *Data:* EF Core entities, `DbContext`, migrations, transactional operations. Depends on Content. Server-only (game, Region and Login servers, migrator); never compiled by Unity, so it can use .NET 10.
    - Protocol, Content and Simulation must compile under Unity (.NET Standard 2.1, roughly C# 9, until Unity 6.8). If the split feels like too much ceremony, Protocol and Content are the easiest pair to merge.
  - **Unity consumes Protocol, Content and Simulation as shared source packages:** one source folder that is both a .NET project (targeting `netstandard2.1` and `net10.0` until Unity 6.8) and a local Unity package with an assembly definition, so Unity compiles the same files. To be checked against Unity 6 in a prototype.
  - **Zones use our own engine-neutral format, not Unity scenes** (2026-10-04). Chunked zone files in git are the source of truth: heightmap, placements (asset ID plus transform), collision volumes, spawns, borders and transition rooms. The server loads them directly; the client builds the world at runtime and streams chunks, loading meshes through Addressables by asset ID. Unity is the editor, through custom editor tools that load and save the format. Costs accepted: no built-in Unity scene workflow (terrain tools, prefab placement, baked lighting, lightmaps, occlusion culling) and our own editor tooling to maintain; low-poly art reduces the need for baked lighting.
  - **Outdoor zones can show LODs of other zones' static scenery** (2026-10-04), including far-away zones that aren't neighbours and outdoor-looking dungeon loading rooms. Only scenery: live entities only sync across borders and official entrances.
  - **Content (items, skills, classes, monsters) is authored outside Unity, as files in git** (2026-10-04): TOML (see below) validated by a schema, reviewed in pull requests. A build step turns them into one compact file that the client and servers both load. A small web editor can be added on top later.
- **Moderation roles** (2026-10-04, adopted from an agent suggestion): plan for three groups: the dev team, volunteer moderators with limited powers (as with RuneScape's Player Moderators, Wurm Online, Ryzom and Tibia's Tutors), and private-server owners, who are admins of their own server. Roles such as moderator, GM and admin share the same tools; exact names and powers are open. **Guarding against abuse by volunteer moderators is a requirement** (project lead); how is open (see Open).
- **Keep the number of separate services small at this stage** (project lead, 2026-10-04). New tooling goes into an existing service unless load or security forces a split.
- **Admin tools: a web admin panel plus in-game GM powers** (project lead, 2026-10-04). The panel handles most work (report queue, chat review, sanctions, character and item lookups, restores); GM powers cover what must happen in the world (invisibility, teleporting to a player, observing, removing a stuck character), handled by game servers for accounts with the GM role. Both use the same permission checks and write to one moderator action log.
- **The admin panel is part of the Login server** (2026-10-04, adopted from an agent suggestion): pages and endpoints behind an admin role, reading data through the shared Data library and sending live actions (mutes, disconnects) to the Region server over gRPC. Private servers get it automatically. Can be split out later if it grows.
- **Reports** (2026-10-04, adopted item by item from agent suggestions): a report is a short form (category plus optional free text), and the server attaches the evidence:
  - who reported whom, when, and where (region, zone, channel or instance, position);
  - recent chat involving the reported player that the reporter could see (nearby, party, and private chat between the two), from the chat buffer below;
  - the reported player's recent automatic flags (such as movement violations);
  - for trade or scam categories, recent item and currency movements between the two players (needs the audit trail, still open).
  Player screenshots or clips are a maybe for later: they mean hosting uploads.
- **Chat: as little logging as possible** (project lead, 2026-10-04, for privacy). There is no general chat log:
  - All chat types, private messages included, pass through a **rolling buffer of up to 24 hours**.
  - **A report saves the relevant chat** from the buffer. **When the report is resolved, the saved chat is deleted**, unless a moderator flags it to keep as evidence. **Report history** (who, what, when, outcome) is kept even after its chat is gone.
  - **Access** (adopted from an agent suggestion): volunteer moderators only see chat attached to reports; staff (GM or admin) can also search the buffer, and every search goes into the moderator action log.
  - **Storage** (adopted from an agent suggestion): a PostgreSQL table partitioned by hour, written by the Region server as it routes chat. A Region server singleton job creates upcoming partitions and drops those older than 24 hours (no extension needed; dropping a partition avoids the dead rows that bulk deletes leave).
  - If this becomes real with EU players, GDPR applies: state the retention in the privacy policy.
- **Item and currency ledger** (2026-10-04, adopted from agent suggestions): an append-only ledger row is written in the same database transaction as each movement (what, from, to, reason, actor, where, when), so it can't disagree with reality.
  - **Scope:** creation, destruction and every change of owner (trade, market, mail, drops and pickups, guild storage, GM actions). Moves within one owner's inventory, bags or Storage are not logged. High-volume stackable resources are summarised (e.g. one row per gathering session).
  - **Retention:** monthly partitions in PostgreSQL for about 6 months, then exported to compressed archive files and dropped; economy summaries are kept forever. Archiving can be disabled (including on official servers) and old archives removed by hand. Player-facing details such as "crafted by" live on the item, not in the ledger.
  - **Uses beyond moderation:** economy balancing (currency sources and sinks, real drop and craft rates), player support and precise fixes after bugs, debugging duplication, and player-facing features (guild storage logs, personal trade and market history).
  - **Cost:** one extra insert per movement inside an existing transaction; storage is the real cost (rough guess 1.5–2 GB a day for a full 10,000-player region; to be measured).
- **Sanctions** (2026-10-04, adopted from agent suggestions):
  - **A standard ladder:** warning (a recorded note the player sees), mute (chat only, timed), trade restriction (no trading, market or mail, timed), suspension (no login, timed) and permanent ban. Each has a reason and an expiry the player can see. **Permanent bans stay permanent unless explicitly lifted** (project lead).
  - **Applied to the account,** across all its characters and regions; records still show which character was involved.
  - **Tiered powers,** configurable per server: volunteer moderators can warn and mute for up to 48 hours (RuneScape's Player Moderator limit); GMs can apply all timed sanctions; admins can issue permanent bans and lift them.
  - **Appeals** go through the Login server's web account page, which shows active sanctions and stays reachable when banned. One appeal per sanction, reviewed by a different staff member than the one who issued it.
- **Rollbacks** (2026-10-04, adopted from agent suggestions):
  - **Targeted restores are the normal tool:** GMs restore or remove specific items and currency (e.g. duplicates traced through the ledger). Each change is a new ledger row with reason "GM action" plus a moderator action log entry; history is never edited.
  - **Region-wide rollback is an admin-only last resort** (catastrophic duplication or data corruption): the region goes offline and its database is restored to a point in time from continuous PostgreSQL backups (WAL archiving; tool such as pgBackRest or WAL-G, chosen later).
- **Guarding against moderator abuse** (2026-10-04, adopted from agent suggestions), on top of the action log, tiered powers, report-only chat access for volunteers and appeals reviewed by someone else:
  - **Conflict of interest is blocked:** moderators can't act on their own accounts, friends or guildmates (including GM restores); the report passes to someone else.
  - **Volunteers see only what's needed:** character names and report evidence, never emails, IP addresses or payment details.
  - **Oversight:** staff review volunteer actions, with per-moderator stats in the panel (actions taken, how often appeals overturn them); powers can be revoked instantly and staff can undo any volunteer sanction.
  - **Moderators are anonymous to players** ("a moderator"); staff always see who acted.
  - **Two-person approval** for the biggest actions (large restores, lifting a permanent ban, region rollback) is supported and configurable: on by default for official servers, can be turned off for small teams and private servers.
  - **Staff powers live on separate staff accounts,** not on the accounts staff play on. GM and admin characters can use custom appearances and are always clearly marked as GMs (project lead, 2026-10-04).
- **Load-test metrics** (2026-10-04, adopted from agent suggestions), measured from the first run of both stages:
  - *Server:* tick duration (median, worst 1%, ticks over budget), garbage-collection pause count and length, allocation rate, send-queue depth per connection and messages replaced by latest-only queues, bytes in and out per player, CPU and memory.
  - *Bots:* round-trip time, the gap between received updates (where TCP stalls show first), correction count and connection failures.
  - **Tooling:** .NET's built-in metrics (`System.Diagnostics.Metrics`) exported with OpenTelemetry, from servers and bots alike; `dotnet-counters` for quick looks and the standalone .NET Aspire dashboard for test runs. Prometheus and Grafana are for real deployments later.
  - **Pass/fail thresholds are written down before each stage runs** (numbers set once the tick rate is chosen).
- **Deploys use maintenance windows; rolling updates are never planned for** (project lead, 2026-10-04: much easier for a small team). A region updates all at once (migrate, deploy, restart), so processes and clients never run mixed versions; handoff and the protocol don't need to work across versions. Web clients pick up a new build at login.
- **Content files are TOML 1.1, read and written with Tomlyn** (project lead, 2026-10-04; JSON ruled out). `[section]` headers for top-level structure and multi-line `{ }` inline tables for nested data such as skill frames and loot tables. Tomlyn (BSD-2) targets TOML 1.1 and keeps comments and formatting when rewriting files, which suits a future web editor. Revisit only if Tomlyn shows unforeseen problems in production. To check in a prototype: editor support for TOML 1.1 (e.g. Taplo, "Even Better TOML" in VS Code).
- **Content layout and IDs** (2026-10-04):
  - **Strictly one file per entity** (project lead), in folders by type with any subfolders. Each file states its own `id`, so files can move freely; the build checks uniqueness.
  - **IDs are namespaced string keys** (`item.wayfarer_coat`, `skill.rising_slash`). Before the first public release they can change freely; after it, **renaming is allowed but discouraged** (project lead), e.g. for a badly named or offensive key.
  - **A committed registry (`content/ids.toml`) maps each key to a number, per type** (adopted from an agent suggestion). The content build assigns the next free number to new keys and writes it back; removed entities are marked retired. **Numbers never change and are never reused in production** (project lead). A rename changes only the string key; its number stays.
  - **The database, network and compiled content use the numbers** (adopted from an agent suggestion). An item row holds `type_id`; ledger rows, learned flags and class entries likewise. Names, icons and stats come from the loaded content.
  - **The migrator copies the registry into lookup tables** (e.g. `content_item_type(id, key)`) on each deploy, so foreign keys protect `type_id` and raw SQL can show keys (adopted from an agent suggestion).
  - **Drift checks** (adopted from agent suggestions): CI compares `ids.toml` against `main` (existing numbers still present, surviving keys keep their numbers, numbers only added or retired); the build fails on unresolved references in content and zone files, and on registry entries with no file that aren't marked retired; the migrator refuses to sync if the database knows a number missing from the registry, or a retired number still used by live rows without a data migration; the build generates C# constants from the registry (`ContentIds.Items.SilverMark`) so code never hard-codes string keys.
  - **Renames go through a rename command in the content tool** (adopted from an agent suggestion) that updates the registry key, the file's `id` and every reference in content and zone files. No rename history is kept (project lead). Removing content that players still own needs a data migration.
- **Mixed persistence** (2026-10-04): item, currency and progression changes are immediate database transactions, acknowledged before the game confirms them to the player. Position, HP and buffs are saved periodically and on handoff.
- **Inventory and Storage share most of their systems.** Crystals are not containers; they reference gear and outfit sets whose items live in Storage (see `items.md`).

## Considering

- **Server navmesh with DotRecast (2026-10-04, to verify in a prototype):** MIT C# port of Recast/Detour (active, 2026.1.3). Builds navmeshes from the zone files on both sides and handles server pathfinding and crowds, with streaming for large worlds.
- **Shared-library details (agent suggestions, 2026-10-04):** shared code uses `System.Numerics` types and converts to Unity types at the edges; no exact determinism or fixed-point maths (client predicts, server corrects); bots are plain .NET console apps using Simulation, Protocol and .NET's WebSocket client; a content version hash is checked at login; a migrator tool runs before each deploy (as FishMMO does); expand/contract schema changes matter less now that deploys use maintenance windows.
- **Dropping web was considered and rejected (2026-10-04).** The project lead weighed desktop-first with UDP (for netcode quality, less browser complexity, and engine freedom) but kept web as a main goal: in-browser play, in the spirit of RuneScape's Java-applet days, is the project's main appeal right now.
- **Game server:** likely a pure C# server, sharing a simulation library with the Unity client and bot clients (project lead, 2026-10-04).
- **Lightweight instances (agent suggestion, 2026-10-04):** a game server loads each dungeon's zone data and navmesh once, read-only, and shares it between all its instances of that dungeon. An instance is then only its live state (entities, monsters, doors, instance items) plus a tick slot, so creating one is an allocation and disposing of it is dropping the object. Locked-content Anima moves into escrow when the dungeon starts, not on entering the loading room, so a disposed room-only instance needs no refund.
- **A single Region process per region (project lead, 2026-10-04):** still open to it, e.g. if one process comfortably handles 5,000–10,000 players. The stateless design keeps that possible: one process is just the smallest deployment.
- **Current planning focus is approach A** (custom .NET stack); SpacetimeDB is still under consideration.
- **Self-hosting (agent suggestion):** a private server runs all three parts in one process, with Region server calls becoming plain method calls, plus PostgreSQL and a reverse proxy that handles TLS automatically (e.g. Caddy), shipped as one docker-compose file.
- **Backend:** plan for two approaches:
  - A. A custom .NET stack (Login server, Region server with PostgreSQL, pure C# game servers, Unity client).
  - B. A product like SpacetimeDB. The project lead is slightly biased against it because of the self-hosting requirement; verify current license terms before weighing it.
  - C (unlikely). A systems-language backend written with help from another developer, if neither A nor B works.
- **Data model (agent suggestions, not agreed):**
  - **Items:** every item has an owner (a character, or account Storage) and a location: inventory slot, Storage, inside a bag, on the ground (until despawn), or placed in a house, plus a slot index; soulbound items return to their owner instead of despawning. Gear and outfit sets are reference lists, not locations (one item may be referenced by several sets; see `items.md`). Container rules are data. Moving, trading and equipping are all location changes in one database transaction, so an item can never be in two places.
  - **Class entries:** current XP, highest level, when unlocked. A Class Crystal is an item with a soulbound owner, a reference to one of the owner's class entries, a bought/granted flag, and references to a loadout, gear set and outfit set. Promotion is one transaction: check requirement, move XP and create the entry if new, convert the crystal, charge resources.
  - **Loadouts:** one slot list per loadout; each slot has a kind (Skill or Rune), a colour, a binding if it's a Skill slot, and a locked flag.
  - **Mastery:** gear and class Runes share one mastery system; progress is stored per Rune on the item (gear, which can have multiple Runes) or on the class entry (class).
  - **Finite rune stones:** the first copy a character receives is marked soulbound on acquisition; later copies are ordinary items.
  - **Anima escrow for locked content:** entry moves Anima into a hold tied to the instance; a clear finalises it, anything else refunds it. On startup, holds from crashed instances are refunded.
  - **Per-player gathering nodes:** per-player records of which nodes have been used.
  - **Temporary structures** (campfires, pitched tents) exist only on the channel; nothing is persisted.

## Open

- **Zone format details** (parked 2026-10-04): chunk size and layout (fixed grid or variable, relation to zone borders); terrain editing (our own heightmap tools or conversion to and from Unity terrain); lighting (realtime and vertex colour only, or some baking per chunk); scope of the first Unity editor tools; how distant-zone LODs are generated, stored and streamed. Overlaps with art direction.
- **Moderation and admin tools:** decided 2026-10-04 (see Decided). Lesson to keep from Ultima Online's Counselors and EverQuest's Guides (scaled back after the AOL volunteer lawsuit, settled 2010): keep volunteer powers limited and check labour law before it becomes real.
- **Content file details:** schema tooling; how the build step produces the compact file and how it reaches web clients (bundled or streamed).
- **Ops tooling, deferred items** (scope decided 2026-10-04; moderation and admin tools, load-test metrics and deploys are decided, see Decided). Later, one line each in the proposal: desktop distribution (own patcher or a storefront), a community bot (e.g. Discord), and dashboard and alerting detail (Prometheus and Grafana for real deployments). Server discovery is probably the Login server's region list.
- **No battle-tested library does game-state replication for a pure C# server over WebSocket.** The game-server message layer is ours to write (its design is in `netcode.md`, 2026-10-04); FishNet + headless Unity remains the fallback if that proves too hard.
- **WebTransport upgrade paths (for later):** (1) Kestrel's experimental WebTransport using independent streams instead of datagrams (pure C#; preview feature); (2) our own WebTransport handshake on MsQuic via its C# interop (gets datagrams; protocol work); (3) wrap libwtf (MIT, C, on MsQuic); (4) wait for the open `System.Net.Quic` datagram API proposal. A Go/Rust proxy is ruled out by the C#-only rule. WebTransport's `serverCertificateHashes` might let private servers skip a domain and certificate for game traffic (unverified).
- **FishMMO** (https://www.fishmmo.com/): an MIT-licensed MMO template on FishNet + Unity. Not a base (no shipped games, not very actively developed) but a good reference codebase; see Research. FishNet itself is the fallback if we go back to headless Unity.

## Research (2026-10-04)

Agent web research to sanity-check the architecture. These are findings, not decisions. Primary sources (the Albion talk slides, Unity's manual) were blocked by the session's network policy, so details come from search summaries and should be verified before going into human-readable docs.

### Real MMO backends

- **Albion Online:** Unity client, C# servers on Photon. ~600 world "clusters" (~1 km² each) spread over game servers; separate Login, Chat, World (guilds), Marketplace and other servers. Cassandra for game data, PostgreSQL for accounts and markets. Game servers keep player state in memory, write every change immediately, and read only on login or server change; handoff between servers goes through the database. Lesson from a Cassandra bug that wiped all buildings in alpha: be careful with cutting-edge tech.
- **New World:** a seamless world split into grid squares spread across 7 stateless "hub" servers (non-adjacent squares per hub). State is batch-written to DynamoDB (~800k writes per 30 s).
- **EVE Online:** client → load balancer → Proxy nodes (sessions, public-facing) → SOL nodes (single-threaded simulation) → one large SQL Server database.
- **Guild Wars 2 megaserver:** a weighted load balancer scores every copy of a map by party, guild, language and home World. Close to our decided channel preference (which uses party, guild, friends and language but has no home World).
- **FFXIV (from emulators):** Lobby; a World server for everything global (zoning, linkshells, friends); Zone servers for battle and events.
- **AzerothCore (WoW emulator):** authserver + worldserver + MySQL, shipped as one docker-compose file. A benchmark for easy self-hosting.
- **Ryzom (open source):** many small services per shard (Ryzom's term for a whole world copy, like our region); a shard that needed 8 machines in 2004 now runs on one.

### Patterns

- Everyone has three layers: a thin public front door (login/proxy), simulation servers, and a few global services (guilds, chat, friends, market).
- The Western examples (Albion, EVE, AzerothCore) have game servers talk to the database directly. The BigWorld lineage (World of Tanks; open-source KBEngine) instead has a `dbmgr` process that owns all database access, with simulation processes backing up state through it. We chose the first pattern (shared library, direct access).
- Two persistence styles: write every change (Albion) or stateless servers with batched writes (New World). Both read state only when a player arrives on a server.
- PostgreSQL is a proven choice for anything transactional or query-heavy.

### Web and Unity

- **WebTransport is Baseline** since Safari 26.4 (March 2026), so unreliable datagrams are available in every major browser.
- **Unity's web networking lags:** Unity Transport supports only WebSocket on web (and only to other Unity Transport peers); Netcode for Entities doesn't support web. WebTransport from Unity likely means our own `.jslib` bridge.
- **Unity is moving to CoreCLR:** Unity 6.8 (later in 2026) drops Mono and targets .NET 10; CoreCLR dedicated-server builds are experimental in 6.7. This would let the client and a pure C# server share modern C#. How this applies to web builds (IL2CPP → WebAssembly) is unverified.
- **SpacetimeDB licence:** BSL 1.1, converting to AGPL v3 with a linking exception in 2031. The Additional Use Grant allows free production use of a single instance if not resold as a database service. A private server (one region) probably fits; the official multi-region setup may not. BitCraft (Unity client) runs its whole backend on it, split into global and region modules. Licence text not yet read directly.

### Plain .NET networking libraries (2026-10-04)

From search summaries; verify before relying on them.

| Library | Level | Web support | Status |
| --- | --- | --- | --- |
| **LiteNetLib** | Reliable UDP transport | None (UDP only) | Active: v2.1.4, May 2026. Largest known user is 7 Days to Die (co-op survival, small servers); otherwise mostly mods (Nitrox, Cities: Skylines, RimWorld). No MMO found. Built-in loss/latency simulation. |
| **LiteEntitySystem** (same author) | High-level: entities, synced variables, RPCs, client prediction, lag compensation, delta-compressed state | Custom transports supported, so a WebSocket transport could be written | Engine-agnostic (Unity, Godot, plain .NET). Aimed at fast-paced shooters and action RPGs. Unknown: interest management at MMO scale, licence. |
| **Riptide** | Message layer over UDP (TCP fallback) | None ("no web transport") | Maintained: v2.2.x. |
| **MagicOnion** (Cysharp) | RPC plus real-time StreamingHub over gRPC | Poor: gRPC needs HTTP/2 framing that browsers don't expose; WebGL builds have open issues | Active. Better fit for HTTP APIs than real-time play. |
| **Photon Server** | Reliable UDP (ENet-based), TCP and WebSocket; C# server SDK | Yes (WebSocket) | Proprietary, licensed. The only C# stack found proven at MMO scale (Albion). v5 runs only on Windows Server with .NET Framework 4.6/4.8; Linux/.NET Core was promised for v6 in 2020, with no release found. Free licence: 100 CCU, non-commercial, one machine. Paid self-hosted: ~$500/month (500 CCU, one node) or ~$1,500/month (5,000 CCU, +$0.30 per extra CCU). Private servers would each need a licence (a redistribution licence exists but is negotiated). |
| **DarkRift 2** | Message layer with a standalone server | Unclear | Community-maintained; last repo update January 2024. |

### FishMMO (read 2026-10-04)

Agent read of the repository (github.com/jimdroberts/FishMMO, MIT). Reference only; see Considering.

- **Stack:** Unity 6.2, FishNet with headless Unity servers. Transports via FishNet's Multipass: Tugboat (UDP, built on LiteNetLib) for desktop and Bayou (WebSocket) for WebGL, at the same time. A small ASP.NET Core server hosts the WebGL build.
- **Servers:** Login; World (relays between scene servers, balances load, tracks servers); Scene servers (all game logic, scene stacking and instances, ~100 players per scene). Scene-based, not a seamless world.
- **Database:** PostgreSQL + EF Core in a shared `FishMMO-DB` library with a separate migrator project; every server uses it directly, through static service classes. Redis is also wired in. This matches our shared-library decision.
- **Cross-server social features** (guilds, parties) work by scene servers polling "update" tables in PostgreSQL on a timer, using the database as a message bus. Our plan uses SignalR on the Region server instead.
- **Data model:** around 30 entities (character attributes, inventory, equipment, bank, hotkeys, item cooldowns, buffs, abilities and known abilities, skills, quests, achievements, factions, friends, mail, pets, guilds, parties, chat, server registries). Useful reference for the database-tables topic.
- **Ops tooling:** patcher and update server, app health monitor, Discord bot, IP-fetch service for server discovery.
- **Activity:** ~150 commits June–October 2025, then quiet until a few small community PRs in August 2026.
