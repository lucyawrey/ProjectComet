# ShapeLand

Comet's demo and reference game. Requirements, setup and the scripts are in the [repository README](../README.md).

| Folder | What it holds |
| --- | --- |
| `art/` | Source files for the art (such as `.blend`), plain in git (no Git LFS yet). Exports go into `ShapeLand.Unity/Assets/Models/` as `.glb`. |
| `content/` | Game data: the shapes and the test zone's island, compiled to `content.bin` by `src/ShapeLand.ContentBuild`. |
| `src/` | The game server, the bots, the content build and `ShapeLand.Shared` (rules, world and messages used by server, bots and client). |
| `ShapeLand.Unity/` | The Unity client. |
| `tests/` | Tests for the .NET projects. |
| `docker/`, `deploy/` | Simulated-network runs and the public test build. |

## Code

How Comet's libraries fit together, and how a move travels from one player to the others, is in [Comet's README](../comet/README.md). ShapeLand adds its rules, content and screens on top.

### Shared: `src/ShapeLand.Shared`

Used by the server, the bots and the Unity client alike: `World/ShapeLandRules.cs` (tick rate, name and chat rules, the color sets), `World/ShapeLandWorld.cs` (the movement rules and the test island's collision world), `Content/` (the shape type and the compiled content file) and `Messages/` (ShapeLand's own messages: joining, spawns and chat).

### Server: `src/ShapeLand.GameServer`

`ShapeLandServer` sets up ASP.NET Core and the tick loop; `ShapeLandGame` is the game: joining and changing looks, movement checks, falls the server finishes, chat, and who sees whom. `Player` holds one connection's state. Messages arrive on each connection's own flow and are queued; the tick thread handles them in order, so the game's state belongs to it alone.

### Unity client: `ShapeLand.Unity`

The Game scene (`Assets/Scenes/Game.unity`) holds:

| Object | Components | What they do |
| --- | --- | --- |
| Game | `CometConnection`, `JoinClient`, `GameView`, `JoinScreen` | Connecting and joining (`JoinClient`); drawing the island, blocks and players (`GameView`); the join card for name, shape and colors (`JoinScreen`, `Assets/UI/JoinScreen.uxml`). |
| Hud | `Hud`, `Chat`, `NameTags`, `NetStats` | The in-game overlay (`Assets/UI/Hud.uxml`): the Change Look button and respawn fade, the chat box, name tags and speech bubbles, and the network numbers (F3). |
| Camera | `OrbitCamera` | The third-person camera. |

Other pieces in `Assets/Scripts`: `LocalPlayer` (the player's own movement, on a fixed step, and its reports), `ShapeBody` (a drawn shape's colors and fading, on each shape's prefab in `Assets/Prefabs/Shapes/`), `ShapePreview` (the join card's turning shape, `Assets/Prefabs/ShapePreview.prefab`), `WorldMeshes` (the island and blocks, built from the same data the server uses), `RespawnFade` (how falling fades), `UiText` (fills UXML text from the string file) and `ShapeLandControls` (generated from the bindings in `Assets/ShapeLandControls.inputactions`; edit them in the Input System's window).

The editor scripts in `Assets/Editor` add the ShapeLand menu: builds (`Builds`) and copying the content in (`ContentSync`, also done as play mode starts)

The play-mode tests (`Assets/Tests`) load the Game scene against a real game server and join through the card: `JoinScreenTests`, `ChatTests` and `PlayTests`. `ClientRulesTests` need no server, and also check that every interface text key the scripts, UXML and web page use exists.

### Interface text: `content/strings/en.toml`

Every label and message players see, by key (`[join]` then `name_taken` is `join.name_taken`), with `{name}` placeholders. UXML labels hold `#key`, scripts look keys up in the content's string table, and the web page's elements carry `data-text` keys, filled in when the web build is made. Servers send codes, never text.

### Bots and the content build

`src/ShapeLand.Bots` runs headless players (honest wanderers, optional cheaters) for load and network runs. `src/ShapeLand.ContentBuild` compiles `content/` into `artifacts/content/shapeland/content.bin`.

## Tuning movement

Every movement number has one home. Clients, bots and the server read the same values, and the server checks each player's movement against them, so a change applies everywhere once everything is rebuilt.

### Each shape: `content/shapes/<shape>.toml`

| Key | What it does |
| --- | --- |
| `max_speed` | Top ground speed, in meters per second. |
| `jump_velocity` | Upward speed at take-off, in meters per second. A jump rises v² / 2g: with gravity 25, 8.5 rises about 1.45 m. |
| `scale` | How big the shape is drawn, times its model's size. Looks only: collisions use the shared body below. |

### Physics for every shape: `Rules` in `src/ShapeLand.Shared/World/ShapeLandWorld.cs`

| Value | What it does |
| --- | --- |
| `Gravity` | Downward acceleration, in m/s². Higher makes jumps shorter and snappier at the same take-off speed. |
| `GroundAcceleration`, `AirAcceleration` | How quickly speed changes toward what the stick asks for, on the ground and in the air, in m/s². Low feels slippery, high feels instant. |
| `TurnSpeed` | How fast a shape turns to face where it's going, in degrees per second. Only the facing turns; movement changes direction at once. |
| `StepHeight` | The highest ledge walked up without jumping, in meters. |
| `GroundSnap` | How far the ground may drop under the feet while staying grounded (walking downhill rather than falling), in meters. |
| `BodyRadius`, `BodyHeight` | The collision body every shape shares: a square footprint `BodyRadius` from the center, and `BodyHeight` tall, in meters. |

### How shapes look: the models in `art/shapes.blend`

Each shape is an empty at the player's feet with a `Body` mesh and `Eyes` under it, in meters, facing -Y in Blender, toward the Front view (+Z in Unity). Export each one as `ShapeLand.Unity/Assets/Models/Shapes/<Shape>.glb` (glTF Binary, selected objects only, with the empty at the origin); the prefabs in `Assets/Prefabs/Shapes/` pick up the change. The prefabs set the materials, so the models' own are ignored. These change the drawing only.

### The camera: the Camera object in the Game scene

`OrbitCamera` holds its distance, look height, turning speeds, pitch limits and how far it keeps from the ground and blocks. Change them in the inspector: the values saved in `Assets/Scenes/Game.unity` win over the defaults in `OrbitCamera.cs`.

### Server leniency: `MovementTolerances` in `comet/src/Comet.Server/Movement/MovementValidator.cs`

How much faster or higher than the rules a report may be before the server snaps the player back. They're tuned for bad networks rather than feel; `MovementHeadroom` and the bad-profile runs (`tools/shapeland-netrun.sh`) show how much room honest players need. The checks follow the shape's speed and jump and `Gravity`, so tuning movement doesn't need changes here.

### Trying a change

- **In a browser:** `tools/shapeland-dev.sh` rebuilds the content and the server on every run, and the web build whenever the client's code changed.
- **In the editor (fastest):** `tools/shapeland-dev.sh --no-web` builds the content and starts the server on `localhost:5080` (add `--bots N` for company). After each change, rerun it and restart play mode, which copies in the new content as it starts: the server and play mode each load the numbers once. If they still differ, the join screen says so.
- **Play-mode tests** read the same copy, refreshed when they start; rebuild the content first.
