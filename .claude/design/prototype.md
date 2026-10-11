# Prototype

**Layer: base (Comet),** with ShapeLand as its game.

Agent notes on the early prototype of Comet and ShapeLand. The roadmap phases are in `proposal.md`; the load test design is in `backend.md`.

## Decided

- **Prototype early,** before the human-readable proposal is done. **All of ShapeLand phase 0 (3a–3c) came before returning to the docs** (project lead): it shows whether the project could go anywhere before more time goes into the proposal. Phase 0 is done (2026-10-10). **The full game load test and the texture filtering comparison move to the start of phase 1** (project lead), after the proposal work; the roadmap's phase 0 is done when the stack benchmark and ShapeLand's network criteria pass.
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
  4. The full game load test on top of ShapeLand (now opening phase 1).
  - The texture filtering comparison can happen any time, in parallel (now alongside phase 1).
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

- **Milestone 1 passes, with one known issue** (project lead, 2026-10-06): every check passes, locally and on AWS, except the worst GC pause on AWS (10.04 ms against 10, under the impaired profile): one or two blocking collections of 10–20 ms as players join, copying their connections' state (about 22 KB each) out of gen0 and then gen1. It never caused a missed tick. No GC setting fixed it (Rejected); its likely fix is the connection slot pool (Considering), before the stage 2 load test. The GC limit keeps counting pauses from players joining (project lead): a restart or a crowd teleporting in looks similar in real play. The numbers and the GC investigation are in `.claude/drafts/Prototype Results.md`.
- **Server CPU is the tightest margin:** TCP's own cost at a long round trip doubles it under the impaired profile (0.21 to 0.44 cores locally; kernel time doubles on AWS too, with no netem on the server's machine), so players far away cost the server more CPU than nearby ones.
- **Why the GC pause matters later:** the bench holds almost no long-lived state. A real zone server holds far more (entities, AI, items, zone data, content), and if a deep blocking collection grows with that, it becomes missed ticks, which action combat feels most. The heavy-heap variant (Considering) tests that.
- **The server results log every GC pause** from startup to the window's end (`gcPauseLog`: time from the window's start, duration, collection, generation, reason, type, sizes after it) and the GC's configuration (`gcSettings`, with the latency mode). `BENCH_GC_LATENCY_MODE` and `BENCH_RAMP` switch the latency mode and the bots' ramp; `BENCH_RESULTS` names an AWS run's results folder. `GC.CollectionCount(1)` includes gen2 collections.
- **AWS runs:** `tools/StackBench/aws/run.sh` needs the AWS CLI logged in (`aws login`), region us-east-2. A dropped SSH connection ends a run (cleanup still runs); keep the machine awake for the 15 minutes.

### Code layout and build

