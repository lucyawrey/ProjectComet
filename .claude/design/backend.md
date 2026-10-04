# Backend and data model

Agent notes on backend architecture and data-model ideas. Almost everything here is still open or an agent suggestion.

## Decided

- **Private servers should be easy to self-host**, without a large proprietary dependency.
- **Unity is the engine** (decided 2026-10-04). Web export is a main project goal (classic in-browser MMO play), and Unity is the only engine where that isn't difficult or too limited.
- **No headless Unity server** (2026-10-04).
- **Three-part service map** (2026-10-04, for approach A):
  - **Login server** (renamed from Gateway): public HTTP. Accounts, auth, character select, signed tickets for joining a game server or listening across a border. Clients connect to game servers directly; the Login server doesn't relay game traffic.
  - **Region server** (renamed from Data Center): one per region and the only process that talks to PostgreSQL. Also owns everything that spans shards: shard and instance placement and lifecycle, parties, guilds, friends, chat routing, and the cross-shard economy (market, mail, trades between shards). Split pieces out only if load forces it.
  - **Game servers:** live simulation and in-memory state of one or more shards or dungeon instances. They load a character from the Region server on arrival and send changes back through it.
  - PostgreSQL and a static host for the web client and streamed assets sit alongside these.
- **Seamless handoff: pre-warm + delta** (2026-10-04). When the client starts listening across a border, the new game server preloads the character from the Region server (the same path as login). At the crossing only the live-state delta moves. Items and currency never travel in a handoff because they're already persisted.
- **The Region server owns the character-ownership record** (2026-10-04): the old game server freezes the character, the Region server flips ownership, the new game server activates it. A crash on either side is resolved from that one record.
- **Carried across a border:** cooldowns, and buffs and debuffs with their remaining duration. **Not carried:** monster aggro, and a skill in progress (see Open).
- **Mixed persistence** (2026-10-04): item, currency and progression changes are immediate transactions through the Region server, acknowledged before the game confirms them to the player. Position, HP and buffs are saved periodically and on handoff.
- **Inventory and Storage share most of their systems.** Crystals are not containers; they reference gear and outfit sets whose items live in Storage (see `items.md`).

## Considering

- **Game server:** likely a pure C# server, sharing a simulation library with the Unity client and bot clients (project lead, 2026-10-04).
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
  - **Temporary structures** (campfires, pitched tents) exist only on the shard; nothing is persisted.

## Open

- **Skill in progress at a border:** it doesn't carry over, so does crossing cancel it, or does the handoff wait until it finishes? (Agent suggestion: delay the handoff, since it already happens a few metres past the line.)

## Research (2026-10-04)

Agent web research to sanity-check the architecture. These are findings, not decisions. Primary sources (the Albion talk slides, Unity's manual) were blocked by the session's network policy, so details come from search summaries and should be verified before going into human-readable docs.

### Real MMO backends

- **Albion Online:** Unity client, C# servers on Photon. ~600 world "clusters" (~1 km² each) spread over game servers; separate Login, Chat, World (guilds), Marketplace and other servers. Cassandra for game data, PostgreSQL for accounts and markets. Game servers keep player state in memory, write every change immediately, and read only on login or server change; handoff between servers goes through the database. Lesson from a Cassandra bug that wiped all buildings in alpha: be careful with cutting-edge tech.
- **New World:** a seamless world split into grid squares spread across 7 stateless "hub" servers (non-adjacent squares per hub). State is batch-written to DynamoDB (~800k writes per 30 s).
- **EVE Online:** client → load balancer → Proxy nodes (sessions, public-facing) → SOL nodes (single-threaded simulation) → one large SQL Server database.
- **Guild Wars 2 megaserver:** a weighted load balancer scores every copy of a map by party, guild, language and home World. Matches our decided shard preference.
- **FFXIV (from emulators):** Lobby; a World server for everything global (zoning, linkshells, friends); Zone servers for battle and events.
- **AzerothCore (WoW emulator):** authserver + worldserver + MySQL, shipped as one docker-compose file. A benchmark for easy self-hosting.
- **Ryzom (open source):** many small services per shard; a shard that needed 8 machines in 2004 now runs on one.

### Patterns

- Everyone has three layers: a thin public front door (login/proxy), simulation servers, and a few global services (guilds, chat, friends, market).
- The Western examples (Albion, EVE, AzerothCore) have game servers talk to the database directly. The BigWorld lineage (World of Tanks; open-source KBEngine) instead has a `dbmgr` process that owns all database access, with simulation processes backing up state through it. Our Region server follows the second pattern.
- Two persistence styles: write every change (Albion) or stateless servers with batched writes (New World). Both read state only when a player arrives on a server.
- PostgreSQL is a proven choice for anything transactional or query-heavy.

### Web and Unity

- **WebTransport is Baseline** since Safari 26.4 (March 2026), so unreliable datagrams are available in every major browser.
- **Unity's web networking lags:** Unity Transport supports only WebSocket on web (and only to other Unity Transport peers); Netcode for Entities doesn't support web. WebTransport from Unity likely means our own `.jslib` bridge.
- **Unity is moving to CoreCLR:** Unity 6.8 (later in 2026) drops Mono and targets .NET 10; CoreCLR dedicated-server builds are experimental in 6.7. This would let the client and a pure C# server share modern C#. How this applies to web builds (IL2CPP → WebAssembly) is unverified.
- **SpacetimeDB licence:** BSL 1.1, converting to AGPL v3 with a linking exception in 2031. The Additional Use Grant allows free production use of a single instance if not resold as a database service. A private server (one region) probably fits; the official multi-region setup may not. BitCraft (Unity client) runs its whole backend on it, split into global and region modules. Licence text not yet read directly.
