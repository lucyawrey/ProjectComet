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

## Considering

- Nothing being considered right now.

## Rejected

- **Going past phase 0 in the early prototype** (combat, loot, levels): left for phase 1.

## Open

- **Repository layout** for code (where Comet, ShapeLand and tools live on `main`).
- **Toolchain versions:** Unity version and .NET version.
- **Load-test thresholds,** written down before the first run (`backend.md`).
- **Where the load test runs:** a local machine, a cloud VM, or both.
- **Order of work** and the first milestone.
- **ShapeLand phase 0 details:** which servers it needs (Login, Region, game server), whether chat goes through SignalR from the start, and whether the web build is tested in phase 0.