- **One `src/` per layer, each project a self-contained folder named like the project** (`comet/src/Comet.Protocol/Comet.Protocol.csproj`; folder, project, assembly and namespace match). Every layer has the same shape: `src/` and `tests/` (a test project per project it tests, named `<Project>.Tests`), plus `content/` and `unity/`. Shared packages are both a .NET project and a Unity package, so the project folder is the package root. Tests go in `comet/tests/`.
- **All build output goes to `artifacts/`** at the root (.NET's artifacts output), so Unity never imports `bin/` or `obj/` from a shared package and the vault stays clean.
- **One `ProjectComet.slnx` solution,** central package versions (`Directory.Packages.props`), shared defaults in `Directory.Build.props` (nullable on, warnings as errors), C# 9 for the Unity-shared libraries.
- **Projects start from official templates** (`dotnet new`, Unity Hub), then get edited.
- **Tests use xUnit v3.**
- **Code folders are hidden in Obsidian.**

### Shared packages and content

- **Tomlyn 2 (System.Text.Json-style API, TOML 1.1 only)** reads the content files, with a snake_case naming policy and its source generator.
- **The JSON Schema comes from .NET's built-in `JsonSchemaExporter`,** with the same naming policy and attributes as Tomlyn, so the schema and the reader can't disagree.
- **The shared Content types carry System.Text.Json attributes** (`[JsonRequired]` and the like), which Tomlyn and `JsonSchemaExporter` both read, so missing fields fail the build.
- **One set of Content types** serves both the TOML files and the compiled MessagePack content; split them only if they diverge.
- **ShapeLand's shape definitions hold look and movement:** display name, mesh kind, max speed and jump velocity, read by both client and server, so movement validation uses content rather than constants.
- **The phase 0 zone's heightmap is generated by a content-tool command** (a seeded test terrain) in the real binary format, until the Unity editor tools exist (a test stand-in; real ShapeLand levels are made by hand with those tools): a floating disc of gentle hills (56 m across, heights about 1.5–7.5 m) with holes past its edge; the generated file is committed (`shapeland/content/zones/test/terrain.height`), since it stands in for the file those tools will save there.
- **Four libraries from the start** (Protocol, Content, Simulation, Data; `backend.md`), plus Client. Data stays empty in phase 0.
- **The shared packages must compile in the Unity editor and in IL2CPP desktop and web builds,** where MessagePack's ahead-of-time needs (generated formatters) and .NET Standard 2.1 limits show up.
- **Content pipeline, core path only:** TOML files read by Tomlyn into Content types, validated, compiled into a MessagePack file that the server and client load; a minimal ID registry (no generated constants: code looks content up by key at load, failing loudly on unknown keys); a JSON Schema generated from the Content types so VS Code (Even Better TOML) autocompletes and flags errors while editing. Hash delivery, text extraction, CI drift checks and the rename command wait for phase 1.
- **TOML editor support is checked in VS Code** only.
- **The content build is a Comet library with a small console project per game:** `Comet.ContentBuild` (in `comet/src/`, server-side .NET only) reads, validates, assigns registry numbers, compiles and exports the schema; each game's console project (ShapeLand's is `shapeland/src/ShapeLand.ContentBuild`) registers its own types and runs it, with `build`, `schema` and `terrain` commands. Game types stay out of Comet, and Tomlyn's and MessagePack's source generators work without reflection.
- **Unity 6000.6.4f1** (the newest Unity 6 release) with the WebGL and macOS IL2CPP build modules; **ShapeLand's Unity project uses URP** (Universal 3D template) in `shapeland/unity/`; **MessagePack comes through NuGetForUnity,** from a committed `packages.config` matching the .NET version.
- **The shared package check scene** (`shapeland/unity`, `SharedPackageCheck`) loads the compiled content, finds each shape by key, draws the test island with its holes and round-trips a protocol frame, and passes in the editor (a Play mode test), an IL2CPP macOS build and a web build (checked by hand in Firefox; headless Chrome showed no WebGL here). Builds come from `ShapeLand.Client.Editor.Builds` (`MacOS`, `Web`) into `artifacts/unity/shapeland/`; the web build is uncompressed for now.
- **System.Text.Json comes with Unity 6.6** (its .NET Standard 2.1 extensions, with `[JsonRequired]`), so NuGetForUnity installs only MessagePack (with its analyzer, annotations and StringTools). On a fresh clone, and whenever `packages.config` changes, run `tools/unity-restore.sh` before the first open: the editor can't compile the shared packages, and so can't run NuGetForUnity's own restore, until MessagePack is there. The script runs the NuGetForUnity CLI restore, then rewrites the analyzer DLLs' `.meta` files: the CLI (4.5.0) writes them in a format Unity 6.6 rejects (PluginImporter version 1), so Unity would import the source generator as an ordinary library (`MessagePackSerializationException` then exists twice). The restored packages stay gitignored. The Play mode test needs `content.bin` in `StreamingAssets` (ShapeLand > Copy Content, or copy it from `artifacts/content/shapeland`).
- **Each platform's build toolchain packages are committed** when we start building for it (the final game ships on macOS, Linux and Windows). Unity adds them when that platform is the active build target; a fresh Library on Linux defaults to Linux, so open with `-buildTarget WebGL` (the main target) until Linux builds are wanted.
- **Play mode tests don't read a child process's output asynchronously** (`BeginOutputReadLine`): in Unity's Mono it keeps the editor from exiting after the run. `JoinTests` waits for the server's port instead.
- **A batch-mode editor on Linux often hangs while quitting,** after `-executeMethod` (creating scenes, web builds) and after `-runTests`, once its work is done (once with a PulseAudio assertion, `pa_stream_get_state`; otherwise the cause is unknown). Run batch jobs through `tools/unity-batch.sh`, which watches the log for the line that ends the run, allows 20 s for a normal exit, then stops the editor and returns its result. A web build also rewrites `Mobile_RPAsset.asset`'s shader prefiltering and `ProjectSettings.asset`; those changes aren't committed.
- **Batch imports keep graphics on:** a `-nographics` import emptied the URP global settings' runtime list. The first CLI test run on a fresh Library also froze on the initial asset refresh (cause unknown); later runs were fine.
- **Linux IL2CPP builds need the Linux target active and two old libraries.** The sysroot package hooks into the build only when compiled for Linux, so batch builds start the editor with `-buildTarget Linux64` (from the menu, switch the platform first). Unity's bundled clang wants `libncurses.so.6` and `libxml2.so.2`, which Arch-family distros don't ship: install `libxml2-legacy` and link `/usr/lib/libncurses.so.6` to `libncursesw.so.6`. Ubuntu, Windows and macOS hosts need neither; release builds for Linux will likely come from CI in an Ubuntu container (considering).
- **Batch builds start the editor on their target** (`-buildTarget WebGL`, `Linux64`, `OSXUniversal`): a web build from an editor left on Linux failed every WebGL draw ("Mismatch between texture format and sampler type"), leaving an empty sky; `Builds` refuses a batch build on the wrong target. Main-light shadows stay on in `Mobile_RPAsset`.
- **Unity's audio is off** (`AudioManager`): ShapeLand has no sound, and a Linux player could abort in PulseAudio while quitting, leaving its window up.
- **A killed or failed build leaves the Performance Testing package's files behind** (`Assets/Resources/PerformanceTest*` and an entry in `preloadedAssets`); delete them rather than commit them.
- **The shared projects are local Unity packages** (`package.json` and an assembly definition in each project folder, referenced by `file:` path); Unity's `.meta` files in them are committed, since assembly references depend on their GUIDs.
- **ShapeLand's shared types live in `ShapeLand.Shared`** (`shapeland/src/ShapeLand.Shared`): its content types now and its messages later, a .NET project and a Unity package like Comet's shared packages.
- **Tomlyn ignores unknown keys,** so the content build checks every key against the type's System.Text.Json metadata (the same metadata the schema comes from) and reports typos at their line.
- **Running the content build:** `dotnet run --project shapeland/src/ShapeLand.ContentBuild -- build` checks `shapeland/content`, adds new keys to `ids.toml` (commit it) and writes `content.bin` and the editor schemas to `artifacts/content/shapeland/`; `terrain [--seed N]` regenerates the test heightmap. Errors use the compiler's `file(line,col): error:` format. VS Code finds the schemas through `.taplo.toml` at the root, after a first build.

