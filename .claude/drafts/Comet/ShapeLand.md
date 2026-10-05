# ShapeLand

ShapeLand is Comet's demo game: a tiny MMO where players are coloured shapes in a toy-box world, sliding around, chatting and fighting shape monsters with magic. It builds and tests Comet during the first phases, before Project Anima's own systems exist, and stays afterwards as the reference game for anyone building on Comet or testing a private server.

It needs no art assets or animation, so the early phases don't wait on art.

## Players

Players choose one of three faceted shapes, and each shape is a class with its own hitbox and spells:

| Shape | Body | Plays |
| --- | --- | --- |
| **Cube** | Wide and sturdy | Short range |
| **Diamond** (a tall, pointed bipyramid) | Tall, thin and fast | Mid range |
| **Pyramid** | Low and wide | Ranged |

Each has a projectile, an area spell, a dash and one signature spell. Everyone can jump and dodge, so height zones and invulnerability get a real workout. All combat is magic because melee animations would look awkward on shapes.

## Monsters

Monsters are curved shapes (spheres, capsules, cylinders and rings) so they never look like players. They have a few attack patterns each, and one big boss serves the 100-player boss load test. Like Comet, ShapeLand is PvE only.

## Look

A playful toy box: bright primary colours and simple props.

## Growing with the phases

| Phase | ShapeLand adds | Comet gets tested on |
| --- | --- | --- |
| 0. Prototypes | Sliding and chat | Connections, movement, chat |
| 1. Vertical slice | Combat, paint drops that recolour your shape, XP and levels | Combat core, items and the ledger, trading, progression, persistence, 100+ bots |
| 2. Seamless-world proof | Several zones, a border, channels, a dungeon, a teleport | Handoff, channels, instances, streaming |

## Staying small

ShapeLand is a test fixture that happens to be playable. Every system it adopts is something to maintain, so it only grows when Comet needs testing. Where Project Anima has a rich system and ShapeLand needs a simple one (mounts, equipment), ShapeLand gets its own trivial version rather than a shared abstraction.

## Open questions

- The exact spells for each shape.
