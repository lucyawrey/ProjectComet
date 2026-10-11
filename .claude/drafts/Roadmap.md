# Roadmap

## Phases

The roadmap has phases with goals and exit criteria, not dates. The first three build and prove Comet using ShapeLand; Anima's own systems start at the closed alpha. The biggest risk, the seamless world, is proven before any serious content production.

Phase 0 came before the proposal was finished, and it's done: ShapeLand is playable in the browser at https://shapeland.lucyawrey.com, with no download. It runs on a single game server, with no accounts, Login server or Region server; those come in phase 1. The project lead built it, with agents writing much of the code from agreed plans. What it showed is in the [Prototype Report](Prototype%20Report.md).

| Phase | Goal | Done when |
| --- | --- | --- |
| **0. Prototypes** (done) | In order: the stack benchmark; shared code compiling in Unity, with editor support for content files; ShapeLand sliding, jumping and chatting, built for web and desktop, under simulated latency | The stack benchmark and ShapeLand's network criteria pass, both written down beforehand. Both passed |
| **1. Vertical slice** | First level editing (zone files and simple editor tools, to build bigger areas), interest management and the full load test; then ShapeLand in one zone with combat, loot and levels; 100+ bots; simulated latency from day one. Comparing texture filtering runs alongside | Movement and combat feel responsive under simulated latency, and performance meets thresholds |
| **2. Seamless-world proof** | Two or more zones with border handoff, channels, a dungeon instance and a teleport | Crossing borders is invisible under simulated latency |
| **3. Closed alpha** | Anima's early part playable: accounts, persistence, classes, items, crafting, basic moderation tools, private-server packaging | Stable with real players, and they come back |
| **4. Open beta** | The early part complete, the late part in progress, membership and bonds, operations tooling | Scale and the economy hold up with a real population |
| **5. Launch** | Early part free, late part for members | |
| **6. Expansions** | New parts; free players move up a part | |

## The load test

The netcode stack is the gate for the whole technical plan, so it's tested first, in two stages: a bare stack benchmark (a server sending position-sized messages to 100–300 bots over WebSocket, with packet loss, no game logic), then the full game. A failure in the first stage points at the stack or language; a failure only in the second points at our code. If the stack fails, the fallback is FishNet with headless Unity servers. The first stage passed in phase 0; the full game test opens phase 1.

The stack benchmark runs 300 bots at 30 Hz against one server core, twice: once with 1% packet loss, and once with about 80 ms of added latency as well. Network conditions are simulated in Docker locally; official runs put the server on a cloud machine and the bots on a separate one. Passing means steady tick times, short garbage-collection pauses, the server using under half a core, no long gaps between updates for the bots, and under 10 KB/s per player. Every check passed, locally and on AWS, except one: a one-off garbage-collection pause of 10–20 ms on AWS as players join, which never caused a missed tick.

## Team roles

The proposal lists roles, not people; in a small team one person covers several.

| Area | Roles |
| --- | --- |
| Programming | Unity client and gameplay; server and networking; tools (zone editor, content build, admin panel) |
| Art | Low-poly character artist; environment artist; animator for the shared skeleton |
| Design | Systems and economy; combat and boss encounters; UI and UX |
| Writing | Narrative and quests (human-written only) |
| Audio | Composer and sound designer (later) |
| Community | Community manager and moderation lead |
| QA | Testing and playtesting |
| Operations | Deployments, databases, monitoring |

## Hosting costs

The proposal covers hosting only; development budget is for the team to decide. Costs, largest first: game servers, Region server processes, PostgreSQL with backups, Valkey for larger regions, and download bandwidth (every new browser player downloads the client and content). Numbers follow the full load test at the start of phase 1, once we know how many players fit on a CPU core.

## Risks

| Risk | Why | Mitigation |
| --- | --- | --- |
| **Netcode** | Action combat in a seamless world, over WebSocket, in a browser | The two-stage load test first; event-based sync, interest management and priorities; a known fallback |
| **The seamless world** | Handoff, streaming and distant scenery on the web | Proven in phase 2 with ShapeLand before content production |
| **Moderation** | Chat, roleplay and young players | Built-in tools, volunteer safeguards, privacy-friendly chat buffer |
| **Economy integrity** | Duplication, bots, real-money trading | The item ledger, server authority, free-account limits, bonds |
| **Too many players** | More demand than regions can hold | Channels, region scaling, more regions |

## Open questions

- Pass/fail thresholds for the full load test and later phases.
- Hosting cost numbers, after the full load test.
