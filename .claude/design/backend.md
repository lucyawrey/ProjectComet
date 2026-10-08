# Backend and data model

**Layer: base (Comet).** Comet provides generic mechanisms; where Project Anima is the user (Anima as the entry cost, crystals, the market, soulbound rules), the text says so.

Agent notes on backend architecture and data-model ideas.

## Decided

### Engine and approach

- **Private servers should be easy to self-host,** without a large proprietary dependency.
- **Unity is the engine.** Web export is a main project goal (classic in-browser MMO play), and Unity is the only engine where that isn't difficult or too limited.
- **High-quality web play is a goal down the line:** WebGPU rendering and WebTransport networking on web, both if possible (project lead). Each keeps a fallback (WebGL 2; WebSocket), so neither is needed to play. WebGPU gets a trial after phase 0; WebTransport is out of scope for now.
- **The server is plain .NET using a message-level library:** no headless Unity server and no Unity networking package (Mirror, FishNet, Netcode…) for now. Fallback if rolling our own proves too hard: FishNet with headless Unity servers (FishNet seems to scale well).
- **Keep the number of separate services small at this stage.** New tooling goes into an existing service unless load or security forces a split.

### Services

- **Three-part service map:**
  - **Login server:** public HTTP. The game's website and web client page, accounts, auth and sessions, character select, the admin panel, and signed tickets for joining a game server or listening across a border. Clients connect to game servers directly; the Login server doesn't relay game traffic.
  - **Region server:** one logical Region server per region, which may run as more than one process for scaling. Coordinates everything that spans instances: instance placement and lifecycle (channels and dungeon instances), parties, guilds, friends, chat routing, and the cross-channel economy (mail, trades between channels, and Project Anima's market as a game module running in the Region server). Split pieces out only if load forces it.
  - **Game servers:** live simulation and in-memory state of one or more instances (channels, dungeon instances, house instances).
  - PostgreSQL and a static host (or CDN) for the web client's build files and streamed assets sit alongside these.
- **The web client's website isn't purely static:** it keeps state such as being logged in, so the Login server serves the game's web page and session (logged in, character select), while large files (client build, assets, content) come from a static host or CDN.
- **A deployment** is one Login server with its account database plus every region it lists (see `glossary.md`). The official game is one deployment; each private server is another.
- **A region is a separate world copy,** like a classic MMO server: its own characters, economy and guilds. **A region always shares one database,** even when it runs several Region server processes.
- **Plan for 5,000–10,000 peak concurrent players per region.**
- **Region server scaling:**
  - **Stateless Region server processes.** Durable state (parties, friends, guilds, mail, instance placement, and game state such as Project Anima's market) lives in PostgreSQL; processes keep only caches, and presence is rebuilt from the character ownership rows. Any process can serve any client.
  - **Singleton jobs run on whichever Region process holds a PostgreSQL advisory lock:** placing instances, cleaning up dead instances, refunding entry-cost escrow after a crash (Anima, in Project Anima). If that process dies, another takes the lock. Placement is an atomic database write, so nothing is placed twice.
  - **Valkey** (the BSD-licensed Redis fork) is the SignalR backplane for multi-process regions. A single-process region (e.g. a private server) needs no backplane.
  - **One PostgreSQL primary per region;** read replicas are the first step if reads become a bottleneck.
- **Shared data library:** game servers and the Region server use one C# model/data library and talk to PostgreSQL directly, as Albion, EVE and AzerothCore do. Trade-offs accepted: every process holds database credentials, and schema migrations must be coordinated across processes.

### Handoff and instances

- **Seamless handoff: pre-warm + delta.** When the client starts listening across a border, the new game server preloads the character from the database (the same path as login). At the crossing only the live-state delta moves, directly from the old game server to the new one. Items and currency never travel in a handoff because they're already persisted.
- **Character ownership is a versioned database row.** At handoff the old game server freezes the character, then the new one claims it atomically (`UPDATE … SET owner = new, version = v + 1 WHERE version = v`). Every character write carries the version its server holds, so late writes from the old server fail (fencing). The Region server subscribes to ownership changes for presence (placement, parties, chat routing) but isn't on the handoff path.
- **Carried across a border:** cooldowns, and buffs and debuffs with their remaining duration. **Not carried:** monster aggro. **A skill in progress delays the handoff** until it finishes (invisible, since handoff already happens a few metres past the line).
- **Teleports reuse the border handoff.** When the cast starts, the destination server preloads the character and the client starts streaming the destination. When the cast finishes, ownership is claimed and the live-state delta moves, behind the teleport scene. The scene lasts a fixed time even if loading finishes early. Cancelling the cast drops the destination server's preload. A teleport carries the same state as a border crossing.
- **Dungeon loading rooms are part of the instance.** The instance is created when the first party member reaches the dungeon entrance; the entrance acts as a small border into it, and the client streams the dungeon while the party gathers in the room. If everyone leaves the loading room before the dungeon starts, the instance is disposed of quickly.
- **Instances are very cheap to create and dispose of:** not a heavy concept like a Unity scene. One game server runs many instances.
- **Channels and dungeon or house instances are one concept in code:** an instance is a running copy of a zone file on a game server. Channel, dungeon instance and house instance are policies on top of it: who may enter, placement, borders, lifetime.

### Transport and libraries

- **Netcode is designed for WebSocket as the worst case.** WebTransport would need a WebSocket fallback anyway (some networks block UDP), so the game must play acceptably over TCP; better transports only make hiccups rarer.
- **All clients start on WebSocket,** desktop included: one code path to build and load-test. WebTransport for web is a later goal (see Engine and approach) and UDP for desktop a possible later upgrade. Both, and supporting several transports behind one interface, are out of scope for now.
- **Connections:** clients hold two connections: a raw WebSocket to their game server (gameplay only) and SignalR to the Region server (chat, parties, guilds, friends, presence), using its groups, reconnection and scale-out backplane. They also make ordinary HTTP requests to the Login server (not a held connection): sign-in, character select, join tickets.
- **MessagePack-CSharp for serialisation** (SignalR's own binary format; IL2CPP-safe via source generators). MemoryPack stays an option for hot paths later.
- **gRPC (ASP.NET Core) for server-to-server calls** (Region ↔ game servers, the handoff delta between game servers).
- **Rest of the stack, to try:** ASP.NET Core (Kestrel) WebSockets on game servers (proven as a web and WebSocket server, e.g. under SignalR, but no shipped real-time game server on it was found), with our own message layer (framing, latest-only send queues, dispatch; LiteEntitySystem's source as a possible reference); NativeWebSocket on the Unity client (web via its built-in `.jslib`; used by Colyseus's Unity SDK), plus the SignalR JavaScript client behind a `.jslib` on web; Npgsql + EF Core (Dapper for hot paths), with EF concurrency tokens on PostgreSQL's `xmin` for the versioned ownership row; ASP.NET Core minimal APIs with Identity for the Login server.

### Load testing

- **The 100-player test is the gate for this stack.** If it fails, that may mean the tech isn't there yet for browser MMOs, or that we should consider switching languages (FishNet + headless Unity is also still the fallback).
- **The test is split so a failure points at its cause:** first a stack-only benchmark (bare Kestrel sending position-sized MessagePack messages to 100–300 WebSocket bots at the planned tick rate, with simulated loss, no game logic), then the full game test (simulation, interest management, our message layer). Stack fails → the stack or language is the problem; only the full test fails → our code is. Watch garbage-collection pauses specifically and tune .NET (server GC, low-latency modes, fewer allocations in the send path) before blaming the language.
- **Bot load tests run over the baseline transport (currently WebSocket) with simulated packet loss,** so TCP stalls show up early.
- **Load-test metrics,** measured from the first run of both stages:
  - *Server:* tick duration (median, worst 1%, ticks over budget), garbage-collection pause count and length, allocation rate, send-queue depth per connection and messages replaced by latest-only queues, bytes in and out per player, CPU and memory.
  - *Bots:* round-trip time, the gap between received updates (where TCP stalls show first), correction count and connection failures.
  - **Tooling:** .NET's built-in metrics (`System.Diagnostics.Metrics`) exported with OpenTelemetry, from servers and bots alike; `dotnet-counters` for quick looks and the standalone .NET Aspire dashboard for test runs. Prometheus and Grafana are for real deployments later.
  - **Pass/fail thresholds are written down before each stage runs** (stage 1 numbers are in `prototype.md`).

### Shared libraries and code layout

- **Five libraries.** Boundaries follow who needs the code, how often it changes, and whether it must compile under Unity.
  - *Protocol:* the network contract. Message types and IDs, MessagePack setup, the transport interface, SignalR hub interfaces for the Region server. Messages are plain data and never reference simulation types. Used by the client, game servers, Region server (hubs), bots and network test tools. Kept separate because changes need client/server version coordination, and bots and tools need it without the rules.
  - *Content:* types and readers for content definitions (items, skills, classes, monsters) and the zone format (chunks, placements, collision volumes). No dependencies beyond MessagePack and `System.Numerics`. Used by everything, including the Unity editor tools and the content build tool.
  - *Simulation:* rules that must match on client and server: frame-data execution, hitboxes and height-zone masks, hit checks, movement and knockback curves, cooldowns and buffs, tick maths. Depends on Content. Used by the client, game servers and bots.
  - *Client:* the client's network logic without Unity types: the session that sends and decodes frames, the server-tick estimate from pings, the interpolation buffer for other entities and the correction blend. Depends on Protocol. Used by the Unity client and the bots, so load tests exercise the real client's code.
  - *Data:* EF Core entities, `DbContext`, migrations, transactional operations. Depends on Content. Server-only (game, Region and Login servers, migrator); never compiled by Unity, so it can use .NET 10.
  - Protocol, Content, Simulation and Client must compile under Unity (.NET Standard 2.1, roughly C# 9, until Unity 7). If the split feels like too much ceremony, Protocol and Content are the easiest pair to merge.
- **Unity consumes Protocol, Content, Simulation and Client as shared source packages:** one source folder that is both a .NET project (targeting `netstandard2.1` and `net10.0` until Unity 7) and a local Unity package with an assembly definition, so Unity compiles the same files. To be checked against Unity 6 in a prototype.
- **Monorepo layout, per-game server programs and per-game Unity projects:** see `proposal.md` (two layers).

### Zones

- **Zones use our own engine-neutral format, not Unity scenes.** Chunked zone files in git are the source of truth: heightmap, placements (asset ID plus transform), collision volumes, spawns, borders and transition rooms. The server loads them directly; the client builds the world at runtime and streams chunks, loading meshes through Addressables by asset ID. Unity is the editor, through custom editor tools that load and save the format. Costs accepted: no built-in Unity scene workflow (terrain tools, prefab placement, baked lighting, lightmaps, occlusion culling) and our own editor tooling to maintain; low-poly art reduces the need for baked lighting.
- **Chunks, optional for small zones:** chunks serve client streaming on the web, LOD granularity, small git diffs and merges, and fast editor loads and incremental builds; the server can load whole zones. Each region has one fixed world grid (e.g. 64 m chunks, size tuned in the prototype), and **open zones own whole chunks**, so borders follow chunk edges and a zone is its set of chunk files. Interest-management cells subdivide chunks; the client uses a floating origin for precision. **Dungeon and house zones use their own local grid** with the same chunk size and format, and are often a single chunk. Heightmaps are optional per chunk (indoor dungeons may be placed meshes only). A dungeon zone can carry an optional **world anchor** placing its static scenery on the region grid, for open-world LODs of outdoor-looking loading rooms.
- **Terrain: heightmap plus a modular rock kit:** a coarse heightmap per chunk, edited with our own simple Unity tools (raise, lower, smooth, flatten, paint) and turned into a flat-shaded low-poly mesh with painted vertex colours; cliffs, arches, overhangs and cave mouths are kit meshes placed and snapped in the editor. **Ground height can have several values** (bridges, overhangs): checks use the heightmap as a fast path and raycast collision meshes where placed meshes are walkable.
- **Lighting: realtime plus vertex colour:** a realtime sun and ambient light, vertex colours and simple shadows; the content build bakes ambient occlusion into vertex colours per chunk. No lightmaps; leaves room for a day/night cycle.
- **Encoding: TOML plus binary maps:** placements, spawns, borders and collision volumes in TOML per chunk (diffable, mergeable, same tooling as content); heightmaps and vertex-colour maps as small binary images per chunk. The content build compiles them to MessagePack.
  - **Heightmaps are raw little-endian uint16 samples behind a small header:** magic `CHGT`, a format version, the grid's width and height in samples, and a float height scale and offset that turn samples into metres. **Sample value 0 is a hole** (no ground: no mesh, no collision, open sky or a chasm), and a terrain triangle exists only where all three of its samples are ground, so edges follow the grid; placed meshes can dress a rim or an island's underside. Floating islands in ShapeLand and chasms or cave mouths in Project Anima both use it. It's the raw `.r16` format terrain tools exchange, plus a header that keeps size and scale with the data and makes a wrong or truncated file fail clearly; converting to or from `.r16` is a small content-tool command if artists ever need it.
- **Distant LODs are automatic and hierarchical:** the content build merges and simplifies each chunk's terrain and scenery into LOD meshes, then merges groups of chunks (2×2, 4×4) into coarser levels for distance. LOD files are delivered by hash, like content. Artists can override a landmark's far LOD by hand. Fog is only a light touch (`art.md`), so LOD changes must hide well on their own.
- **Outdoor zones can show LODs of other zones' static scenery,** including far-away zones that aren't neighbours and outdoor-looking dungeon loading rooms. Only scenery: live entities only sync across borders and official entrances.
- **Collision: per-asset collision meshes plus the heightmap:** each kit piece and prop ships a simple collision mesh authored with it; terrain collision comes from the heightmap; hand-placed volumes cover special cases (invisible walls, water). Feeds movement validation and the navmesh.
- **First editor tools (phases 0–2): a minimal set:** open and save chunks; sculpt the heightmap, paint holes and paint vertex colours; place and snap assets; mark spawns, borders, entrances and transition rooms. Navmesh and LOD previews come later. **ShapeLand needs them too:** the project lead builds its levels and props by hand with these tools; generated content (such as the test island) is only a stand-in for tests.
- **View distance is set in the prototype,** from web performance measurements in phase 2; LOD levels are sized to match.

### Content

- **Content (items, skills, classes, monsters) is authored outside Unity, as files in git:** validated by a schema, reviewed in pull requests. A build step turns them into compact files that the client and servers both load. A small web editor can be added on top later.
- **Content files are TOML 1.1, read and written with Tomlyn,** with snake_case keys (`max_speed = 5`), mapped to the C# types' PascalCase properties. `[section]` headers for top-level structure and multi-line `{ }` inline tables for nested data such as skill frames and loot tables. Tomlyn (BSD-2) targets TOML 1.1 and keeps comments and formatting when rewriting files, which suits a future web editor. Revisit only if Tomlyn shows unforeseen problems in production. To check in a prototype: editor support for TOML 1.1 (e.g. Taplo, "Even Better TOML" in VS Code).
- **Content layout and IDs:**
  - **Strictly one file per entity,** in folders by type with any subfolders. Each file states its own `id`, so files can move freely; the build checks uniqueness.
  - **IDs are namespaced string keys** (`item.wayfarer_coat`, `skill.rising_slash`). Before the first public release they can change freely; after it, **renaming is allowed but discouraged**, e.g. for a badly named or offensive key.
  - **A committed registry (`content/ids.toml`) maps each key to a number, per type.** Comet has one and each game has one; the content build merges Comet's with one game's. The build assigns the next free number to new keys and writes it back; removed entities are marked retired. **Numbers never change and are never reused in production.** A rename changes only the string key; its number stays.
  - **Code refers to content by key, looked up at load** (unknown keys fail loudly). There are no generated constants for now (see Open).
  - **The database, network and compiled content use the numbers.** An item row holds `type_id`; ledger rows, learned flags and class entries likewise. Names, icons and stats come from the loaded content.
  - **The migrator copies the registry into lookup tables** (e.g. `content_item_type(id, key)`) on each deploy, so foreign keys protect `type_id` and raw SQL can show keys.
  - **Drift checks:** CI compares `ids.toml` against `main` (existing numbers still present, surviving keys keep their numbers, numbers only added or retired); the build fails on unresolved references in content and zone files, and on registry entries with no file that aren't marked retired; the migrator refuses to sync if the database knows a number missing from the registry, or a retired number still used by live rows without a data migration; CI checks that numbers are unique within each type on every build, including after merging to `main`; `ids.toml` lists entries in number order, so concurrent additions from two branches show as a merge conflict.
  - **Renames go through a rename command in the content tool** that updates the registry key, the file's `id` and every reference in content and zone files. No rename history is kept. Removing content that players still own needs a data migration.
- **Content schema: the Content library's C# types are the source of truth.** The build validates by loading each TOML file into those types with Tomlyn (wrong types, missing or unknown fields fail), plus C# validation rules for what types can't express (references, ranges, overlapping frames…). A JSON Schema for editors (Taplo, VS Code) is generated from the types, never hand-written.
- **Compiled content is delivered by hash.** The build produces MessagePack content files named by their hash (`content-3f9a1c.bin`). Servers load them at startup; the Login server tells clients the region's hash at login, and clients download the file from the static host if they don't have it, cached permanently by hash. Content-only updates skip the Unity rebuild.
  - **Content builds may be split into several files,** e.g. per expansion. Splits are about packaging, not access: much expansion content must reach players who don't own the expansion (other players' equipment, for example), so ownership is checked by game logic, not by which files a client has.
- **In-game text in content: inline, extracted by the build.** English text (names, descriptions) is written inline in content files; the build extracts it into per-language string tables keyed by number and field, delivered like the content files. Translations live in separate per-language files, and the build can list missing or outdated ones. **Large amounts of text can also be authored in separate files,** e.g. books, lore or long dialogue referenced from a content file; the file format is open. Non-content text (UI, NPC dialogue systems) is a later topic.

### Database

- **Database design: the project lead decides the final schema.** Agent work on tables (`database.md`) is input, not the schema. Depth for this phase: tables and key columns per area, relationships, and which rules the database enforces; no full DDL.
- **One account database per deployment, one database per region.** The account database (Login server) holds accounts, login methods and sessions, staff accounts and roles, sanctions and appeals, the moderator action log and the region list. Each region database (Region and game servers, through the Data library) holds characters and everything they own, social data, the market, the ledger, the chat buffer, reports, instance placement and content lookup tables. No foreign keys between them: characters store `account_id` as a plain number; game servers never touch the account database; the Login server checks sanctions when issuing join tickets, and live sanctions reach the Region server over gRPC. Private servers keep both as two PostgreSQL schemas (`account`, `region`) in one database, with the same code.
- **Database conventions:**
  - **Snowflake-style 64-bit IDs** for database rows, generated by the Data library with a region or process number inside: unique across regions (so character transfers stay possible), time-ordered, as compact as `bigint`. Content keeps its compact registry numbers.
  - **`timestamptz`, always UTC** (Npgsql enforces UTC `DateTime`; time-zone maths such as a region's local reset works).
  - **Columns by default; JSONB only for flexible data read as a whole and rarely queried** (e.g. appearance details, client settings).
  - **snake_case, singular table names** (`character`, `item`, `guild_member`), mapped with EFCore.NamingConventions.
- **Mixed persistence:** item, currency and progression changes are immediate database transactions, acknowledged before the game confirms them to the player. Position, HP and buffs are saved periodically and on handoff.
- **Game-specific data on top of Comet: adding non-default tables and rows in a game must not be painful** (project lead):
  - **One combined EF Core model per game:** Comet ships its entity classes and configurations, the game project registers its own alongside them in one `DbContext`, and the game owns the single migration history. Comet upgrades show up as ordinary migrations in the game's repo; foreign keys and transactions across Comet and game tables just work.
  - **Extra fields on Comet entities go in game-owned 1:1 side tables** keyed by the base row's id (e.g. `anima_character(character_id, anima_capacity, …)`; the prefix is the game's name). Comet tables never change shape per game.
  - **Games add rows to Comet-defined lists (ledger reasons, flag kinds, container kinds, sanction types) through registry keys,** like content: string keys mapped to permanent numbers in a committed registry, with the same lookup tables and drift checks as `ids.toml`.
- **Inventory and Storage share most of their systems.** In Project Anima, crystals reference gear and outfit sets whose items live in Storage (see `items.md`).

### Moderation and operations

- **Moderation roles:** three groups: the dev team, volunteer moderators with limited powers (as with RuneScape's Player Moderators, Wurm Online, Ryzom and Tibia's Tutors), and private-server owners, who are admins of their own server. Roles such as moderator, GM and admin share the same tools; exact names and powers are open. **Guarding against abuse by volunteer moderators is a requirement** (project lead).
- **Admin tools: a web admin panel plus in-game GM powers.** The panel handles most work (report queue, chat review, sanctions, character and item lookups, restores); GM powers cover what must happen in the world (invisibility, teleporting to a player, observing, removing a stuck character), handled by game servers for accounts with the GM role. Both use the same permission checks and write to one moderator action log.
- **The admin panel is part of the Login server:** pages and endpoints behind an admin role, reading data through the shared Data library and sending live actions (mutes, disconnects) to the Region server over gRPC. Private servers get it automatically. Can be split out later if it grows.
- **Reports:** a report is a short form (category plus optional free text), and the server attaches the evidence:
  - who reported whom, when, and where (region, zone, channel or instance, position);
  - recent chat involving the reported player that the reporter could see (nearby, party, and private chat between the two), from the chat buffer below;
  - the reported player's recent automatic flags (such as movement violations);
  - for trade or scam categories, recent item and currency movements between the two players, from the ledger.
  Player screenshots or clips are a maybe for later: they mean hosting uploads.
- **Chat: as little logging as possible** (project lead, for privacy). There is no general chat log:
  - All chat types, private messages included, pass through a **rolling buffer of up to 24 hours**.
  - **A report saves the relevant chat** from the buffer. **When the report is resolved, the saved chat is deleted**, unless a moderator flags it to keep as evidence. **Report history** (who, what, when, outcome) is kept even after its chat is gone.
  - **Access:** volunteer moderators only see chat attached to reports; staff (GM or admin) can also search the buffer, and every search goes into the moderator action log.
  - **Storage:** a PostgreSQL table partitioned by hour, written by the Region server as it routes chat. A Region server singleton job creates upcoming partitions and drops those older than 24 hours (dropping a partition avoids the dead rows that bulk deletes leave).
  - With EU players, GDPR applies: state the retention in the privacy policy.
- **Item and currency ledger:** an append-only ledger row is written in the same database transaction as each movement (what, from, to, reason, actor, where, when), so it can't disagree with reality.
  - **Scope:** creation, destruction and every change of owner (trade, market, mail, drops and pickups, guild storage, GM actions). Moves within one owner's inventory, bags or Storage are not logged. High-volume stackable resources are summarised (e.g. one row per gathering session).
  - **Retention:** monthly partitions in PostgreSQL for about 6 months, then exported to compressed archive files and dropped; economy summaries are kept forever. Archiving can be disabled (including on official servers) and old archives removed by hand. Player-facing details such as "crafted by" live on the item, not in the ledger.
  - **Uses beyond moderation:** economy balancing (currency sources and sinks, real drop and craft rates), player support and precise fixes after bugs, debugging duplication, and player-facing features (guild storage logs, personal trade and market history).
  - **Cost:** one extra insert per movement inside an existing transaction; storage is the real cost (rough guess 1.5–2 GB a day for a full 10,000-player region; to be measured).
- **Sanctions:**
  - **A standard ladder:** warning (a recorded note the player sees), mute (chat only, timed), trade restriction (no trading, market or mail, timed), suspension (no login, timed) and permanent ban. Each has a reason and an expiry the player can see. **Permanent bans stay permanent unless explicitly lifted** (project lead).
  - **Applied to the account,** across all its characters and regions; records still show which character was involved.
  - **Tiered powers,** configurable per server: volunteer moderators can warn and mute for up to 48 hours (RuneScape's Player Moderator limit); GMs can apply all timed sanctions; admins can issue permanent bans and lift them.
  - **Appeals** go through the Login server's web account page, which shows active sanctions and stays reachable when banned. One appeal per sanction, reviewed by a different staff member than the one who issued it.
- **Rollbacks:**
  - **Targeted restores are the normal tool:** GMs restore or remove specific items and currency (e.g. duplicates traced through the ledger). Each change is a new ledger row with reason "GM action" plus a moderator action log entry; history is never edited.
  - **Region-wide rollback is an admin-only last resort** (catastrophic duplication or data corruption): the region goes offline and its database is restored to a point in time from continuous PostgreSQL backups (WAL archiving; tool such as pgBackRest or WAL-G, chosen later).
- **Guarding against moderator abuse,** on top of the action log, tiered powers, report-only chat access for volunteers and appeals reviewed by someone else:
  - **Conflict of interest is blocked:** moderators can't act on their own accounts, friends or guildmates (including GM restores); the report passes to someone else.
  - **Volunteers see only what's needed:** character names and report evidence, never emails, IP addresses or payment details.
  - **Oversight:** staff review volunteer actions, with per-moderator stats in the panel (actions taken, how often appeals overturn them); powers can be revoked instantly and staff can undo any volunteer sanction.
  - **Moderators are anonymous to players** ("a moderator"); staff always see who acted.
  - **Two-person approval** for the biggest actions (large restores, lifting a permanent ban, region rollback) is supported and configurable: on by default for official servers, can be turned off for small teams and private servers.
  - **Staff powers live on separate staff accounts,** not on the accounts staff play on. GM and admin characters can use custom appearances and are always clearly marked as GMs (project lead).
- **Deploys use maintenance windows; rolling updates are never planned for** (project lead: much easier for a small team). A region updates all at once (migrate, deploy, restart), so processes and clients never run mixed versions; handoff and the protocol don't need to work across versions. Web clients pick up a new build at login.
- **Client-side mod support is wanted, but far down the line.** Scope and approach not yet discussed (see Open).

## Considering

- **Server navmesh with DotRecast (to verify in a prototype):** MIT C# port of Recast/Detour (active, 2026.1.3). Builds navmeshes from the zone files on both sides and handles server pathfinding and crowds, with streaming for large worlds.
- **Shared-library details:** shared code uses `System.Numerics` types and converts to Unity types at the edges; no exact determinism or fixed-point maths (client predicts, server corrects); bots are plain .NET console apps using Simulation, Protocol and .NET's WebSocket client; a migrator tool runs before each deploy (as FishMMO does).
- **Lightweight instances:** a game server loads each dungeon's zone data and navmesh once, read-only, and shares it between all its instances of that dungeon. An instance is then only its live state (entities, monsters, doors, instance items) plus a tick slot, so creating one is an allocation and disposing of it is dropping the object. Entry costs (Anima, in Project Anima) move into escrow when the dungeon starts, not on entering the loading room, so a disposed room-only instance needs no refund.
- **A single Region process per region** (project lead): still open to it, e.g. if one process comfortably handles 5,000–10,000 players. The stateless design keeps that possible: one process is just the smallest deployment.
- **SpacetimeDB** remains under consideration as an alternative to the custom .NET stack, which is the current planning focus. The project lead is slightly biased against it because of the self-hosting requirement; verify current licence terms before weighing it. A systems-language backend written with another developer is an unlikely third option.
- **Self-hosting package:** a private server runs all three parts in one process, with Region server calls becoming plain method calls, plus PostgreSQL and a reverse proxy that handles TLS automatically (e.g. Caddy), shipped as one docker-compose file.
- **Data model suggestions (not agreed; `database.md` has the drafts):**
  - **Items:** every item has an owner (a character, or account Storage) and a location: inventory slot, Storage, inside a bag, on the ground (until despawn), or placed in a house, plus a slot index; in Project Anima, soulbound items return to their owner instead of despawning. Gear and outfit sets are reference lists, not locations (see `items.md`). Container rules are data. Moving, trading and equipping are all location changes in one database transaction, so an item can never be in two places.
  - **Class entries (Project Anima):** current XP, highest level, when unlocked. A Class Crystal is an item with a soulbound owner, a reference to one of the owner's class entries, a bought/granted flag, and references to a loadout, gear set and outfit set. Promotion is one transaction: check requirement, move XP and create the entry if new, convert the crystal, charge resources.
  - **Loadouts:** one slot list per loadout; each slot has a kind (Skill or Rune), a category, a binding if it's a Skill slot, and a locked flag.
  - **Mastery:** gear and class Runes share one mastery system; progress is stored per Rune on the item (gear, which can have multiple Runes) or on the class entry (class).
  - **Finite rune stones (Project Anima):** the first copy a character receives is marked soulbound on acquisition; later copies are ordinary items.
  - **Entry-cost escrow for locked content** (Project Anima charges Anima): entry moves the cost into a hold tied to the instance; a clear finalises it, anything else refunds it. On startup, holds from crashed instances are refunded.
  - **Per-player gathering nodes:** per-player records of which nodes have been used.
  - **Temporary structures** (campfires, pitched tents) exist only on the channel; nothing is persisted.

## Rejected

- **Dropping web.** Desktop-first with UDP was weighed (better netcode, less browser complexity, engine freedom) and rejected: in-browser play, in the spirit of RuneScape's Java-applet days, is the project's main appeal.
- **A Region server that owns the database** (a central data service): replaced by the shared data library.
- **JSON content files:** TOML 1.1 instead.
- **Snowflake numbers for content IDs:** they would only remove rare merge conflicts, at a permanent bandwidth cost in hot messages.
- **Voxel terrain:** months of tooling; the heightmap and rock kit cover it. Editor-generated procedural shapes remain a fallback if the kit feels limiting. Worth re-evaluating if human level design shows a real need for it (project lead).
- **Unity Terrain converted to our format:** built for high-res terrain, not low-poly.
- **Baked lightmaps:** heavy tooling, and rules out a day/night cycle.
- **Rolling updates.**
- **Free-form border polygons and per-zone coordinates:** zones own whole chunks on one world grid instead.
- **WebTransport on desktop:** once there's an abstraction over several transports, desktop would use plain UDP; WebTransport is only for web. The exception: if WebTransport becomes reliable enough to drop the WebSocket fallback, one transport everywhere could make sense (unlikely; likelier if WebSocket over TCP doesn't meet our needs) (project lead).

## Open

- **Collision against meshes, with or without a physics engine** (decide with the zone format): gameplay movement is our own kinematic code in `Comet.Simulation` (heightmap and boxes in phase 0), shared by client and server. Per-asset collision meshes and raycasts need real collision queries on both sides: our own simple mesh raycasts, or a pure C# library such as BepuPhysics v2 (Apache-2.0). The project lead leans towards **no physics engine**, except later in Project Anima for client-side-only props and gear (cosmetic, never gameplay).
- **Generated content ID constants (revisit later):** skipped for now; code looks content up by key. If adopted, likely only for entries marked in their TOML. Generated files aren't checked into git without a very good reason (project lead).
- **Client-side mods** (far down the line, not yet discussed): what mods may change (UI, visuals and shaders, sounds, new behaviour); how they load on web and IL2CPP builds, which can't load new compiled C# at runtime (so likely data, assets or a scripting language); fairness and cheating limits, and how the server stays authoritative; whether private servers can push server-side content to clients.
- **Volunteer moderation and the law:** lesson from Ultima Online's Counselors and EverQuest's Guides (scaled back after the AOL volunteer lawsuit, settled 2010): keep volunteer powers limited and check labour law before it becomes real.
- **Ops tooling, deferred items** (one line each in the proposal later): desktop distribution (own patcher or a storefront), a community bot (e.g. Discord), and dashboard and alerting detail (Prometheus and Grafana for real deployments). Server discovery is probably the Login server's region list.
- **The game-server message layer is ours to write:** no battle-tested library does game-state replication for a pure C# server over WebSocket (its design is in `netcode.md`); FishNet + headless Unity remains the fallback if that proves too hard.
- **WebTransport upgrade paths (for later):** (1) Kestrel's experimental WebTransport using independent streams instead of datagrams (pure C#; preview feature); (2) our own WebTransport handshake on MsQuic via its C# interop (gets datagrams; protocol work); (3) wrap libwtf (MIT, C, on MsQuic); (4) wait for the open `System.Net.Quic` datagram API proposal. A Go/Rust proxy is ruled out by the C#-only rule. WebTransport's `serverCertificateHashes` might let private servers skip a domain and certificate for game traffic (unverified).
- **FishMMO** (https://www.fishmmo.com/): an MIT-licensed MMO template on FishNet + Unity. Not a base (no shipped games, not very actively developed) but a good reference codebase; see Research. FishNet itself is the fallback if we go back to headless Unity.

## Research

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
- **Unity's web networking lags (checked again 2026-10-08):** Unity Transport supports only WebSocket on web, as a client only, and only to other Unity Transport peers; its 2026 releases (2.7, 6.6) added nothing for WebTransport, and Unity's web networking manual lists WebTransport only as further reading. Netcode for Entities doesn't support web. WebTransport from Unity likely means our own `.jslib` bridge; our browser transport in `comet/unity` is already custom, so it would sit behind the same interface.
- **WebGPU is supported in Unity 6.6** (out of experimental, September 2026; we're on 6000.6). WebGL 2 stays the default: WebGPU is opted into in Player Settings by putting it first in the graphics API list, with automatic fallback to WebGL 2, and Graphics Device Filtering can force the fallback for problem browsers, GPUs or drivers. It works with URP (Forward+, Deferred, SRP Batcher, BatchRendererGroup) and adds compute shaders, GPU skinning, VFX Graph and GPU Resident Drawer. Needs HTTPS. Unity calls performance against WebGL 2 context-dependent (WebGL can still win on CPU cost with many draw calls). Browsers: Chrome, Edge and Safari 26 ship it; Firefox only on Windows and Apple Silicon Macs (Linux is Nightly only, expected in 2026; Android off), so the headless-Firefox-on-Linux web test would fall back to WebGL 2. Not found: Unity 7's WebGPU status, and any known 6.6 bug matching our web shadow problem (most web shadow reports come down to the web build's lower quality level).
- **Unity is moving to CoreCLR:** Unity 7 (which replaced the planned 6.8; a beta is due soon, per the project lead on 2026-10-06) drops Mono and targets .NET 10; CoreCLR dedicated-server builds are experimental in 6.7. This would let the client and a pure C# server share modern C#. How this applies to web builds (IL2CPP → WebAssembly) is unverified. The first alpha (7000.0.0a7, October 2026) still targets .NET Standard and C# 9 (project lead).
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
