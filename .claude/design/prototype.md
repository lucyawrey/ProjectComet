# Prototype

**Layer: base (Comet),** with ShapeLand as its game.

Agent notes on the early prototype of Comet and ShapeLand. The roadmap phases are in `proposal.md`; the load test design is in `backend.md`.

## Decided

- **Prototype early,** before the human-readable proposal is done. Not started yet; planning comes first.
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
- **First milestone: the stack benchmark passes** its thresholds, written down before the run. A failure tells us early that the stack or language is the problem.
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

- **Toolchain:** .NET 10 (LTS) for servers and tools; the newest supported Unity 6 release when work starts, moving to Unity 6.8 (CoreCLR, .NET 10) once it's out and web builds are confirmed working (`backend.md`, research).
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
