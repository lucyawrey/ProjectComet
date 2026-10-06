# Prototype

**Layer: base (Comet),** with ShapeLand as its game.

Agent notes on the early prototype of Comet and ShapeLand. The roadmap phases are in `proposal.md`; the load test design is in `backend.md`.

## Decided

- **Prototype early,** before the human-readable proposal is done. Building has started with the stack benchmark.
- **Scope: phase 0 only.**
  - The two-stage load test: a bare stack benchmark, then the full game test (`backend.md`, load testing).
  - Shared source packages (Protocol, Content, Simulation) compiling in Unity.
  - Editor support for TOML 1.1 content files.
  - The texture filtering comparison: crisp against soft (`art.md`).
  - ShapeLand's phase 0: shapes sliding around and chatting.
- **Built by the project lead with agents:** the project lead drives and reviews; agents write much of the code from agreed plans.
- **Order of work:**
  1. The bare stack benchmark (Kestrel and bots, no Unity), the biggest risk.
  2. Shared packages compiling in Unity, and TOML support.
  3. ShapeLand sliding and chat.
  4. The full game load test on top of ShapeLand.
  - The texture filtering comparison can happen any time, in parallel.
- **The prototype's first milestone: the stack benchmark passes** its thresholds, written down before the run. A failure tells us early that the stack or language is the problem.
- **ShapeLand phase 0 runs on a game server only:** players connect straight to one game server with a name, no accounts; chat goes over the game connection. Login, Region and the database come in phase 1.
- **The web build is tested early:** ShapeLand is built for web as soon as it slides, and the full load test includes real web clients alongside the bots.
- **Repository layout: top-level folders per layer,** one solution:

  ```
  ProjectComet/
    (one solution file)
    comet/
      src/       Protocol, Content, Simulation, Data, Bots, server hosting
      unity/     the Comet Unity package
      content/
    shapeland/
      server/  bots/  content/
      unity/     its own Unity project
    tools/       content build, benchmarks
    docs/  .claude/  (as now)
  ```

- **Toolchain:** .NET 10 (LTS) for servers and tools; **the Unity 7 beta as soon as it's available** (Unity 7 replaced the planned 6.8 and brings CoreCLR and .NET 10; CoreCLR is a priority and this is a prototype, so a beta is fine). Until then, the newest Unity 6 release. Web builds on the beta still need confirming (`backend.md`, research).
- **Stack benchmark thresholds** (starting numbers; 300 bots, 30 Hz, 1% simulated packet loss, one server core):
  - *Server:* tick time median under 5 ms and worst 1% under 20 ms; under 0.1% of ticks over the 33 ms budget; worst garbage-collection pause under 10 ms; CPU under 50% of one core.
  - *Bots:* worst 1% gap between updates under 150 ms; median round trip under 10 ms on a local run; no connection failures.
  - *Bandwidth:* under 10 KB/s down per bot.
- **Where the load test runs:** on Linux machines. Iterate locally; official runs use a cloud VM for the server and a separate machine for the bots, so bots don't take the server's CPU and the network is real.

### Stack benchmark

- **Pieces,** kept in `tools/` as a regression bench, rerun after big stack changes (.NET or Unity upgrades); its message layer becomes Comet's:
  - *Bench server:* bare Kestrel WebSocket endpoint, 30 Hz tick loop, one fake moving entity per bot, our own latest-only send queues, MessagePack. No game logic.
  - *Bench bots:* one .NET console app with 300 WebSocket connections; each sends position reports at about 15 Hz and records round trips, gaps between updates and failures.
  - *Report:* server and bots write JSON metrics; a script checks them against the thresholds and prints pass or fail.
