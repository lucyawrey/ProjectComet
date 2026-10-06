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
- **Where the load test runs:** iterate locally; official runs use a cloud VM for the server and a separate machine for the bots, so bots don't take the server's CPU and the network is real.

## Considering

- Nothing being considered right now.

## Rejected

- **Going past phase 0 in the early prototype** (combat, loot, levels): left for phase 1.
- **A single `src/` tree** for all code: per-layer folders keep Comet and each game clearly apart.
- **Login and Region servers in phase 0:** they come in phase 1, so phase 0 reaches the load test sooner.

## Open

- **Full game load test thresholds** (stage 2), written down before it runs.
