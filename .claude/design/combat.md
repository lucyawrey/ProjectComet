# Combat

**Layer: base (Comet).** Hit checks, height zones, invulnerability, frame data and fight sizes are shared by every game on the base.

Agent notes on combat design.

## Decided

- **Feel:** action combat that feels a little like ARPGs and a little like tab-target games, but is extremely approachable for casual players. Optional lock-on.
- **Simple to understand, hard to master,** with **complex boss mechanics** like FFXIV's and Rabbit and Steel's (raid mechanics without tab-targeting, using shape-based collision). High-end raiding is part of the audience.
- **PvE only for the first major version.** No PvP system is planned, so no PvP netcode.
- **Every class can dodge, but invulnerability varies by class and skill.** Defenders have invulnerability levels (none / dodge / a rarer "true" invuln); attacks have pierce levels, and some attacks hit through normal invuln.
- **Height zones are separate from invulnerability.** Zones (digging, crouching, standing, jumping, flying) are vertical position; invulnerability is defensive state. Both feed one shared "is this hit valid?" check.
- **Real height check:** height thresholds between attacker and defender add or subtract an integer offset to the zone check. Can be disabled for flat areas like boss arenas as an optimisation. Where placed meshes are walkable, ground height comes from collision meshes as well as the heightmap (`backend.md`, zone format).
- **Frame data in 30 Hz simulation ticks:** explicit startup, active and recovery frames, run in parallel with (not driven by) animations. Consistent timing matters more than visual match.
- **Thin skill base:** Comet defines only what executes an action (frame data, hitboxes, cooldowns, effects). How skills are acquired, slotted and advanced is game code (`skills.md` for Project Anima).
- **Attack patterns: mostly telegraphs, some bullets:** mostly telegraphed swings and AoE shapes, with occasional moderate projectile patterns for raids. Bullet counts are capped for bandwidth, and patterns sync as events, not per-projectile state.
- **Fight sizes:** dedicated raids up to ~30 players; open-world bosses aim for ~100 in the worst case (revisit if not feasible).
- **Player hitboxes are small and identical:** for attacks, a point at the player's centre, or a circle much smaller than the model, the same for every ancestry and class. ShapeLand follows this.
  - **World collision is separate:** for moving through the world (terrain, walls, objects), players use a collider closer to a normal one, with a wider base. It matters for walls, travel and platforming (`world.md`).
    - **The same for everyone,** whatever the ancestry, class or model size, so jumps, gaps and passages work identically for every player.
    - **Starting size, tuned in the prototype:** an upright cylinder with a flat base, about 0.8 m wide and 1.7 m tall (standing). The flat base lets players stand on ledge edges instead of sliding off as a rounded capsule would. Level design keeps low ceilings above the tallest model so nothing visibly clips.
- **No body blocking** in the initial design.
- **Most attacks don't move anyone**, to keep some tab-target feel; a limited set of skills include movement.
- **Physicality without physics** is one of the game's largest goals, despite the simple graphics.

## Considering

- **Nudging:** moving slower through other bodies (`netcode.md` has how it would sync).

## Open

- Nothing open right now.
