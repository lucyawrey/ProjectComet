# Architecture

<!-- Agent scaffolding: a checklist of what this page should cover, with links to the design notes. Delete items as you write. -->

- [ ] Login server, Region server, game servers ([`backend.md`](../../.claude/design/backend.md))
- [ ] Account database and region databases; conventions ([`backend.md`](../../.claude/design/backend.md))
- [ ] Shared libraries: Protocol, Content, Simulation, Data ([`backend.md`](../../.claude/design/backend.md))
- [ ] Game-specific data: combined EF Core model, side tables, registry keys ([`backend.md`](../../.claude/design/backend.md))
- [ ] Monorepo layout: Comet package, one Unity project per game, per-game server programs ([`proposal.md`](../../.claude/design/proposal.md))
- [ ] Persistence: immediate transactions vs periodic saves ([`backend.md`](../../.claude/design/backend.md))
- [ ] Draft tables (the project lead decides the final schema) ([`database.md`](../../.claude/design/database.md))