### Texture filtering comparison

- **Two scenes:** synthetic test textures (checkerboards, gradients on simple shapes) for the technical check, and a small art scene from CC0 pieces (Kenney, Quaternius or KayKit) with low-res textures (64–128 px) added: ground, a wall, a tree or two and one character, covering near ground, distant ground and faces.
- **Four variants:** crisp; crisp with mipmaps; soft (bilinear); soft with trilinear and anisotropic filtering. Distance shimmer is crisp textures' main weakness, and mipmaps decide whether it's acceptable.
- **Judged by eye with a live toggle:** a key cycles the variants while walking around, in a desktop and a web build; screenshots and short clips are kept for the proposal. The project lead decides (`art.md`).

### ShapeLand phase 0

- **Phase 0 is done** (project lead, 2026-10-10): every 3c criterion passes and the project lead signed it off by eye under good and bad, in the web and desktop builds; the public test build (below) finishes it. The full game load test waits (Open: its thresholds). The tuning history and every run's numbers are in `.claude/drafts/Prototype Results.md`.
- **What it does:** a desktop client, a web client and bots join, see each other slide and jump smoothly and chat with bubbles, also under simulated latency; falling off the island respawns you; a cheating bot gets snapped back and logged.
- **Joining:** enter a name, pick cube, diamond or pyramid, and pick a body colour and an eye colour. Names are 1–16 letters, digits, '-', '_' and single spaces, unique online ignoring case; nothing is saved.
- **Colours come from small starting sets, with more from items later** (project lead): the join request carries the chosen colours (0xRRGGBB) and the server checks each is one the player may use, which in phase 0 means the starting sets (`ShapeLandRules.BodyColours`, `EyeColours`). The sets in code are placeholders; the project lead decides the final ones.
- **Mechanisms in Comet, the rest in ShapeLand:** Comet has the transport, replication (spawn, state, despawn), movement validation, the ground, collision and jump maths (`Comet.Simulation`) and the client logic (`Comet.Client`); ShapeLand has joining with a shape and colours, chat and its rate limit, the hard-coded blocks and the game server program. Chat moves to Comet once Anima needs it.
- **Running it:** `ShapeLand.GameServer` (on `Comet.Server`; port 5080, `/ws`; needs the content build) and `ShapeLand.Bots` (honest wanderers and cheaters that slide at 2.5× speed): `dotnet run --project shapeland/src/ShapeLand.GameServer` and `dotnet run --project shapeland/src/ShapeLand.Bots -- --count 5 --cheaters 1`. Bots stay quiet unless `--chat` is given (`tools/shapeland-web.sh --bots-chat`), so playtests with several people aren't drowned out; the Play mode tests pass it. An end-to-end test runs the server in-process with bots over real WebSockets.
- **Everyone sees everyone** (a broadcast); grid interest management and the priority accumulator (`netcode.md`) come with the full load test.
- **World:** the generated floating island from content plus a few hard-coded blocks with simple box collision on client and server, defined once in `ShapeLand.Shared`; later, a tiny zone file with a heightmap and props, generated by the content tool until the editor tools exist.
- **Actions: slide and jump.** Dodge and combat come in phase 1.
- **Falling off an island respawns you nearby:** below a kill height, the server puts the player back at their last safe ground (or the zone's spawn), with no penalty.
- **Chat: one shared channel:** everyone on the server sees every message; trimmed, up to 200 characters, a burst of 3 and then one every 2 seconds, nothing logged (`backend.md`). Messages show in a chat box and as a bubble over the shape.
- **Bots in the full load test** mostly wander, sometimes clump into crowds (the worst case for interest management) and send occasional chat lines. Behaviour is seeded, so runs repeat.

#### Server

- *Messages:* Comet's IDs are 1–63 and games number theirs from 64, so every ID stays one byte. A game passes its MessagePack resolver to the frame writer and reader (`ProtocolSerializer.CreateOptions`, `ConnectionRegistry`), so its messages work under IL2CPP too.
- *Movement checks* (`Comet.Server.Movement.MovementValidator`): speed is a distance budget (each server tick earns max speed × `SpeedFactor`, capped at `MaxBurstSeconds` of banked movement, so a burst after a stall passes but banked movement doesn't); height is limited to the shape's jump apex above the last ground stood on, plus slack; airborne reports must follow the gravity arc, and players silent in the air have their fall finished by the server (`netcode.md`); the body may not overlap a block or sit below the terrain. A snap-back or respawn carries a sequence number the client echoes, so reports already in flight are ignored instead of causing a chain of snap-backs. Violations are logged at the 1st, 10th, 100th… per player. Collision checks against meshes come with zone files.
- *Movement tolerances, set from measurements* (project lead; `MovementTolerances`): `SpeedFactor` 1.1, `JumpFactor` 1.0, `HeightSlack` 0.4 m, `AirTimeSlack` 0.2 s, `MaxBurstSeconds` 1 s (also the longest a moving player can stall without a snap-back), and 1.0 s of silence in the air before the server finishes a fall (`SilenceSeconds`). The server records how much of each tolerance every player needed (`MovementHeadroom`, logged on leave; netrun prints the honest worst), and netrun's cheaters can be subtle (`--cheat-speed`, `--cheat-jump`). Position checks can't tell a 1.1× jump from honest play, so height waits for the input replay check (`netcode.md`).
- *Entity states carry the mover's own tick, shifted by their smoothed latency* (`StateStamp.Broadcast`): the server stamps each state with the tick in the mover's report frame header, bounded to within the 0.5 s maximum lag of arrival either way and never before that player's previous stamp, then shifts it by the median of arrival minus stamp over the last 5 s (approached at no more than 5% of real time; the whole-tick shift changes only once the smoothed shift is 0.75 tick away). The spacing of a player's states stays as they sent it, so their network jitter doesn't wobble on everyone else's screen, while their steady latency doesn't count towards everyone's delay (a far player otherwise pulls everyone's up). Validation keeps the sender's own, unshifted stamps. TrinityCore (the WoW server emulator) passes on converted client times in a similar way; engines that stamp with server time (Source and the like) run everyone's movement on the server.
- *States sent to others carry the mover's velocity* (the server's own during a fall it finishes), for dead reckoning.
- *The server takes the client's address from `X-Forwarded-For`* behind the test build's proxy (Public test build).

#### Shared movement (`Comet.Simulation`)

- Ground height follows the terrain mesh's own triangles (holes included); the player motor (used by bots and the Unity client) slides, jumps, falls, walks up 0.35 m steps and slopes up to ~63°, and slides along blocks. Gameplay movement is our own kinematic code, with no physics engine (`backend.md`, open).
- *Facing turns at a rate; movement doesn't wait for it:* the motor turns facing towards the move direction at `MovementRules.TurnSpeed` (900°/s placeholder, so 180° takes 0.2 s), while speed and direction respond at once. Facing is game state (reported, replicated, later used by combat), so it lives in the motor rather than only in drawing.
- *Every shape collides as the one shared body* in `MovementRules`; looks are separate (below).

#### Client logic (`Comet.Client`)

The session, server-tick estimate, interpolation buffer and correction blend, engine-free and tested with xunit; the bots run on it, and `comet/unity` holds only the transport and Unity glue (`backend.md`, shared libraries).

- *One polled session on every platform:* the caller runs `Update(now)` on its own thread (Unity's main thread), passing the time in, so tests control the clock. Only the transports differ, behind one interface: desktop and bots use an async `ClientWebSocket` that receives on a background thread into a queue; the web uses a `.jslib` that queues frames from the browser (in the Unity package). Unity 6 web builds have no C# threads (Unity's web multithreading covers only engine code and Burst jobs, and needs COOP/COEP headers), and timers and `CancellationTokenSource` don't work there either, so code the web build runs keeps time only through `Update(now)`.
- *Server-tick estimate:* a ping every second; the lowest-RTT sample of the last ~10 is trusted (the least queueing delay), and the estimate slews toward it (running up to ~5% fast or slow), jumping only when it's off by more than ~250 ms. It gives two timelines: the server's tick now (for stamping reports) and the tick of frames arriving now (half a round trip behind); other entities are drawn the interpolation delay behind the second, so the cushion doesn't shrink as latency grows.
- *Own movement runs on the server's clock:* `FixedStep` steps on a grid (step n at n over the rate), driven by `ClientSession.ServerSeconds`, so every step of the bots (30 Hz) and every other step of the Unity client (60 Hz) falls exactly on a server tick (`TickOfStep`); only those steps are reported, stamped with their tick, so stamp and position agree. A frame carries one tick, so a report for another tick sends what's queued first. Bots wake at 60 Hz and catch up missed steps. Fractional stamps in the protocol were the alternative (project lead chose this one: they would have turned the server's tick-based rules into fractional time).
- *Adaptive interpolation delay* (`InterpolationDelay`): one delay for every other entity, so they're all drawn at the same moment. Each state that continues a move gives a sample of the delay it needed (the receive tick on arrival minus the entity's previous stamp). Every quarter second the 95th percentile of the last 2 s of samples plus one tick (never under two report intervals) gives a raw target; the target is the highest raw target of the last 5 s (`HoldSeconds`), so it rises at once for a stall but falls only 5 s after it. The delay moves towards it by running the render tick at most 4% slow (growing) or 2% fast (shrinking), so it never jumps or goes back, and keeps its target while nobody moves. Mirror (Unity) adapts its delay the same way; Source's and Gaffer on Games' fixed delays of two to three send intervals were the starting point.
- *Dead reckoning when the buffer runs dry* (project lead; `RemoteEntities`): over TCP a lost packet stalls a stream for one to two round trips, so a run-dry entity moves on along its last velocity (under gravity in the air, never below `ShapeLandWorld.GroundHeight`) for up to 0.3 s, then holds; when the late state arrives the difference eases out over 0.15 s instead of jumping. A long gap counts as standing still only after a state standing still; a state left standing from before an idle gap (200 ms) is restamped to just before the next one, so a player who starts moving doesn't glide slowly across the gap. World of Warcraft, over TCP, moves other players this way; Source extrapolates up to a quarter of a second.
- *Far jumps aren't blended:* two states further apart than the teleport speed allows (`ShapeLandWorld.TeleportSpeed`: 1.5× the fastest shape's top speed or jump speed, across the ground or upwards; falling never counts) are drawn as a jump at the second state's tick, so respawns and snap-backs of other players don't slide across the island.
- *Snap-back blend:* the simulation jumps to the correction at once (so its next reports aren't rejected again); the drawn position carries an offset that decays to zero over 150 ms (smoothstep). A respawn isn't blended; Unity fades instead.
- *Placeholders* (agent picks, not yet revisited): the sharp-turn report angle (45°), frames assumed sent mid-tick for the clock, and the 64 KB frame limit.

#### Unity client

- *GameObjects* (MonoBehaviours and prefabs); `Comet.Client` stays engine-free, so GameObjects or ECS (Entities) is revisited for Anima's client.
- *The Comet Unity package* (`comet/unity`, `com.comet.unity`) holds the browser transport and a game-agnostic driver (`CometConnection`) that owns the transport and session, updating it early each frame and flushing at the end, on Unity's unscaled real-time clock. Desktop and the editor use `Comet.Client`'s `WebSocketTransport`; web builds use the browser one, picked at compile time. The browser transport queues in JavaScript and is polled: the `.jslib` keeps each socket's received messages, and `TryReceive` asks for the next one's length, then copies it into C#. No JavaScript calls into C#.
- *Scenes:* the bare Join scene (`Assets/Scenes/Join.unity`, `JoinClient`, ShapeLand > Create Join Scene) stays as the minimal connection check; the Game scene (`Assets/Scenes/Game.unity`, ShapeLand > Create Game Scene) is the full client: `GameView` draws the island (from the heightmap at run time, using the same triangles as `TerrainGround`, holes included), the blocks and every shape (`WorldMeshes`); `LocalPlayer` moves the player's own shape; `OrbitCamera` follows it.
- *Own movement on a fixed 60 Hz step:* the client runs the shared `PlayerMotor` in fixed 1/60 s steps (as many as fit in the frame's time) and draws the shape between the last two, so movement doesn't depend on frame rate and a hitch can't tunnel through a block. 60 Hz rather than the bots' 30 adds about 17 ms of input delay on average instead of 33, for action combat. A discrete jump rises about v·dt/2 above v²/2g (0.05 m at 60 Hz, 0.1–0.14 m for 30 Hz bots).
- *Camera and controls* (placeholders, adjusted by playing): a hand-written third-person orbit camera (no Cinemachine) with its numbers in one place, keeping above the ground and block tops (it can still clip into a block's side); WASD and Space, the mouse turning the camera while the right button is held; the left stick, the right stick and the south button on a gamepad; movement relative to the camera; Enter opens chat. Bindings are defined in code (`ShapeLandControls`) rather than an input asset, so they sit in one file.
- *Player shapes are meshes generated in code* (cube, diamond as a square bipyramid, pyramid): flat-shaded with hard edges and tinted with the player's colour, chosen by each shape's `mesh` in content and sized by its `[look]` table there (project lead): width, depth, height, hover, the diamond's waist, and the eyes' size, spacing and level, checked by the content build. Shapes hover a little (`hover`, 0.15 m to start) above their feet (project lead), and two small dark blocks on the front face are eyes, showing which way a shape faces; the diamond's middle ring is square to the axes so it has a front face. The server never needs the looks. They move into a shared primitive library when the prop tool is built (monsters' curved shapes come from the same code later).
- *The respawn fade starts while falling* (project lead): a respawn arrives without warning, so the screen darkens as your own shape falls towards the kill height (from 18 m above it) and is already dark when the respawn arrives (it cuts to black if not), then fades back in over 0.4 s (`RespawnFade`, `ScreenFade`). Other players' shapes fade out and back in the same way, by height (`ShapeFade`, swapping to transparent copies of the materials made by ShapeLand > Create Game Scene), so a snap-back, made at full visibility, stays a plain jump.
- *A dev-only speed-cheat key* (2.5×, like the cheating bots) in development builds and the editor, to see a snap-back blend by eye: hold C. Release web builds don't have it.
- *UI Toolkit* for the join screen, chat box, name labels and bubbles, drafted first as HTML mockups (`ui.md`). ShapeLand's UI uses the plain look (project lead): dark see-through panels, one blue accent, Atkinson Hyperlegible (OFL); mockup `.claude/drafts/ui/shapeland-phase-0.html`. Panel settings use the mockups' 960 × 600, scaled with the screen. Name labels and bubbles are screen-space elements placed over each shape every frame, so they keep one readable size.
- *The join screen* (`JoinScreen`, `Assets/UI/JoinScreen.uxml`, styles in `ShapeLand.uss`) is a small card over a slow orbit of the island: name, shape, body colour and eye colour (plus the server address on desktop, defaulting to `localhost:5080`) and Join. Shapes and colours are picked with buttons and swatches (`ShapeIcon` vector icons), the chosen shape turning in a render-texture preview (on a layer the main camera skips), with its speed and jump height (project lead), each with a bar against the best shape; jump height is worked out from content and `MovementRules` (v²/2g), so it follows tuning. The server's refusals and connection failures show on the card, which stays up to try again on the same connection; the choices are remembered in PlayerPrefs. When the connection is lost the game says so and goes back to this screen (PR #4).
- *The HUD* (`Hud`, `Assets/UI/Hud.uxml`, its own GameObject: a UIDocument under another one is added inside that one's tree) shows the chat log in a lower corner with each speaker's shape icon, fading after 10 s without messages and coming back when one arrives or chat opens; an input that Enter opens (on release: focusing it while Enter is down left web builds ignoring typed characters) and Esc closes, with the shape held still while typing; name tags over others and a bubble over every speaker (own included) with their latest line; and the server's too-fast refusals as notes in the log.
- *A network stats overlay* (project lead picked layout B of `.claude/drafts/ui/shapeland-net-stats.html`), in every build, off until F3 (remembered): `NetStats` shows the latest and trusted round trip, the interpolation delay and its target, holds over 5 s (counted by `RemoteEntities`) and the player's own snap-backs and respawns. Each row's dot turns amber or red past a line: holds amber past 2% and red at 4%, ping amber past 250 ms, and the snap-back dot red for any honest snap-back (it ignores snap-backs within a second of the speed cheat). Numbers use Atkinson Hyperlegible Mono (OFL), since USS has no tabular figures.
- *Tests* (Play mode, starting the real game server with `dotnet run` on a free port, content built; they route all input to the game, since batch mode has no focused Game view): `JoinTests` joins from the bare scene; `GameTests` walks and jumps with a simulated keyboard with no snap-backs, sees real bots appear, move and leave, gets the speed cheat snapped back and blended, darkens the screen before a respawn, joins through the join screen (a bad name caught before sending, a taken name refused, an unreachable server reported), and covers chat, bubbles and the overlay. Other players' fade is checked only through its materials, since bots don't fall off.
- *Builds* (ShapeLand > Build): Web (Join Scene) and Web (Game Scene) to `artifacts/unity/shapeland/web-join` and `web-game`; macOS (Game Scene) and Linux (Game Scene) make development builds (so the speed cheat works) at `macos-game` and `linux-game`, running in the background when unfocused, in a resizable 1280 × 800 window. IL2CPP on Linux needs Unity's Linux sysroot packages, which are in the manifest. Web builds allow plain HTTP downloads (set in `Builds`): Unity otherwise fetches the content only from localhost, so other devices saw an empty sky.
- *The game server can serve the web build* (a dev option, `ShapeLand:WebRoot`), so one command runs both and the page connects to the host it came from; a `?server=` in the page's address names another server (as the public test build's page does). ShapeLand only: Anima's page comes from the Login server (`backend.md`). `tools/shapeland-web.sh` builds the content, makes the Unity web build in batch mode (skipped when nothing it's made from changed), runs the game server serving it, optionally starts bots (`--bots`, `--cheaters`) and opens the page; Ctrl+C stops everything. `--lan` listens on every interface and prints the machine's network address.

#### Simulated network and pass criteria (3c)

- **The network is simulated with netem in Docker** (project lead): the game server runs in a container with netem on its link, as in the stack benchmark, so every client (browser, desktop build, bots) gets real latency, jitter and loss with TCP's real resends. `tools/shapeland-web.sh --net none|good|bad|awful` runs the host-built server and bots in Docker (`shapeland/docker`, sharing `tools/docker/netem.sh` with the benchmark); `--bots-net` adds a profile on top for the bots alone. Profiles, each way: good 40 ± 10 ms and 1% loss (about 80 ms round trip), bad 100 ± 25 ms and 2%, awful 100 ± 25 ms and 5%. Desktop builds join it on the same port. Docker on macOS is untried.
- **Headless runs** (`tools/shapeland-netrun.sh PROFILE`, default 180 s, 10 honest bots, 1 cheater, seed 1, 10 s warmup): the server in Docker under the profile, the bots on the host through its published port (as a browser), or in their own container with `--bots-net`; `--far N` adds N honest "Far" bots in their own container under bad on top (about 300 ms). The bots record the overlay's numbers (`--warmup`, `--results`, `NetReport`), leaving cheaters out of the smoothness counts; results and the server's log (with timestamps) go to `artifacts/netruns/`, stamped with profile, commit and the client's tuning settings. The server logs each fall it finishes and each player's violation and fall counts on leaving, which the script totals. The report counts visible hitches: a jump, a blend-back over 0.2 m, or dead reckoning running out. Bot pings read about 16 ms high (a bot sees the pong on its next 60 Hz wake).
- **The pass criteria**, written down before tuning and judged on one run per profile:
  1. *Fairness* (none, good, bad): no snap-backs on honest bots, of either kind (rule violations or falls the server finishes).
  2. *Cheaters* (every profile): every cheater caught.
  3. *Smoothness* (good, bad): holds at most 2% of moves, and at most 3 visible hitches per other player per minute under bad and 2 under good.
  4. *Steadiness* (good, bad): the target moves at most 30 ms/s on average.
  5. *Lag* (good, bad): delay median at most 300 ms under good and 550 ms under bad.
  6. *Awful* is reported, not required to pass: it shows how the game degrades.
  7. *By eye:* the project lead's sign-off in the browser and the desktop build under good and bad.
- **Final runs** (commit `1126554`; 180 s, 10 honest bots, 1 cheater, seed 1; far runs add far players under bad on top of good): every criterion passes under none, good and bad.

  | Run | Honest snap-backs | Cheater caught | Ping median | Delay median (5th–95th) | Target moving | Holds | Visible hitches per other player per minute |
  | --- | --- | --- | --- | --- | --- | --- | --- |
  | none | 0 | yes | 16 ms | 158 ms (137–173) | 2 ms/s | 0.0% | 0.0 |
  | good | 0 | yes | 96 ms | 239 ms (189–286) | 8 ms/s | 1.2% | 1.4 |
  | bad | 0 | yes | 224 ms | 453 ms (397–623) | 16 ms/s | 1.3% | 2.5 |
  | awful (reported only) | 7 (3 falls, 4 violations) | yes | 287 ms | 619 ms (501–810) | 27 ms/s | 2.0% | 4.6 |
  | good, 1 far of 12 | 0 | yes | 96 ms | 243 ms (194–294) | 8 ms/s | 1.4% | 2.5 |
  | good, 3 far of 14 | 1 (a fall) | yes | 96 ms | 253 ms (204–304) | 8 ms/s | 1.8% | 4.6 |

### Public test build

ShapeLand can be played at https://shapeland.lucyawrey.com without downloads (project lead): the page on Vercel, the game server on an on-demand AWS instance. Everything runs from `tools/shapeland-testbuild.sh` on the project lead's machine; its header lists the commands and options.

- **Where things are:** the page on Vercel (project `shapeland`, free plan; `shapeland/deploy/vercel.json` sends `Content-Encoding: br` for the Brotli-compressed build); the game server at `wss://server.shapeland.lucyawrey.com/ws` on a `t4g.micro` (ARM) in the AWS CLI's region, with Caddy in front for TLS (`shapeland/deploy/Caddyfile`, `boot.sh`). The server's name is an A record in Vercel DNS, rewritten on each `up`. Caddy's certificate is kept in S3 between runs, since Let's Encrypt allows about 5 a week per hostname. The page is never served by the game server.
- **Once per machine:** log in to the AWS CLI (`aws login`) and the Vercel CLI (`vercel login`); the first run creates `~/.config/shapeland-testbuild/env` for `ACME_EMAIL`, the certificate's contact address. **Once per AWS account:** `setup` (the S3 bucket and firewall rules).
- **A session:** `up` builds the server and starts it (about 20 cents a day; `--hours H` sets when it shuts itself down, 24 by default, 0 for never); `status` says what's running and whether it answers; `log` prints its boot log; `down` terminates it. `page` builds the web build and publishes it (`--no-build` publishes the last one); it only needs rerunning when the client changes.
- **The Vercel project has no Git connection:** the page holds a local Unity web build that isn't in the repo, so builds from GitHub pushes would deploy an empty site to production and take the domain. Pages are published only by `page`.
- **While the server is down,** the page's card says ShapeLand isn't open for testing right now, and checks again on request.
- **`down` parks the name at 192.0.2.1** rather than removing it: the domain's wildcard record would answer for a removed name, and resolvers keep that answer for 30 minutes, past the next `up`. A record under `shapeland` (such as `server.shapeland`) stops the wildcard covering `shapeland` itself, so the page's domain has its own record.
- **The server takes the client's address from `X-Forwarded-For`,** trusting only Caddy, so players don't all share the proxy's address and its per-address connection cap. `/health` answers `ok`; the page and `status` check it.
- **The AWS account is on the Free Plan:** upgrade it to the Paid Plan (credits carry over) before its 6-month limit, or it closes. Not a concern yet (project lead).
- **The server may stay up for long stretches** (project lead): it's cheap, and people want to try it.
- **Agent sessions:** the sandbox blocks AWS, Vercel and DNS calls, so the script runs outside it.

## Considering

- **Per-shape collision bodies** (later, if drawn sizes drift far from the shared body): each shape's body radius and height from content, used by the client motor, the server's validation and the bots, so what's drawn is what collides. For now every shape collides as `MovementRules`' one body (project lead).
- **A heavy-heap variant of the stack benchmark** (agent suggestion; later, before the stage 2 load test, not part of milestone 1): the server also holds synthetic long-lived zone state, about 100–300 MB, in a mix of plain struct arrays and ordinary object graphs, so we can measure how deep GC pauses scale with a real zone's state, which the bench as it stands can't show. It decides whether the GC is a real risk for zone servers: switching away from C# would only be worth weighing if blocking collections recur in steady state and approach the 33 ms tick budget even after per-tick allocation is reduced.
- **A connection slot pool in Comet** (agent suggestion; later, with the heavy-heap variant, not part of milestone 1): a zone server allocates per-connection state and buffers up front at startup, up to the channel's hard cap, so players joining reuse long-lived objects instead of creating about 22 KB each to promote. First measure what that state is (heap dumps idle and with 300 bots, by type), since Kestrel's and the WebSocket's share can't be pooled.

## Rejected

- **A Unity 7 alpha side test after 2b:** the alpha (7000.0.0a7) still targets .NET Standard and C# 9 (project lead), so it couldn't yet show `net10.0`-only shared packages; revisit when Unity 7 matures.
- **One generic content tool that loads a game's assembly by reflection** (instead of a small console project per game): run-time assembly loading and version conflicts, a two-step build, errors at run time instead of compile time, and the build serializing through a different MessagePack path than the client.
- **A single `src/` tree** for all code: per-layer folders keep Comet and each game clearly apart.
- **Unity projects under `src/`, or renamed to the `Comet.X` / `ShapeLand.X` pattern:** `src/` holds the .NET projects in `ProjectComet.slnx`, and Unity's generated `.csproj` files share names with them; the assemblies inside already follow the pattern.
- **The full content pipeline in phase 0** (hash delivery, text extraction, drift checks, rename command): phase 1.
- **Login and Region servers in phase 0:** they come in phase 1, so phase 0 reaches the load test sooner.
- **A heap settle after startup** (one full, compacting collection before players arrive): it made the join pause worse in every AWS run, turning a gen1 into a gen2.
- **`SustainedLowLatency` mode and a slower bot ramp as fixes for the join pause:** neither brought it under 10 ms.

## Open

- **Revisit server choices made while clients didn't extrapolate** (project lead), as its own step, measuring stalls first (the netrun logs' timestamps). The code review found server-finished falls still needed: dead reckoning covers silences up to 0.3 s and the server's fall starts at 1 s, and it's what stops a silent hover (the arc check only catches reports above the arc) and keeps the server's own position honest for new viewers and later hit checks. Its cost is a snap-back for an honest player stalled over 1 s in the air. **Considering** (the reviewing agent's suggestion): viewers dead-reckon an airborne entity until it lands (gravity, never below the ground) instead of stopping at 0.3 s; raise `SilenceSeconds` above measured TCP stalls (about 2 s); optionally, answer position questions about a silent player (hits, zones) from the arc meanwhile. Also review then: lagging stamps add about 0.5 s to the hover allowance (`AirTimeSlack`), and the server fall's upward cap times the arc from the arrival tick rather than the stamp, which is stricter by the player's latency.
- **Full game load test thresholds** (stage 2), written down before it runs.
- **Whether a newer NuGetForUnity CLI writes current `.meta` files** (project lead: check later); if so, `tools/unity-restore.sh` can drop its rewrite.