- **Budgeted batches:** each tick, each bot gets one message with about 10 entity updates (about 300 bytes), chosen round-robin from all entities, mirroring the per-client budget in `netcode.md`.
- **The bench runs in Docker,** with network conditions from Linux netem (`tc qdisc … netem`) applied to the bots' container link only, so other local traffic is unaffected. The same setup runs on the cloud bot machine.
- **Two runs:**
  - *Clean:* 1% loss only; checks every threshold, including the local round-trip median.
  - *Impaired:* about 80 ms ± 20 ms latency plus 1% loss; checks everything except the round-trip median.
- **10-minute runs after a 1-minute warmup.**
- **The message layer lives in Comet's libraries from the start:** framing, message IDs and messages in `Comet.Protocol`; connections, send queues and the tick loop in `Comet.Server`. `tools/StackBench/` only wires them up, so the benchmark tests the real code.
- **Frame encoding:** the server tick as a fixed 4-byte number once per frame; each message's ID and length as varints (usually 1 byte each), then its MessagePack payload. Client frames use the same layout, with the client's server-tick estimate.
- **MessagePack formatters are source-generated** (MessagePack 3), with no run-time code generation.
- **Pings are answered immediately,** from the receive loop as their own small frame, not on the next tick, so round trips measure the network rather than the tick phase.
- **Backpressure: at most one tick frame in flight per connection.** If it's still sending at the next tick, that flush is skipped and counted; state stays in the latest-only queue and events wait.
- **The tick loop sleeps, then spins:** a dedicated thread sleeps until about 1 ms before each deadline, then spins, so ticks start on time (costs about 3% of a core).
- **Bots' entities take their position from the bots' reports** (seeded random walks, no validation), so the inbound path is exercised too.
- **GC pauses are timed from runtime events** (`GCSuspendEEBegin` to `GCRestartEEEnd`) by an in-process listener.
- **Percentiles come from HdrHistogram.**
- **Network conditions apply both ways on the bots container:** egress, plus ingress through an ifb device (netem belongs on the receiver's ingress for realistic TCP). Loss 1% each way; the impaired run adds 40 ms ± 10 ms each way (about 80 ms round trip). A pfifo child qdisc stops jitter from reordering packets.
- **In Docker, the server is pinned to one CPU** (cpuset, with `DOTNET_PROCESSOR_COUNT=1`) rather than given a CPU quota, whose throttling would freeze it mid-tick; the bots get every CPU outside that physical core. The host loads the traffic-shaping modules at boot (`/etc/modules-load.d/stackbench.conf`).

### Stack benchmark results

- **Local Docker runs pass every threshold** (2026-10-06; AMD Ryzen 7 3700X, server pinned to one core, 300 bots, full 10-minute windows). The official runs on a cloud VM and a separate bot machine are still to do.

  | Check | Limit | Clean | Impaired |
  | --- | --- | --- | --- |
  | Tick work, median | 5 ms | 0.32 ms | 0.36 ms |
  | Tick work, worst 1% | 20 ms | 2.2 ms | 2.1 ms |
  | Ticks over budget | 0.1% | 0 | 0 |
  | Worst GC pause | 10 ms | 5.7 ms | 4.2 ms |
  | Server CPU | 0.5 cores | 0.21 | 0.44 |
  | Gap between updates, worst 1% | 150 ms | 39 ms | 127 ms |
  | Round trip, median | 10 ms (clean only) | 0.08 ms | 82 ms |
  | Connection failures | 0 | 0 | 0 |
  | Bytes down per bot | 10 KB/s | 7.6 KB/s | 7.6 KB/s |

- **Server CPU is the tightest margin:** it doubles under the impaired network (0.21 to 0.45 cores) with the same traffic. Most of the increase is kernel time (0.11 to 0.29 cores; the server's own code goes from 0.10 to 0.16).
- **Likely cause (unconfirmed): netem's own work, billed to the server.** On one host, the server's send call carries packets through the bridge into the bots' ingress emulator, and delayed packets cost netem a time-sorted queue and timers. Part may be real TCP cost at an 80 ms round trip. The cloud runs settle it: netem runs only on the bot machine there, so comparing the server's kernel time between profiles shows what a real server pays.

### Code layout and build

- **One `src/` per layer, each project a self-contained folder named like the project** (`comet/src/Comet.Protocol/Comet.Protocol.csproj`; folder, project, assembly and namespace match). Shared packages are both a .NET project and a Unity package, so the project folder is the package root. Tests go in `comet/tests/`.
- **All build output goes to `artifacts/`** at the root (.NET's artifacts output), so Unity never imports `bin/` or `obj/` from a shared package and the vault stays clean.
- **One `ProjectComet.slnx` solution,** central package versions (`Directory.Packages.props`), shared defaults in `Directory.Build.props` (nullable on, warnings as errors), C# 9 for the Unity-shared libraries.
- **Projects start from official templates** (`dotnet new`, Unity Hub), then get edited.
- **Tests use xUnit v3.**
- **Code folders are hidden in Obsidian.**

### Shared packages and content (step 2)

- **Four libraries from the start** (Protocol, Content, Simulation, Data; `backend.md`). Simulation and Data stay nearly empty in phase 0.
- **The shared packages must compile in the Unity editor and in IL2CPP desktop and web builds,** where MessagePack's ahead-of-time needs (generated formatters) and .NET Standard 2.1 limits show up.
- **Content pipeline, core path only:** TOML files read by Tomlyn into Content types, validated, compiled into a MessagePack file that the server and client load; a minimal ID registry with generated constants; a JSON Schema generated from the Content types so VS Code (Even Better TOML) autocompletes and flags errors while editing. Hash delivery, text extraction, CI drift checks and the rename command wait for phase 1.
- **TOML editor support is checked in VS Code** only.

### Texture filtering comparison

- **Two scenes:** synthetic test textures (checkerboards, gradients on simple shapes) for the technical check, and a small art scene from CC0 pieces (Kenney, Quaternius or KayKit) with low-res textures (64–128 px) added: ground, a wall, a tree or two and one character, covering near ground, distant ground and faces.
- **Four variants:** crisp; crisp with mipmaps; soft (bilinear); soft with trilinear and anisotropic filtering. Distance shimmer is crisp textures' main weakness, and mipmaps decide whether it's acceptable.
- **Judged by eye with a live toggle:** a key cycles the variants while walking around, in a desktop and a web build; screenshots and short clips are kept for the proposal. The project lead decides (`art.md`).

### ShapeLand phase 0

- **Joining:** enter a name, pick cube, diamond or pyramid, get a random colour. Names are unique among players currently online; nothing is saved.
- **World:** a flat plane with a few hard-coded blocks first; once TOML support works (step 2), a tiny zone file with a heightmap and props.
- **Actions: slide and jump.** Jumping tests vertical movement and the world collider early; dodge and combat come in phase 1.
- **Movement validation: speed check and snap-back:** distance against max speed with the ~20% tolerance and stall bursts, allowing for the jump arc, plus the correction-and-blend path (`netcode.md`). Collision checks come with zone files.
- **Other players are drawn through a 100 ms interpolation buffer,** tuned under the impaired run (`netcode.md`).
- **Chat: one shared channel:** everyone on the server sees every message; about 200 characters at most, a simple rate limit, nothing logged (`backend.md`). Messages show in a chat box and as a bubble over the shape.
- **Bots in the full load test** mostly wander, sometimes clump into crowds (the worst case for interest management) and send occasional chat lines. Behaviour is seeded, so runs repeat.

## Considering

- Nothing being considered right now.

## Rejected

- **Going past phase 0 in the early prototype** (combat, loot, levels): left for phase 1.
- **A single `src/` tree** for all code: per-layer folders keep Comet and each game clearly apart.
- **The full content pipeline in phase 0** (hash delivery, text extraction, drift checks, rename command): phase 1.
- **Login and Region servers in phase 0:** they come in phase 1, so phase 0 reaches the load test sooner.

## Open

- **Full game load test thresholds** (stage 2), written down before it runs.
