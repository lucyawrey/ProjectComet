# Combat

**Layer: base (Comet).** Hit checks, height zones, invulnerability, frame data and fight sizes are shared by every game on the base.

Agent notes on combat design. Decisions made with the project lead on 2026-10-03.

## Decided

- **Feel** (project lead, 2026-10-04): action combat should feel a little like ARPGs and a little like tab-target games, but be extremely approachable for casual players.
- **Simple to understand, hard to master** (project lead, 2026-10-04), with **complex boss mechanics** like FFXIV's and Rabbit and Steel's (FFXIV-inspired raid mechanics without tab-targeting, using shape-based collision as we do). High-end raiding is part of the audience.

- **PvE only for the first major version.** No PvP system is planned, so no PvP netcode.
- **Every class can dodge, but invulnerability varies by class and skill.** Defenders have invulnerability levels (none / dodge / a rarer "true" invuln); attacks have pierce levels, and some attacks hit through normal invuln.
- **Height zones are separate from invulnerability.** Zones (digging, crouching, standing, jumping, flying) are vertical position; invulnerability is defensive state. Both feed one shared "is this hit valid?" check.
- **Real height check:** height thresholds between attacker and defender add or subtract an integer offset to the zone check. Can be disabled for flat areas like boss arenas as an optimization.
- **Frame data in 30 Hz simulation ticks:** explicit startup, active and recovery frames, run in parallel with (not driven by) animations. Consistent timing matters more than visual match.
- **Fight sizes:** dedicated raids up to ~30 players; open-world bosses aim for ~100 in the worst case (revisit if not feasible).
- **No body blocking** in the initial design. "Nudging" (moving slower through other bodies) is under consideration.
- **Most attacks don't move anyone**, to keep some tab-target feel; a limited set of skills include movement.
- **Attack patterns: mostly telegraphs, some bullets** (2026-10-05, adopted from an agent suggestion): mostly telegraphed swings and AoE shapes, with occasional moderate projectile patterns for raids. Bullet counts are capped for bandwidth, and patterns sync as events, not per-projectile state.
- **Physicality without physics** is one of the game's largest goals, despite the simple graphics.

## Open

- Nothing open right now.
