# Prototype

**Layer: base (Comet),** with ShapeLand as its game.

Agent notes on the early prototype of Comet and ShapeLand. The roadmap phases are in `proposal.md`; the load test design is in `backend.md`.

## Decided

- **Prototype early,** before the human-readable proposal is done. Building has started with the stack benchmark. **All of ShapeLand phase 0 (3a–3c) comes before returning to the docs** (project lead): it shows whether the project could go anywhere before more time goes into the proposal; the full game load test (step 4) waits until after.
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
- **The phase 0 zone's heightmap is generated by a content-tool command** (a seeded test terrain) in the real binary format, until the Unity editor tools exist (a test stand-in; real ShapeLand levels are made by hand with those tools): a floating disc of gentle hills (56 m across, heights about 1.5–7.5 m) with holes past its edge; the generated file is committed (`shapeland/content/zones/test/terrain.height`), since it stands in for the file those tools will save there.
- **Four libraries from the start** (Protocol, Content, Simulation, Data; `backend.md`), with Client added in 3b. Data stays empty in phase 0.
- **The shared packages must compile in the Unity editor and in IL2CPP desktop and web builds,** where MessagePack's ahead-of-time needs (generated formatters) and .NET Standard 2.1 limits show up.
- **Content pipeline, core path only:** TOML files read by Tomlyn into Content types, validated, compiled into a MessagePack file that the server and client load; a minimal ID registry (no generated constants: code looks content up by key at load, failing loudly on unknown keys); a JSON Schema generated from the Content types so VS Code (Even Better TOML) autocompletes and flags errors while editing. Hash delivery, text extraction, CI drift checks and the rename command wait for phase 1.
- **TOML editor support is checked in VS Code** only.
- **The content build is a Comet library with a small console project per game:** `Comet.ContentBuild` (in `comet/src/`, server-side .NET only) reads, validates, assigns registry numbers, compiles and exports the schema; each game's console project (ShapeLand's is `shapeland/src/ShapeLand.ContentBuild`) registers its own types and runs it, with `build`, `schema` and `terrain` commands. Game types stay out of Comet, and Tomlyn's and MessagePack's source generators work without reflection.
- **2b uses Unity 6000.6.4f1** (the newest Unity 6 release) with the WebGL and macOS IL2CPP build modules; **ShapeLand's Unity project uses URP** (Universal 3D template) in `shapeland/unity/`; **MessagePack comes through NuGetForUnity,** from a committed `packages.config` matching the .NET version.
- **2b is done:** the check scene (`shapeland/unity`, `SharedPackageCheck`) loads the compiled content, finds each shape by key, draws the test island with its holes and round-trips a protocol frame, and passes in the editor (a Play mode test), an IL2CPP macOS build and a web build (checked by hand in Firefox; headless Chrome showed no WebGL here). Builds come from `ShapeLand.Client.Editor.Builds` (`MacOS`, `Web`) into `artifacts/unity/shapeland/`; the web build is uncompressed for now.
- **System.Text.Json comes with Unity 6.6** (its .NET Standard 2.1 extensions, with `[JsonRequired]`), so NuGetForUnity installs only MessagePack (with its analyzer, annotations and StringTools). On a fresh clone, and whenever `packages.config` changes, run `tools/unity-restore.sh` before the first open: the editor can't compile the shared packages, and so can't run NuGetForUnity's own restore, until MessagePack is there. The script runs the NuGetForUnity CLI restore, then rewrites the analyzer DLLs' `.meta` files: the CLI (4.5.0) writes them in a format Unity 6.6 rejects (PluginImporter version 1), so Unity would import the source generator as an ordinary library (`MessagePackSerializationException` then exists twice). The restored packages stay gitignored. The Play mode test needs `content.bin` in `StreamingAssets` (ShapeLand > Copy Content, or copy it from `artifacts/content/shapeland`).
- **Each platform's build toolchain packages are committed** when we start building for it (the final game ships on macOS, Linux and Windows). Unity adds them when that platform is the active build target; a fresh Library on Linux defaults to Linux, so open with `-buildTarget WebGL` (the main target) until Linux builds are wanted.
- **Play mode tests don't read a child process's output asynchronously** (`BeginOutputReadLine`): in Unity's Mono it keeps the editor from exiting after the run. `JoinTests` waits for the server's port instead.
- **A batch-mode editor on Linux often hangs while quitting,** after `-executeMethod` (creating scenes, web builds) and after `-runTests`, once its work is done (once with a PulseAudio assertion, `pa_stream_get_state`; otherwise the cause is unknown). Run batch jobs through `tools/unity-batch.sh`, which watches the log for the line that ends the run, allows 20 s for a normal exit, then stops the editor and returns its result. A web build also rewrites `Mobile_RPAsset.asset`'s shader prefiltering and `ProjectSettings.asset`; those changes aren't committed.
- **Batch imports keep graphics on:** a `-nographics` import emptied the URP global settings' runtime list. The first CLI test run on a fresh Library also froze on the initial asset refresh (cause unknown); later runs were fine.
- **The shared projects are local Unity packages** (`package.json` and an assembly definition in each project folder, referenced by `file:` path); Unity's `.meta` files in them are committed, since assembly references depend on their GUIDs.
- **ShapeLand's shared types live in `ShapeLand.Shared`** (`shapeland/src/ShapeLand.Shared`): its content types now and its messages later, a .NET project and a Unity package like Comet's shared packages.
- **Tomlyn ignores unknown keys,** so the content build checks every key against the type's System.Text.Json metadata (the same metadata the schema comes from) and reports typos at their line.
- **Running the content build:** `dotnet run --project shapeland/src/ShapeLand.ContentBuild -- build` checks `shapeland/content`, adds new keys to `ids.toml` (commit it) and writes `content.bin` and the editor schemas to `artifacts/content/shapeland/`; `terrain [--seed N]` regenerates the test heightmap. Errors use the compiler's `file(line,col): error:` format. VS Code finds the schemas through `.taplo.toml` at the root, after a first build.
- **2a is done,** with every criterion met (autocompletion checked by hand in VS Code): the build compiles ShapeLand's three shapes and the test terrain; a test loads the compiled file and finds each shape by key; bad files fail with clear errors; and VS Code autocompletes and flags errors in a shape file. A server loading the content comes with ShapeLand phase 0.

