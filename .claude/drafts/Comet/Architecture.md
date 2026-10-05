# Architecture

## Services

Comet has three kinds of server, deliberately few. New tooling goes into an existing service unless load or security forces a split.

| Service | Job |
| --- | --- |
| **Login server** | Public web front door: the game's website and web client page, accounts, sign-in and sessions, character select, the admin panel, and signed tickets that let a client join a game server. It never relays game traffic. |
| **Region server** | One per region (it can run as several processes). Coordinates everything that spans instances: placing channels and dungeons, parties, guilds, friends, chat routing and mail, plus game modules such as Project Anima's market. |
| **Game servers** | Run the live simulation for one or more instances (channels, dungeon instances, house instances). |

Alongside them sit PostgreSQL and a static host (or CDN) for the large files: the web client build, assets and content downloads. The website itself isn't static, since it keeps state such as being logged in, so the Login server serves it.

A **region** is a separate copy of the world, like a classic MMO server: its own characters, economy and guilds. We plan for 5,000–10,000 peak players per region.

## How a client connects

A client holds two connections: a raw WebSocket to its current game server for gameplay, and a SignalR connection to the Region server for chat, parties, guilds and presence. Moving between zones or instances hands the character from one game server to another without a loading screen (see [World and Zones](World%20and%20Zones.md)).

## Data

- **One account database per deployment** (accounts, logins, staff, sanctions, the moderator log) and **one database per region** (characters and everything they own, social data, the ledger, the chat buffer, reports, instance placement).
- **No central data service.** Game servers and the Region server share one C# data library and talk to PostgreSQL directly, as several shipped MMOs do.
- **Mixed persistence.** Item, currency and progression changes are saved in a database transaction before the game confirms them. Position, health and buffs are saved periodically and on handoff.
- **Character ownership is a versioned row.** When a character moves between game servers, the new server claims it atomically and late writes from the old one are rejected. This is what makes handoff safe.
- **Conventions:** 64-bit time-ordered IDs, UTC timestamps, plain columns by default and JSONB only for data read as a whole.

Private servers keep both databases as two schemas in one PostgreSQL database, with the same code.

## Shared libraries

Code that must match on client and server lives in shared libraries, compiled both by .NET and by Unity:

| Library | Contains | Used by |
| --- | --- | --- |
| **Protocol** | The network contract: message types, serialisation, the transport interface | Client, servers, bots, test tools |
| **Content** | Content definitions and the zone format | Everything, including editor tools |
| **Simulation** | Rules both sides run: frame data, hitboxes, hit checks, movement curves, cooldowns | Client, game servers, bots |
| **Data** | Database entities, migrations, transactions | Servers only |

## The monorepo

Everything lives in one repository:

- **Comet** as libraries, server code and a Unity package (networking, replication, prediction, zone streaming, input).
- **Each game as its own Unity project** with its own UI, rendering and art.
- **Each game's own server programs** (for example `Anima.GameServer`, `ShapeLand.GameServer`) built from Comet's libraries plus the game's modules. There are no generic servers that load games at runtime.
- **Content per game**, merged with Comet's own content by the build.
- **Bots:** a bot framework in Comet, with behaviours written per game. Bots are headless clients used to load-test with hundreds of simulated players.

## Games and the database

Adding game-specific data must not be painful. Each game has one combined database model: Comet's tables plus its own, with a single migration history. Extra fields on Comet's tables go in game-owned side tables (for example `anima_character`), so Comet's tables never change shape per game. Lists that Comet defines (ledger reasons, flag kinds) take extra entries through the same ID registry as content.

## Technology

Unity for the client, with web export as a main goal. Plain .NET (ASP.NET Core) for every server, PostgreSQL, MessagePack for serialisation, SignalR for social traffic, gRPC between servers, and Valkey only for multi-process regions. No headless Unity server and no Unity networking package; FishNet with headless Unity servers is the fallback if our own netcode proves too hard.

## Open questions

- Whether a region needs more than one Region server process, or one comfortably handles a full region.
- The final database schema (draft tables exist; the project lead decides).
