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

## Considering

- Nothing being considered right now.

## Rejected

- **Going past phase 0 in the early prototype** (combat, loot, levels): left for phase 1.
- **Login and Region servers in phase 0:** they come in phase 1, so phase 0 reaches the load test sooner.

## Open

- **Repository layout** for code (where Comet, ShapeLand and tools live on `main`).
- **Toolchain versions:** Unity version and .NET version.
- **Load-test thresholds,** written down before the first run (`backend.md`).
- **Where the load test runs:** a local machine, a cloud VM, or both.