### Texture filtering comparison

- **Two scenes:** synthetic test textures (checkerboards, gradients on simple shapes) for the technical check, and a small art scene from CC0 pieces (Kenney, Quaternius or KayKit) with low-res textures (64–128 px) added: ground, a wall, a tree or two and one character, covering near ground, distant ground and faces.
- **Four variants:** crisp; crisp with mipmaps; soft (bilinear); soft with trilinear and anisotropic filtering. Distance shimmer is crisp textures' main weakness, and mipmaps decide whether it's acceptable.
- **Judged by eye with a live toggle:** a key cycles the variants while walking around, in a desktop and a web build; screenshots and short clips are kept for the proposal. The project lead decides (`art.md`).

### ShapeLand phase 0

- **Joining:** enter a name, pick cube, diamond or pyramid, and pick a body colour and an eye colour. Names are unique among players currently online; nothing is saved.
- **Colours come from small starting sets, with more from items later** (project lead): the join request carries the chosen colours (0xRRGGBB) and the server checks each is one the player may use, which in phase 0 means the starting sets (`ShapeLandRules.BodyColours`, `EyeColours`). The sets in code are placeholders; the project lead decides the final ones. Until the join screen exists, clients and bots pick at random.
- **Three sub-steps:** 3a, the server and protocol, tested with headless bots (including a cheating bot); 3b, the Unity client; 3c, a playtest with desktop and web clients and bots under simulated latency and loss, where the interpolation delay's settings and validation tolerances get tuned.
- **Phase 0 is done when** a desktop client, a web client and bots join, see each other slide and jump smoothly and chat with bubbles, also under simulated latency; falling off the island respawns you; the cheating bot gets snapped back and logged; and validation has unit tests. The full game load test (step 4) follows.
- **3a is done:** `ShapeLand.GameServer` (on `Comet.Server`), `ShapeLand.Bots` (honest wanderers and cheaters that slide at 2.5× speed) and the shared `Comet.Simulation` package. In a live run, honest bots get no snap-backs and every cheater report fails the speed check; an end-to-end test runs the server in-process with bots over real WebSockets. Run them with `dotnet run --project shapeland/src/ShapeLand.GameServer` (port 5080, `/ws`; needs the content build) and `dotnet run --project shapeland/src/ShapeLand.Bots -- --count 5 --cheaters 1`.
  - *Messages:* Comet's IDs are 1–63 and games number theirs from 64, so every ID stays one byte. A game passes its MessagePack resolver to the frame writer and reader (`ProtocolSerializer.CreateOptions`, `ConnectionRegistry`), so its messages work under IL2CPP too.
  - *Movement checks* (`Comet.Server.Movement.MovementValidator`): speed is a distance budget (each server tick earns max speed × 1.2, capped at one second's worth, so a burst after a stall passes but banked movement doesn't); height is limited to the shape's jump apex × 1.2 above the last ground stood on; the body may not overlap a block or sit below the terrain. A snap-back or respawn carries a sequence number the client echoes, so reports already in flight are ignored instead of causing a chain of snap-backs. Violations are logged at the 1st, 10th, 100th… per player.
  - *Shared movement* (`Comet.Simulation`): ground height follows the terrain mesh's own triangles (holes included); the player motor (used by bots now and the Unity client in 3b) slides, jumps, falls, walks up 0.35 m steps and slopes up to ~63°, and slides along blocks. Gameplay movement is our own kinematic code, with no physics engine (`backend.md`, open).
  - *Joining and chat:* names are 1–16 letters, digits, '-', '_' and single spaces, unique online ignoring case; chat is trimmed, up to 200 characters, with a burst of 3 and then one every 2 seconds.
