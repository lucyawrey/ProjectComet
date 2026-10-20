# ShapeLand

Comet's demo and reference game. Requirements, setup and the scripts are in the [repository README](../README.md).

| Folder | What it holds |
| --- | --- |
| `art/` | Source files for the art (such as `.blend`), plain in git (no Git LFS yet); none yet, until the one prop's model. Exports go into `ShapeLand.Unity/Assets/Models/` as `.glb`. |
| `content/` | Game data: the shapes, their looks and other groups (`groups/`), the placeable assets (`.glb` props; the levels are built from Comet's primitives), the game-wide settings (`settings/game.toml`), the interface text and the zone, made by hand in the zone editor. Compiled to `content.bin` by `src/ShapeLand.ContentBuild`. |
| `src/` | The game server, the bots, the content build and `ShapeLand.Shared` (rules, world and messages used by server, bots and client). |
| `ShapeLand.Unity/` | The Unity client. |
| `tests/` | Tests for the .NET projects. |
| `docker/`, `deploy/` | Simulated-network runs and the public test build. |

## Code

How Comet's libraries fit together, and how a move travels from one player to the others, is in [Comet's README](../comet/README.md). ShapeLand adds its rules, content and screens on top.

### Shared: `src/ShapeLand.Shared`

Used by the server, the bots and the Unity client alike: `World/ShapeLandRules.cs` (tick rate, name and chat rules, spawn claim reach), `World/ShapeLandWorld.cs` (the collision world, built from the zone in content), `Content/` (the shape and game settings types and the compiled content file) and `Messages/` (ShapeLand's own messages: joining, spawns and chat).

### Server: `src/ShapeLand.GameServer`

`ShapeLandServer` sets up ASP.NET Core and the tick loop; `ShapeLandGame` is the game: joining and changing looks, movement checks, falls the server finishes, chat, and who sees whom. `Player` holds one connection's state. Messages arrive on each connection's own flow and are queued; the tick thread handles them in order, so the game's state belongs to it alone.

### Unity client: `ShapeLand.Unity`

The Game scene (`Assets/Scenes/Game.unity`) holds:

| Object | Components | What they do |
| --- | --- | --- |
| Game | `CometConnection`, `JoinClient`, `GameView`, `JoinScreen` | Connecting and joining (`JoinClient`); drawing the zone's terrain and placed objects, and the players (`GameView`); the join card for name, shape and colors (`JoinScreen`, `Assets/UI/JoinScreen.uxml`). |
| Hud | `Hud`, `Chat`, `NameTags`, `NetStats` | The in-game overlay (`Assets/UI/Hud.uxml`): the Change Look button and respawn fade, the chat box, name tags and speech bubbles, and the network numbers (F3). |
| Camera | `OrbitCamera` | The third-person camera. |

Other pieces in `Assets/Scripts`: `LocalPlayer` (the player's own movement, on a fixed step, and its reports), `ShapeBody` (a drawn shape, built from its look by `GameView.BuildShape`: its colors and fading), `ShapePreview` (the join card's turning shape, `Assets/Prefabs/ShapePreview.prefab`), `Vectors` (conversions between shared code's vectors and Unity's), `RespawnFade` (how falling fades) and `ShapeLandControls` (generated from the bindings in `Assets/ShapeLandControls.inputactions`; edit them in the Input System's window).

The builds are the build profiles in `Assets/Settings/Build Profiles/` (File › Build Profiles). The editor scripts in `Assets/Editor` add ShapeLand's steps to every build (`Builds`: the content copied in, the page's text filled in) and keep the asset library and Comet settings in step with the content each time Comet copies it in (`ContentSync`). Comet's own Content › Build menu runs the content build, and saving a zone does too.

The play-mode tests (`Assets/Tests`) load the Game scene against a real game server and join through the card: `JoinScreenTests`, `ChatTests` and `PlayTests`. `ClientRulesTests` need no server, and also check that every interface text key the scripts, UXML and web page use exists.

### Interface text: `content/strings/en.toml`

Every label and message players see, by key (`[join]` then `name_taken` is `join.name_taken`), with `{name}` placeholders. UXML labels hold `#key`, scripts look keys up in the content's string table, and the web page's elements carry `data-text` keys, filled in when the web build is made. Servers send codes, never text.

### Bots and the content build

`src/ShapeLand.Bots` runs headless players (honest wanderers, optional cheaters) for load and network runs. `src/ShapeLand.ContentBuild` compiles `content/` into `artifacts/content/shapeland/content.bin`.

## Building the level

ShapeLand's zone is `content/zones/hub` (`world.default_zone` in `game.toml` names it), built by hand with Comet's zone editor; [Comet's README](../comet/README.md#zones-and-the-zone-editor) describes the tools.

- **Which zone a server plays:** the default zone, unless `ShapeLand:Zone` names another (`--ShapeLand:Zone zone.tests`). The server names its zone in Comet's `Welcome`, and the client and the bots switch to it; the join screen shows the default zone until then.
- **The tests' zone:** `content/zones/tests` is laid out for the play-mode and end-to-end tests, which play it so editing the hub can't break them: flat ground straight ahead of its spawn, then a drop, with a slope and a gap off to the side. Change it only along with the tests (`ContentTests` checks the layout they count on).

- **Edit it:** in the Unity editor, Comet › Zones › Open… and pick the zone, then use the Terrain and Place tools. Ctrl+S saves the zone's files and rebuilds the content; commit the changed files under `content/zones/`.
- **Spawn points:** add them with the Place tool's spawn point option. Each shape's `spawn` (in `content/shapes/<shape>.toml`) must name one in the default zone, and one in each zone is its default (where every shape starts in the other zones); the content build reports any that don't match. Markers are drawn in `unset_color` (`[spawns]` in `game.toml`) until a player claims one.
- **A new asset:** model it in Blender, export it as a `.glb` into `ShapeLand.Unity/Assets/Models/`, and add `content/assets/<name>.toml` with its `id` (`asset.<name>`), its `model` path (relative to `Assets/`) and optionally `color` (0xRRGGBB) and `uniform_scale`. Objects named with a `-col` suffix are its collision mesh, hidden in the game; without them its whole mesh collides. Rebuild the content (Comet › Content › Build) and it appears in the Place panel.
- **Default colors** for the whole game are in `[world]` in `game.toml`: `ground_color` and `under_color` for terrain, `placed_color` for placements without their own color. The zone editor's place tool gives each new placement the color picked in its panel.

## Tuning movement

Every movement number has one home. Clients, bots and the server read the same values, and the server checks each player's movement against them, so a change applies everywhere once everything is rebuilt.

### Each shape: `content/shapes/<shape>.toml`

| Key | What it does |
| --- | --- |
| `max_speed` | Top ground speed, in meters per second. |
| `jump_velocity` | Upward speed at take-off, in meters per second. A jump rises v² / 2g: with gravity 25, 8.5 rises about 1.45 m. |
| `scale` | How big the shape is drawn, times its model's size. Looks only: collisions use the shared body below. |

### Physics for every shape: `[movement]` in `content/settings/game.toml`

| Key | What it does |
| --- | --- |
| `gravity` | Downward acceleration, in m/s². Higher makes jumps shorter and snappier at the same take-off speed. |
| `ground_acceleration`, `air_acceleration` | How quickly speed changes toward what the stick asks for, on the ground and in the air, in m/s². Low feels slippery, high feels instant. |
| `turn_speed` | How fast a shape turns to face where it's going, in degrees per second. Only the facing turns; movement changes direction at once. |
| `step_height` | The highest ledge walked up without jumping, in meters. |
| `ground_snap` | How far the ground may drop under the feet while staying grounded (walking downhill rather than falling), in meters. |
| `body_radius`, `body_height` | The collision body every shape shares: a square footprint `body_radius` from the center, and `body_height` tall, in meters. |

### How shapes look: the groups in `content/groups/shape_*.toml`

Each shape's `look` (in `content/shapes/<shape>.toml`) is a group of primitives standing on its pivot at the player's feet, facing +Z. Parts with `role = "body"` take the player's body color and `role = "eyes"` their eye color; other parts keep their own color. Edit a look like any group in the zone editor: place a copy (Groups in the place tool's panel) and use Open group or Edit in place, then delete the copy. The shape's own `scale` sizes the whole look and `hover` lifts it off the ground, in meters before scaling. These change the drawing only: every shape collides as the same body (`body_radius`, `body_height`). The 2D icon on buttons and chat lines is drawn by the kind of the body part (a box, an octahedron, or else a pyramid).

### The camera: the Camera object in the Game scene

`OrbitCamera` holds its distance, look height, turning speeds, pitch limits and how far it keeps from the ground and blocks. Change them in the inspector: the values saved in `Assets/Scenes/Game.unity` win over the defaults in `OrbitCamera.cs`.

### Server leniency: `MovementTolerances` in `comet/src/Comet.Server/Movement/MovementValidator.cs`

How much faster or higher than the rules a report may be before the server snaps the player back. They're tuned for bad networks rather than feel; `MovementHeadroom` and the bad-profile runs (`tools/shapeland-netrun.sh`) show how much room honest players need. The checks follow the shape's speed and jump and `gravity`, so tuning movement doesn't need changes here.

### Trying a change

- **In a browser:** `tools/shapeland-dev.sh` rebuilds the content and the server on every run, and the web build whenever the client's code changed.
- **In the editor (fastest):** `tools/shapeland-dev.sh --no-web` builds the content and starts the server on `localhost:5080` (add `--bots N` for company). Restart play mode after each change, which copies in the new content as it starts: the server and play mode each load the numbers once. If they still differ, the join screen says so.
- **Content changes** (the numbers under `content/`, zones saved from Comet › Zones) need no rerun: while it runs, `shapeland-dev.sh` watches `content/`, rebuilds the content when something there changes and restarts the server and bots. Clients rejoin by themselves (the join card tries for a minute after the server shuts down), and a browser page reloads first to load the new content; play mode says to restart it. A failed build leaves the old server running. Code changes still need a rerun; `--no-watch` turns the watching off.
- **Play-mode tests** read the same copy, refreshed when they start; rebuild the content first.
