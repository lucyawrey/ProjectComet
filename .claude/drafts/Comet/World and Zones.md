# World and Zones

The seamless world is Comet's firmest requirement and its biggest challenge. Players walk from one area to the next with no loading screen; only teleports hide loading, behind a short travel scene.

## Structure

- A **region** is one copy of the world (see [Architecture](Architecture.md)).
- A **zone** is one area of that world, stored as a zone file. Zones come in three kinds: open zones, dungeons and houses.
- An **instance** is a running copy of a zone on a game server:
  - a **channel** is a copy of an open zone shared by many players (150–300 at most);
  - a **dungeon instance** belongs to one party;
  - a **house instance** belongs to one owner (guild halls too).

In code these are one concept: an instance with a policy for who may enter, how it's placed and how long it lives. Instances are cheap to create and dispose of, and one game server runs many of them.

## Crossing borders

Open zones meet at borders, which can be wide (a valley between mountains) but always join exactly two zones. Near a border, the client also listens to the neighbouring zone's game server and sees it read-only, so the next zone is already there when you arrive. A few metres past the line, the character is handed over:

1. The new game server has already loaded the character from the database while the player approached.
2. At the crossing, ownership moves to the new server in one atomic step.
3. Only the live state (cooldowns, buffs and their timers) travels between servers. Items and currency never travel; they're already saved.

A skill in progress delays the handoff until it ends. Attacks and effects never cross a border, and monsters stay in their own zone, so borders are designed with little or no combat. Flying can cross borders, and boats and airships on fixed routes carry their passengers across as one group.

Many zones need no border at all: dungeon entrances, houses and some areas are reached only through a door or a teleport.

## Channels

When a zone gets busy, it runs in several channels. Comet handles:

- **Placement** with a preference policy the game supplies (who you'd like to be placed with).
- **Soft and hard caps:** new arrivals fill a channel only to about 80%; the headroom is for party members and travellers crossing a border.
- **Pairing across borders:** crossing into the next zone prefers the channel most nearby travellers are heading to, so groups stay together.
- **Draining and merges:** a quiet channel stops taking new players and empties naturally; remaining players are moved only if it stays nearly empty. Items on the ground move with a merge.

## Teleports and instances

A teleport reuses the border handoff. When the cast starts, the destination loads in the background; when it finishes, ownership moves behind a short travel scene, which lasts a fixed time even if loading finishes early. Dungeon entrances work like small borders into an instance, with a loading room where the party gathers while the dungeon streams in.

What players see around these mechanisms (the travel scene, loading rooms, re-entry rules) is up to each game; see [World and Travel](../Project%20Anima/World%20and%20Travel.md) for Project Anima's.

## Zone format

Zones use our own engine-neutral format rather than Unity scenes. Zone files in git are the source of truth: the server loads them directly, the client builds the world from them at runtime, and Unity is the editor through custom tools.

- **Chunks.** Each region has a fixed world grid (around 64 m chunks, tuned in the prototype) and open zones own whole chunks, so borders follow chunk edges. Dungeons and houses use their own local grid and are often a single chunk. Chunks let the web client stream what's near, keep diffs and merges small, and speed up the editor and builds.
- **Terrain:** a coarse heightmap per chunk, turned into a flat-shaded low-poly mesh with painted vertex colours. Cliffs, arches, overhangs and cave mouths come from a modular rock kit placed in the editor. Where placed meshes are walkable (bridges, overhangs), ground-height checks use collision meshes as well as the heightmap.
- **Lighting:** a realtime sun and ambient light with vertex colours and simple shadows; the build bakes ambient occlusion into vertex colours. No lightmaps, which leaves room for a day/night cycle.
- **Files:** placements, spawns, borders and collision volumes are text (TOML) per chunk, so they diff and merge well; heightmaps and colour maps are small binary images. The build compiles everything to a compact binary format.
- **Distance:** the build generates distant versions of each chunk automatically, merging groups of chunks into coarser levels further away; artists can hand-make a landmark's far version. Outdoor zones can show other zones' scenery in the distance, including dungeons dressed to look like open world.
- **Collision:** each asset ships a simple collision mesh; terrain collision comes from the heightmap; a few hand-placed volumes cover special cases.
- **First tools:** open and save chunks, sculpt and paint terrain, place and snap assets, mark spawns, borders and entrances.

## Open questions

- View distance, set from web performance measurements in the seamless-world prototype.
- Whether the rock kit is enough, or editor-generated shapes are needed later.
