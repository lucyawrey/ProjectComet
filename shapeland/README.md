# ShapeLand

Comet's demo and reference game. Requirements, setup and the scripts are in the [repository README](../README.md).

| Folder | What it holds |
| --- | --- |
| `content/` | Game data: the shapes and the test zone's island, compiled to `content.bin` by `src/ShapeLand.ContentBuild`. |
| `src/` | The game server, the bots, the content build and `ShapeLand.Shared` (rules, world and messages used by server, bots and client). |
| `ShapeLand.Unity/` | The Unity client. |
| `tests/` | Tests for the .NET projects. |
| `docker/`, `deploy/` | Simulated-network runs and the public test build. |

## Tuning movement

Every movement number has one home. Clients, bots and the server read the same values, and the server checks each player's movement against them, so a change applies everywhere once everything is rebuilt.

### Each shape: `content/shapes/<shape>.toml`

| Key | What it does |
| --- | --- |
| `max_speed` | Top ground speed, in meters per second. |
| `jump_velocity` | Upward speed at take-off, in meters per second. A jump rises v² / 2g: with gravity 25, 8.5 rises about 1.45 m. |
| `scale` | How big the shape is drawn, times its look in `WorldMeshes.Looks`. Looks only: collisions use the shared body below. |

### Physics for every shape: `Rules` in `src/ShapeLand.Shared/World/ShapeLandWorld.cs`

| Value | What it does |
| --- | --- |
| `Gravity` | Downward acceleration, in m/s². Higher makes jumps shorter and snappier at the same take-off speed. |
| `GroundAcceleration`, `AirAcceleration` | How quickly speed changes toward what the stick asks for, on the ground and in the air, in m/s². Low feels slippery, high feels instant. |
| `TurnSpeed` | How fast a shape turns to face where it's going, in degrees per second. Only the facing turns; movement changes direction at once. |
| `StepHeight` | The highest ledge walked up without jumping, in meters. |
| `GroundSnap` | How far the ground may drop under the feet while staying grounded (walking downhill rather than falling), in meters. |
| `BodyRadius`, `BodyHeight` | The collision body every shape shares: a square footprint `BodyRadius` from the center, and `BodyHeight` tall, in meters. |

### How shapes look: `Looks` in `ShapeLand.Unity/Assets/Scripts/WorldMeshes.cs`

Width, depth and height of each mesh, how far it hovers above its feet, the diamond's waist height, and the eyes' size, spacing and height, all in meters at scale 1. These change the drawing only.

### The camera: the Camera object in the Game scene

`OrbitCamera` holds its distance, look height, turning speeds, pitch limits and how far it keeps from the ground and blocks. Change them in the inspector: the values saved in `Assets/Scenes/Game.unity` win over the defaults in `OrbitCamera.cs`.

### Server leniency: `MovementTolerances` in `comet/src/Comet.Server/Movement/MovementValidator.cs`

How much faster or higher than the rules a report may be before the server snaps the player back. They're tuned for bad networks rather than feel; `MovementHeadroom` and the bad-profile runs (`tools/shapeland-netrun.sh`) show how much room honest players need. The checks follow the shape's speed and jump and `Gravity`, so tuning movement doesn't need changes here.

### Trying a change

- **In a browser:** `tools/shapeland-dev.sh` rebuilds the content and the server on every run, and the web build whenever the client's code changed.
- **In the editor (fastest):** `tools/shapeland-dev.sh --no-web` builds the content and starts the server on `localhost:5080` (add `--bots N` for company). After each change, rerun it and restart play mode, which copies in the new content as it starts: the server and play mode each load the numbers once. If they still differ, the join screen says so.
- **Play-mode tests** read the same copy, refreshed when they start; rebuild the content first.
