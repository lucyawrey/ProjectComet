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
      src/       Protocol, Content, ContentBuild, Simulation, Data, Bots, server hosting
      tests/
      unity/     the Comet Unity package
      content/
    shapeland/
      src/       ShapeLand.Shared (content types, messages), ContentBuild, server, bots
      tests/
      content/   shapes/, zones/, ids.toml
      unity/     its own Unity project
    tools/       benchmarks
    docs/  .claude/  (as now)
  ```

- **Toolchain:** .NET 10 (LTS) for servers and tools; **the newest Unity 6 release now,** moving to Unity 7 once it lands (expected December; it replaced the planned 6.8 and brings CoreCLR and .NET 10). Work doesn't wait for it. Web builds on the beta still need confirming (`backend.md`, research).
- **Stack benchmark thresholds** (starting numbers; 300 bots, 30 Hz, 1% simulated packet loss, one server core):
  - *Server:* tick time median under 5 ms and worst 1% under 20 ms; under 0.1% of ticks over the 33 ms budget; worst garbage-collection pause under 10 ms; CPU under 50% of one core.
  - *Bots:* worst 1% gap between updates under 150 ms; median round trip under 10 ms on a local run; no connection failures.
  - *Bandwidth:* under 10 KB/s down per bot.
- **Where the load test runs:** on Linux machines. Iterate locally; official runs use a cloud VM for the server and a separate machine for the bots, so bots don't take the server's CPU and the network is real.
- **Official stack benchmark runs on AWS,** on the new account's free plan (us-east-2): two `c7i-flex.large` VMs in one network, server and bots. `tools/StackBench/aws/run.sh` creates, runs and deletes everything. On this type the server's core is shared with the OS (2 vCPUs are one core's two threads), and "flex" only guarantees part of a core, so results may be noisier than locally; upgrading to the paid plan for `c7a` (one full core per vCPU) is the fallback.
- **A run is invalid if the bots were CPU-bound** (over 80% of their CPUs): the report says so instead of pass or fail.

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
- **Most of that is TCP's real cost at a long round trip, not the emulator:** on AWS, with no netem on the server's machine, the server's kernel time still doubles under the impaired profile (0.13 to 0.26 cores). Players far away cost the server more CPU than nearby ones.
- **AWS runs (2026-10-06; two `c7i-flex.large`, free plan, us-east-2, full 10-minute windows): clean passes, impaired misses one check.**

  | Check | Limit | Clean | Impaired |
  | --- | --- | --- | --- |
  | Tick work, median | 5 ms | 0.53 ms | 0.65 ms |
  | Tick work, worst 1% | 20 ms | 2.9 ms | 3.1 ms |
  | Ticks over budget | 0.1% | 0 | 0 |
  | Worst GC pause | 10 ms | 8.7 ms | **10.04 ms (fail)** |
  | Server CPU | 0.5 cores | 0.28 | 0.47 |
  | Gap between updates, worst 1% | 150 ms | 40 ms | 132 ms |
  | Round trip, median | 10 ms (clean only) | 0.27 ms | 82 ms |
  | Connection failures | 0 | 0 | 0 |
  | Bytes down per bot | 10 KB/s | 7.6 KB/s | 7.6 KB/s |
  | Bots CPU (validity) | 80% | 19% | 40% |

- **Milestone 1 passes, with one known issue** (project lead, 2026-10-06): every check passes except the worst GC pause on AWS, a one-off pause when players join. It never caused a missed tick. Its likely fix is the connection slot pool (Considering), before the stage 2 load test. The GC limit keeps counting pauses from players joining (project lead): a restart or a crowd teleporting in looks similar in real play.
- **The known issue: one or two blocking collections of 10–20 ms on AWS as players join,** copying their connections' state, about 7 MB for 300 (about 22 KB each), out of gen0 and then gen1. Locally the same pause is 6–10 ms: copying costs about 2.8 ms per MB on `c7i-flex.large` against 1–1.6 ms per MB locally. Every other pause is a gen0 under 3 ms with about 80 KB surviving; nothing deep recurs in steady state.
- **What the GC investigation found** (AWS, per-pause log; the network profile makes no difference):
  - *The GC:* workstation GC with background GC on, one heap. `DOTNET_PROCESSOR_COUNT=1` makes the runtime fall back from the Web SDK's server GC, so DATAS is off too.
  - *No setting fixes it* (clean profile, worst pause in the window):

    | Run | Worst pause |
    | --- | --- |
    | Defaults | 8.7–10.1 ms (blocking gen1) |
    | One full collection after startup (a heap settle) | 15.6–18.0 ms (blocking gen2) |
    | `SustainedLowLatency` mode | 10.1 ms (blocking gen1); with the settle 15.2 ms |
    | Bots joining over 50 s instead of 10 s | 13.5 ms (blocking gen0, then gen1); with the settle 19.9 ms |

  - *A heap settle makes it worse:* a full collection after startup leaves gen2 under 1 MB, so the joins overflow gen2's budget and the next deep collection is a blocking gen2. Rejected.
  - *A slower ramp doesn't spread the cost:* gen0 holds about 9 s of allocation (about 900 KB/s in steady state), so 50 s of joins still land in one or two collections.
- **Why it matters later:** the bench holds almost no long-lived state. A real zone server holds far more (entities, AI, items, zone data, content), and if a deep blocking collection grows with that, it becomes missed ticks, which action combat feels most. The heavy-heap variant (Considering) tests that.
- **The server results log every GC pause** from startup to the window's end (`gcPauseLog`: time from the window's start, duration, collection, generation, reason, type, sizes after it) and the GC's configuration (`gcSettings`, with the latency mode). `BENCH_GC_LATENCY_MODE` and `BENCH_RAMP` switch the latency mode and the bots' ramp; `BENCH_RESULTS` names an AWS run's results folder. `GC.CollectionCount(1)` includes gen2 collections.
- **AWS runs:** `tools/StackBench/aws/run.sh` needs the AWS CLI logged in (`aws login`), region us-east-2. A dropped SSH connection ends a run (cleanup still runs); keep the machine awake for the 15 minutes.

### Code layout and build

- **One `src/` per layer, each project a self-contained folder named like the project** (`comet/src/Comet.Protocol/Comet.Protocol.csproj`; folder, project, assembly and namespace match). Every layer has the same shape: `src/` and `tests/` (a test project per project it tests, named `<Project>.Tests`), plus `content/` and `unity/`. Shared packages are both a .NET project and a Unity package, so the project folder is the package root. Tests go in `comet/tests/`.
- **All build output goes to `artifacts/`** at the root (.NET's artifacts output), so Unity never imports `bin/` or `obj/` from a shared package and the vault stays clean.
- **One `ProjectComet.slnx` solution,** central package versions (`Directory.Packages.props`), shared defaults in `Directory.Build.props` (nullable on, warnings as errors), C# 9 for the Unity-shared libraries.
- **Projects start from official templates** (`dotnet new`, Unity Hub), then get edited.
- **Tests use xUnit v3.**
- **Code folders are hidden in Obsidian.**

### Shared packages and content (step 2)

- **Two parts, content pipeline first:** 2a is the content pipeline (pure .NET); 2b is the shared packages in Unity.
- **Tomlyn 2 (System.Text.Json-style API, TOML 1.1 only)** reads the content files, with a snake_case naming policy and its source generator.
- **The JSON Schema comes from .NET's built-in `JsonSchemaExporter`,** with the same naming policy and attributes as Tomlyn, so the schema and the reader can't disagree.
- **The shared Content types carry System.Text.Json attributes** (`[JsonRequired]` and the like), which Tomlyn and `JsonSchemaExporter` both read, so missing fields fail the build. Step 2b checks System.Text.Json in Unity and IL2CPP; if it doesn't work there, the build's validation rules check required fields instead.
- **One set of Content types** serves both the TOML files and the compiled MessagePack content; split them only if they diverge.
- **ShapeLand's shape definitions hold look and movement:** display name, mesh kind, max speed and jump velocity, read by both client and server, so movement validation uses content rather than constants.
- **The phase 0 zone's heightmap is generated by a content-tool command** (a seeded test terrain) in the real binary format, until the Unity editor tools exist: a floating disc of gentle hills (56 m across, heights about 1.5–7.5 m) with holes past its edge; the generated file is committed (`shapeland/content/zones/test/terrain.height`), since it stands in for the file those tools will save there.
- **Four libraries from the start** (Protocol, Content, Simulation, Data; `backend.md`). Simulation and Data stay nearly empty in phase 0.
- **The shared packages must compile in the Unity editor and in IL2CPP desktop and web builds,** where MessagePack's ahead-of-time needs (generated formatters) and .NET Standard 2.1 limits show up.
- **Content pipeline, core path only:** TOML files read by Tomlyn into Content types, validated, compiled into a MessagePack file that the server and client load; a minimal ID registry (no generated constants: code looks content up by key at load, failing loudly on unknown keys); a JSON Schema generated from the Content types so VS Code (Even Better TOML) autocompletes and flags errors while editing. Hash delivery, text extraction, CI drift checks and the rename command wait for phase 1.
- **TOML editor support is checked in VS Code** only.
- **The content build is a Comet library with a small console project per game:** `Comet.ContentBuild` (in `comet/src/`, server-side .NET only) reads, validates, assigns registry numbers, compiles and exports the schema; each game's console project (ShapeLand's is `shapeland/src/ShapeLand.ContentBuild`) registers its own types and runs it, with `build`, `schema` and `terrain` commands. Game types stay out of Comet, and Tomlyn's and MessagePack's source generators work without reflection.
- **ShapeLand's shared types live in `ShapeLand.Shared`** (`shapeland/src/ShapeLand.Shared`): its content types now and its messages later, a .NET project and a Unity package like Comet's shared packages.
- **Tomlyn ignores unknown keys,** so the content build checks every key against the type's System.Text.Json metadata (the same metadata the schema comes from) and reports typos at their line.
- **Running the content build:** `dotnet run --project shapeland/src/ShapeLand.ContentBuild -- build` checks `shapeland/content`, adds new keys to `ids.toml` (commit it) and writes `content.bin` and the editor schemas to `artifacts/content/shapeland/`; `terrain [--seed N]` regenerates the test heightmap. Errors use the compiler's `file(line,col): error:` format. VS Code finds the schemas through `.taplo.toml` at the root, after a first build.
- **2a is done,** with every criterion met (autocompletion checked by hand in VS Code): the build compiles ShapeLand's three shapes and the test terrain; a test loads the compiled file and finds each shape by key; bad files fail with clear errors; and VS Code autocompletes and flags errors in a shape file. A server loading the content comes with ShapeLand phase 0.

### Texture filtering comparison

- **Two scenes:** synthetic test textures (checkerboards, gradients on simple shapes) for the technical check, and a small art scene from CC0 pieces (Kenney, Quaternius or KayKit) with low-res textures (64–128 px) added: ground, a wall, a tree or two and one character, covering near ground, distant ground and faces.
- **Four variants:** crisp; crisp with mipmaps; soft (bilinear); soft with trilinear and anisotropic filtering. Distance shimmer is crisp textures' main weakness, and mipmaps decide whether it's acceptable.
- **Judged by eye with a live toggle:** a key cycles the variants while walking around, in a desktop and a web build; screenshots and short clips are kept for the proposal. The project lead decides (`art.md`).

### ShapeLand phase 0

- **Joining:** enter a name, pick cube, diamond or pyramid, get a random colour. Names are unique among players currently online; nothing is saved.
- **World:** a flat plane with a few hard-coded blocks first; once TOML support works (step 2), a tiny zone file with a heightmap and props.
- **Falling off an island respawns you nearby:** below a kill height, the server puts the player back at their last safe ground (or the zone's spawn) after a short fade, with no penalty.
- **Actions: slide and jump.** Jumping tests vertical movement and the world collider early; dodge and combat come in phase 1.
- **Movement validation: speed check and snap-back:** distance against max speed with the ~20% tolerance and stall bursts, allowing for the jump arc, plus the correction-and-blend path (`netcode.md`). Collision checks come with zone files.
- **Other players are drawn through a 100 ms interpolation buffer,** tuned under the impaired run (`netcode.md`).
- **Chat: one shared channel:** everyone on the server sees every message; about 200 characters at most, a simple rate limit, nothing logged (`backend.md`). Messages show in a chat box and as a bubble over the shape.
- **Bots in the full load test** mostly wander, sometimes clump into crowds (the worst case for interest management) and send occasional chat lines. Behaviour is seeded, so runs repeat.

## Considering

- **A heavy-heap variant of the stack benchmark** (agent suggestion; later, before the stage 2 load test, not part of milestone 1): the server also holds synthetic long-lived zone state, about 100–300 MB, in a mix of plain struct arrays and ordinary object graphs, so we can measure how deep GC pauses scale with a real zone's state, which the bench as it stands can't show. It decides whether the GC is a real risk for zone servers: switching away from C# would only be worth weighing if blocking collections recur in steady state and approach the 33 ms tick budget even after per-tick allocation is reduced.
- **A connection slot pool in Comet** (agent suggestion; later, with the heavy-heap variant, not part of milestone 1): a zone server allocates per-connection state and buffers up front at startup, up to the channel's hard cap, so players joining reuse long-lived objects instead of creating about 22 KB each to promote. First measure what that state is (heap dumps idle and with 300 bots, by type), since Kestrel's and the WebSocket's share can't be pooled.

## Rejected

- **Going past phase 0 in the early prototype** (combat, loot, levels): left for phase 1.
- **One generic content tool that loads a game's assembly by reflection** (instead of a small console project per game): run-time assembly loading and version conflicts, a two-step build, errors at run time instead of compile time, and the build serializing through a different MessagePack path than the client.
- **A single `src/` tree** for all code: per-layer folders keep Comet and each game clearly apart.
- **The full content pipeline in phase 0** (hash delivery, text extraction, drift checks, rename command): phase 1.
- **Login and Region servers in phase 0:** they come in phase 1, so phase 0 reaches the load test sooner.
- **A heap settle after startup** (one full, compacting collection before players arrive): it made the join pause worse in every AWS run, turning a gen1 into a gen2.
- **`SustainedLowLatency` mode and a slower bot ramp as fixes for the join pause:** neither brought it under 10 ms.

## Open

- **Full game load test thresholds** (stage 2), written down before it runs.
