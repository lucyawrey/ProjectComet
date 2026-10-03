# Combat

Agent notes on combat design. Decisions made with the project lead on 2026-10-03.

## Decided

- **PvE only for the first major version.** No PvP system is planned, so no PvP netcode.
- **Every class can dodge, but invulnerability varies by class and skill.** Defenders have invulnerability levels (none / dodge / a rarer "true" invuln); attacks have pierce levels, and some attacks hit through normal invuln.
- **Height zones are separate from invulnerability.** Zones (digging, crouching, standing, jumping, flying) are vertical position; invulnerability is defensive state. Both feed one shared "is this hit valid?" check.
- **Real height check:** height thresholds between attacker and defender add or subtract an integer offset to the zone check. Can be disabled for flat areas like boss arenas as an optimization.
- **Frame data in 30 Hz simulation ticks:** explicit startup, active and recovery frames, run in parallel with (not driven by) animations. Consistent timing matters more than visual match.
- **Fight sizes:** dedicated raids up to ~30 players; open-world bosses aim for ~100 in the worst case (revisit if not feasible).
- **No body blocking** in the initial design. "Nudging" (moving slower through other bodies) is under consideration.
- **Most attacks don't move anyone**, to keep some tab-target feel; a limited set of skills include movement.
- **Physicality without physics** is one of the game's largest goals, despite the simple graphics.

## Open

- How dense can attack patterns get (Monster Hunter-style telegraphed swings vs Rabbit and Steel-style bullet patterns)? Affects projectile counts and bandwidth.