- **3b, the Unity client:**
  - *GameObjects for phase 0* (MonoBehaviours and prefabs); `Comet.Client` stays engine-free, so GameObjects or ECS (Entities) is revisited for Project Anima's client.
  - *A browser check right after the join scene:* a web build of the join scene connects to a local server in Firefox (checked by hand) before movement is built on top; the full check at the end of 3b stays. A web build of the join scene (ShapeLand > Build > Web (Join Scene), to `artifacts/unity/shapeland/web-join`) joined a local server alongside bots from headless Firefox, and the server saw it leave; a web build of the Game scene (ShapeLand > Build > Web (Game Scene), to `artifacts/unity/shapeland/web-game`) has since been played by hand in the project lead's browser.
  - *Entity states carry the mover's own tick:* the server stamps each state with the tick in the mover's report frame header (its server-tick estimate), clamped to no later than the arrival tick, no more than 0.5 s before it and never before that player's previous stamp; `EntityState` has a `Tick` and clients buffer by it. Otherwise senders' network jitter would show as wobble on everyone else's screen. This is the usual model for movement that clients report: TrinityCore (the WoW server emulator) converts each movement packet's client time to server time with a per-player clock offset and sends that on, rather than stamping on arrival; our clients do the conversion themselves. Engines that stamp with server time (Source and the like) run everyone's movement on the server. Movement reaches other screens at the same time either way; sender stamps only make the sender's trip count towards the interpolation delay.
  - *Placeholders tuned in 3c* (agent picks): the snap-back blend (150 ms, smoothstep), the respawn fade (from 18 m above the kill height, back in over 0.4 s; `RespawnFade`), the sharp-turn report angle (45°), the idle-gap restamp (200 ms), frames assumed sent mid-tick for the clock, and the 64 KB frame limit.
  - *Progress:* `Comet.Client` is built and tested (xunit), and the bots run on it. The Comet Unity package (browser transport, `CometConnection` driver) and the bare join scene (`Assets/Scenes/Join.unity`, `JoinClient`, created by ShapeLand > Create Join Scene) are in; `JoinTests` joins the real server from Play mode on Linux. Own movement and camera are in: the Game scene (`Assets/Scenes/Game.unity`, created by ShapeLand > Create Game Scene) draws the island, the blocks and the player's own shape (`GameView`, `WorldMeshes`), moves it with `LocalPlayer` on `Comet.Client`'s `FixedStep`, and follows it with `OrbitCamera`; the bindings are defined in code (`ShapeLandControls`) rather than an input asset, so they sit in one file. `GameTests` walks and jumps with a simulated keyboard against the real server and gets no snap-backs (Play mode tests route all input to the game, since batch mode has no focused Game view). A discrete jump rises about v·dt/2 above v²/2g (0.05 m at 60 Hz, 0.1–0.14 m for 30 Hz bots), inside the validator's 1.2× apex margin. The camera keeps above the ground and block tops but can still clip into a block's side. Tried by hand in the editor and the browser. Other players are in: `GameView` draws each one where `Comet.Client`'s interpolation buffer puts them, and `GameTests` checks real bots (the `ShapeLand.Bots` program) appear, move and are removed when they leave. Corrections and the respawn fade are in: `LocalPlayer` blends a snap-back with `CorrectionBlend` (the camera follows the drawn shape) and darkens the screen while falling (`ScreenFade`, a placeholder overlay), and `GameView` fades other players' shapes (`ShapeFade`, swapping to transparent copies of the materials made by ShapeLand > Create Game Scene). `GameTests` checks that the speed cheat is snapped back and blended, and that walking off the island darkens the screen before the respawn arrives and lightens it after; other players' fade is checked only through its materials, since bots don't fall off. The join screen is in: `JoinScreen` (UI Toolkit: `Assets/UI/JoinScreen.uxml`, the plain look in `ShapeLand.uss`, Atkinson Hyperlegible under the OFL, panel settings at the mockups' 960 × 600 scaled with the screen) over the camera's slow orbit of the island, with the chosen shape turning in a render-texture preview (on a layer the main camera skips), its speed and jump height, `ShapeIcon` vector icons, the server's refusals and connection failures on the card (it stays up to try again on the same connection), and the choices remembered in PlayerPrefs; `JoinClient` joins on request (`joinOnStart` stays for tests and the bare Join scene). `GameTests` joins through the screen with a chosen shape and colours, gets a bad name caught before sending, a taken name refused and then a second try accepted, and an unreachable server reported; in the web build, typing a name, picking with the mouse and pressing Enter joined. Chat and bubbles are in: `Hud` (`Assets/UI/Hud.uxml`, its own GameObject: a UIDocument under another one is added inside that one's tree, which once left the chat box drawn above the screen) shows the chat log with each speaker's shape icon, fading after 10 s without messages, an input that Enter opens (on release: focusing it while Enter is down left web builds ignoring typed characters) and Esc closes, with the shape held still while typing; name tags over others and a bubble over every speaker (own included) with their latest line; and the server's too-fast refusals as notes in the log. `GameTests` covers sending and the echoed line and bubble, Enter opening chat with the shape staying put, a bot's line in the log and its bubble, the too-fast note, and the overlay filling the screen beside the join screen; in the web build three lines typed after Enter arrived whole. Next: the desktop and web builds.
  - *Client logic in a shared `Comet.Client` package* (`backend.md`, shared libraries): the session, server-tick estimate, interpolation buffer and correction blend, tested with xunit; the bots move onto it. `comet/unity` holds only the transport and Unity glue.
    - *One polled session on every platform:* the caller runs `Update(now)` on its own thread (Unity's main thread), passing the time in, so tests control the clock. Only the transports differ, behind one interface: desktop and bots use an async `ClientWebSocket` that receives on a background thread into a queue (in `Comet.Client`); the web uses a `.jslib` that queues frames from the browser (in the Unity package). Unity 6 web builds have no C# threads (Unity's web multithreading covers only engine code and Burst jobs, and needs COOP/COEP headers), and Unity objects belong to the main thread anyway, so web threading later would change only the web transport. Timers and `CancellationTokenSource` don't work on the web either, so code the web build runs keeps time only through `Update(now)`.
    - *Server-tick estimate:* a ping every second; the lowest-RTT sample of the last ~10 is trusted (the least queueing delay), and the estimate slews toward it (running up to ~5% fast or slow), jumping only when it's off by more than ~250 ms. It gives two timelines: the server's tick now (for stamping inputs) and the tick of frames arriving now (half a round trip behind); other entities are drawn the interpolation delay behind the second, so the cushion doesn't shrink as latency grows.
    - *Far jumps aren't blended:* two states further apart than the teleport speed allows (`ShapeLandWorld.TeleportSpeed`: 1.5× the fastest shape's top speed or jump speed, across the ground or upwards; falling never counts) are drawn as a jump at the second state's tick, so respawns and snap-backs of other players don't slide across the island. The respawn fade builds on it.
    - *Interpolation gaps:* when an entity's buffer runs dry it holds at the newest state, with no extrapolation; a state left standing from before an idle gap is restamped to just before the next one, so a player who starts moving doesn't glide slowly across the gap.
    - *Adaptive interpolation delay:* one delay for every other entity (so they're all drawn at the same moment), from `InterpolationDelay` in `Comet.Client`. Each state that continues a move gives a sample of the delay it needed: the receive tick on arrival minus the entity's previous stamp, which covers the gap between reports, the sender's trip, the server's tick wait and jitter. The target is the 95th percentile of the last 2 s of samples plus one tick, never under two report intervals; the delay moves towards it by running the render tick at most 4% slow (growing) or 2% fast (shrinking), so it never jumps or goes back, and keeps its target while nobody moves. Engines size the delay from at least two send intervals: Source's `cl_interp_ratio` 2 (100 ms at 20 updates a second, enough to lose one); Gaffer on Games about three to survive 2–5% loss; Mirror (Unity, over UDP, TCP and WebSockets alike) adapts it as two intervals plus measured jitter, reached by running a few percent fast or slow. The first fixed 100 ms was one interval at the bots' report rate, so 87–99% of moves ran dry (a hold, then a catch-up jump). With five local bots the delay settles near 190 ms, drawn speed stays near the bots' real speeds (sd 1.0–1.6 m/s against about 4–5), and frames past the newest state match frames standing still. TCP adds only on loss (a resend holds later states about one round trip), which the adaptive delay absorbs by growing.
    - *Snap-back blend:* the simulation jumps to the correction at once (so its next reports aren't rejected again); the drawn position carries an offset that decays to zero over ~150 ms. A respawn isn't blended; Unity fades instead.
  - *The respawn fade starts while falling* (project lead): a respawn arrives without warning, so the screen darkens as your own shape falls towards the kill height and is already dark when the respawn arrives (it cuts to black if not), then fades back in. Other players' shapes fade out and back in the same way (project lead), by height, so a snap-back, made at full visibility, stays a plain jump. The numbers are game code (`RespawnFade` in the ShapeLand client).
  - *The Comet Unity package* (`comet/unity`, `com.comet.unity`) holds the browser transport and a game-agnostic driver that owns the transport and session, updating it early each frame and flushing at the end, on Unity's unscaled real-time clock. Desktop and the editor use `Comet.Client`'s `WebSocketTransport`; web builds use the browser one, picked at compile time.
  - *The browser transport queues in JavaScript and is polled:* the `.jslib` keeps each socket's received messages; `TryReceive` asks for the next one's length, then copies it into C#. No JavaScript calls into C#.
  - *Joining from a bare scene is checked by a Play mode test* that starts the real game server (`dotnet run`, a free port, content built), joins, and checks the welcome and the player's own spawn; the web transport is checked by hand in Firefox at the builds step.
  - *UI Toolkit* for the join screen, chat box, name labels and bubbles, drafted first as HTML mockups (`ui.md`). Name labels and bubbles are screen-space elements placed over each shape every frame, so they keep one readable size, rather than world-space panels.
  - *The join screen is a small card over the island:* name, shape, body colour and eye colour (plus the server address on desktop) and Join, centred over a slowly orbiting view of the island.
  - *Shape and colours are picked with buttons and swatches,* with the chosen shape turning in a small 3D preview. The final colour sets are the project lead's.
  - *The join card shows the chosen shape's stats* (project lead): speed and jump height under the preview, each with a bar against the best shape. Jump height is worked out from content and `MovementRules` (v²/2g), so it follows tuning.
  - *ShapeLand's UI uses the plain look* (project lead): dark see-through panels, one blue accent, Atkinson Hyperlegible. Mockup: `.claude/drafts/ui/shapeland-phase-0.html`.
  - *The chat log fades when idle:* a log in a lower corner with the input underneath (Enter opens it); it fades out after a while without messages and comes back when one arrives or chat opens. Bubbles show the same lines over the speakers.
  - *The game server can serve the web build* (a dev option, `ShapeLand:WebRoot`), so one command runs both and the page connects to the host it came from; a `?server=` in the page's address names another server (for a page hosted elsewhere, such as the public test build). The desktop join screen has an address field, defaulting to `localhost:5080`. ShapeLand only: Project Anima won't have it, since its page comes from the Login server (`backend.md`).
  - *`tools/shapeland-web.sh` builds and runs the web client without opening the editor:* it builds the content, makes the Unity web build in batch mode (skipped when nothing it's made from changed), runs the game server serving it, optionally starts bots (`--bots`, `--cheaters`) and opens the page; Ctrl+C stops everything. Checked end to end: headless Firefox loaded the page from the game server and joined. `--lan` listens on every interface and prints the machine's network address. Web builds allow plain HTTP downloads (set in `Builds`): Unity otherwise fetches the content only from localhost, so other devices saw an empty sky. Until the join screen, the default name gets a random number ("Player 427") so several clients can join.
  - *A dev-only speed-cheat key* (2.5×, like the cheating bots) in development builds and the editor, to see a snap-back blend by eye: hold C (placeholder). Release web builds don't have it.
  - *Starting camera and controls* (placeholders, adjusted by playing): a third-person orbit camera; WASD and Space, the mouse turning the camera while the right button is held; the left stick, the right stick and the south button on a gamepad; movement relative to the camera, the shape facing where it moves; Enter opens chat.
  - *Own movement on a fixed 60 Hz step:* the client runs the shared `PlayerMotor` in fixed 1/60 s steps (as many as fit in the frame's time) and draws the shape between the last two, so movement doesn't depend on frame rate and a hitch can't tunnel through a block. 60 Hz rather than the bots' 30 adds about 17 ms of input delay on average instead of 33, for action combat; reports stay paced by `PositionReporter`. The rate is one constant, tunable later.
  - *The island is drawn from the heightmap* at run time, using the same triangles as `TerrainGround` (holes included), so what's drawn is what's stood on; blocks are drawn from `ShapeLandWorld`'s boxes.
  - *Facing turns at a rate; movement doesn't wait for it:* the shared motor turns facing towards the move direction at `MovementRules.TurnSpeed` (900°/s placeholder, so 180° takes 0.2 s), while speed and direction respond at once. Facing is game state (reported, replicated, later used by combat), so it lives in the motor rather than only in drawing.
  - *Shapes hover a little* (project lead): drawn `hover` (0.15 m to start) above their feet, set per shape in content, which sits better on the terrain; collision and movement are unchanged.
  - *Shapes have eyes:* two small dark blocks on the front face show which way a shape faces; the diamond's middle ring is square to the axes so it has a front face.
  - *A hand-written orbit camera* (no Cinemachine), with its numbers in one place.
  - *A Game scene* grows into the full client; the bare Join scene and its test stay as the minimal connection check.
  - *Order:* transport and session, joining from a bare scene; own movement and camera; other players and interpolation; corrections and respawn fade; join screen, chat and bubbles; desktop and web builds.
  - *Done when* a desktop client and a web client (checked by hand in Firefox) join alongside 5 honest bots and 1 cheater; everyone slides, jumps and chats with bubbles; falling off respawns with a fade; a forced snap-back blends rather than jumps; and `Comet.Client` has tests.
- **Everyone sees everyone in phase 0** (a broadcast); grid interest management and the priority accumulator (`netcode.md`) come with the full load test.
- **Mechanisms in Comet, the rest in ShapeLand:** Comet has the transport, replication (spawn, state, despawn), movement validation, the ground, collision and jump maths (a new shared `Comet.Simulation` package) and the client transport; ShapeLand has joining with a shape and colour, chat and its rate limit, the hard-coded blocks and the game server program. Chat moves to Comet once Project Anima needs it.
- **Player shapes are meshes generated in code** on the client (cube, diamond as a square bipyramid, pyramid): flat-shaded with hard edges and tinted with the player's colour, chosen by each shape's `mesh` in content and sized by its `[look]` table there (project lead): width, depth, height, hover, the diamond's waist, and the eyes' size, spacing and level (they sit on the front face there, tilted with it), checked by the content build. Looks only: every shape still collides as the one shared body in `MovementRules`. The server never needs them. They start in the ShapeLand Unity project and move into a shared primitive library when the prop tool is built (monsters' curved shapes come from the same code later).
- **The Unity client uses our own thin WebSocket transport:** a small Comet Unity package with a browser `.jslib` on the web and .NET's `ClientWebSocket` on desktop, behind one interface.
- **World:** the generated floating island from content plus a few hard-coded blocks with simple box collision on client and server, defined once in `ShapeLand.Shared`; later, a tiny zone file with a heightmap and props, generated by the content tool until the editor tools exist.
- **Falling off an island respawns you nearby:** below a kill height, the server puts the player back at their last safe ground (or the zone's spawn) after a short fade, with no penalty.
- **Actions: slide and jump.** Jumping tests vertical movement and the world collider early; dodge and combat come in phase 1.
- **Movement validation: speed check and snap-back:** distance against max speed with the ~20% tolerance and stall bursts, allowing for the jump arc, plus the correction-and-blend path (`netcode.md`). Collision checks come with zone files.
- **Other players are drawn through a 100 ms interpolation buffer,** tuned under the impaired run (`netcode.md`).
- **Chat: one shared channel:** everyone on the server sees every message; about 200 characters at most, a simple rate limit, nothing logged (`backend.md`). Messages show in a chat box and as a bubble over the shape.
- **Bots in the full load test** mostly wander, sometimes clump into crowds (the worst case for interest management) and send occasional chat lines. Behaviour is seeded, so runs repeat.

## Considering

- **Per-shape collision bodies** (later, if drawn sizes drift far from the shared body): each shape's body radius and height from content, used by the client motor, the server's validation and the bots, so what's drawn is what collides. For now every shape collides as `MovementRules`' one body (project lead).
- **A heavy-heap variant of the stack benchmark** (agent suggestion; later, before the stage 2 load test, not part of milestone 1): the server also holds synthetic long-lived zone state, about 100–300 MB, in a mix of plain struct arrays and ordinary object graphs, so we can measure how deep GC pauses scale with a real zone's state, which the bench as it stands can't show. It decides whether the GC is a real risk for zone servers: switching away from C# would only be worth weighing if blocking collections recur in steady state and approach the 33 ms tick budget even after per-tick allocation is reduced.
- **Shifting each sender's stamps by its smoothed one-way latency** (agent suggestion; to test in 3c or the load test with a few high-ping bots): the server keeps the sender's stamp but adds that player's slowly smoothed trip time, so the spacing stays free of jitter while a high-ping sender's average latency no longer counts towards everyone's delay. Matters only when many players have high pings; with one among many, the 95th percentile mostly leaves the delay alone and only that player holds now and then. Not found in any source.
- **A connection slot pool in Comet** (agent suggestion; later, with the heavy-heap variant, not part of milestone 1): a zone server allocates per-connection state and buffers up front at startup, up to the channel's hard cap, so players joining reuse long-lived objects instead of creating about 22 KB each to promote. First measure what that state is (heap dumps idle and with 300 bots, by type), since Kestrel's and the WebSocket's share can't be pooled.

## Rejected

- **A Unity 7 alpha side test after 2b:** the alpha (7000.0.0a7) still targets .NET Standard and C# 9 (project lead), so it couldn't yet show `net10.0`-only shared packages; revisit when Unity 7 matures.

- **Going past phase 0 in the early prototype** (combat, loot, levels): left for phase 1.
- **One generic content tool that loads a game's assembly by reflection** (instead of a small console project per game): run-time assembly loading and version conflicts, a two-step build, errors at run time instead of compile time, and the build serializing through a different MessagePack path than the client.
- **A single `src/` tree** for all code: per-layer folders keep Comet and each game clearly apart.
- **Unity projects under `src/`, or renamed to the `Comet.X` / `ShapeLand.X` pattern:** `src/` holds the .NET projects in `ProjectComet.slnx`, and Unity's generated `.csproj` files share names with them; the assemblies inside already follow the pattern.
- **The full content pipeline in phase 0** (hash delivery, text extraction, drift checks, rename command): phase 1.
- **Login and Region servers in phase 0:** they come in phase 1, so phase 0 reaches the load test sooner.
- **A heap settle after startup** (one full, compacting collection before players arrive): it made the join pause worse in every AWS run, turning a gen1 into a gen2.
- **`SustainedLowLatency` mode and a slower bot ramp as fixes for the join pause:** neither brought it under 10 ms.

## Open

- **Full game load test thresholds** (stage 2), written down before it runs.
- **Whether a newer NuGetForUnity CLI writes current `.meta` files** (project lead: check later); if so, `tools/unity-restore.sh` can drop its rewrite.
