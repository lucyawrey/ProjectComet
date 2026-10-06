# Combat Core

Every game on Comet shares the same combat rules underneath: action combat where attacks have real shapes in the world, with timing that is consistent regardless of animation. How a game builds classes and skills on top is its own business (see [Comet Overview](Comet%20Overview.md)).

## Hit checks

Whether an attack hits is one shared check with two independent parts:

- **Height zones.** Characters occupy vertical zones: digging, crouching, standing, jumping, flying. Each attack hits certain zones. A sweep along the ground hits crouching and standing characters but misses a jumping one; an overhead swing can be ducked by crouching. A real height difference between attacker and defender (a ledge, a slope) shifts the check up or down. Flat arenas can switch the height adjustment off.
- **Invulnerability.** Defenders have an invulnerability level (none, dodging, or a rarer "true" invulnerability); attacks have a pierce level. Every character can dodge, but some attacks pierce normal dodges.

Both are simple integer and bitmask comparisons, cheap enough for a server running hundreds of characters.

## Frame data

Actions are timed in simulation ticks (30 a second) with explicit startup, active and recovery frames. Animations play alongside the frame data but don't drive it: consistent timing matters more than a perfect visual match. Movement skills and knockback are fixed curves in the same data, so the client can predict them.

## The skill base

**Player hitboxes are small and identical:** for attacks, a point at the player's centre, or a circle much smaller than the model, the same for every player whatever they look like. Moving through the world uses a separate collider, closer to a normal one with a wider base, since it decides how players meet walls and land on platforms. It's also the same for everyone (to start, a flat-based cylinder about 0.8 m wide and 1.7 m tall), so every jump and passage works the same for every player.

Comet only knows how to **execute** an action: its frame data, hitboxes, cooldowns and effects. How players learn skills, which slots they go in and how they improve is game code. That keeps Comet open to games with very different progression, from rune slots to traditional skill trees.

## Attack patterns

Bosses use mostly telegraphed swings and area shapes, with occasional moderate projectile patterns for raids. Patterns are sent as events, not one message per projectile, and projectile counts are capped to keep bandwidth in check.

## Scale and feel

- **Fight sizes:** dedicated raids up to about 30 players; open-world bosses aim for about 100 at worst.
- **PvE only.** No PvP system is planned for the first major version, so the netcode can be generous to players (see [Netcode](Netcode.md)).
- **No body blocking.** Characters pass through each other; "nudging" (moving slower through bodies) is under consideration.
- **Most attacks don't move anyone**, which keeps some of the readability of tab-target games; a limited set of skills include movement.
- **Physicality without physics:** things should feel solid and weighty without simulated physics.

## Open questions

- Whether to add nudging.
