# ShapeLand

Comet's demo and reference game. Requirements, setup and the scripts are in the [repository README](../README.md).

| Folder | What it holds |
| --- | --- |
| `art/` | Source files for the art (such as `.blend`), plain in git (no Git LFS yet); none yet, until the one prop's model. Exports go into `ShapeLand.Unity/Assets/Models/` as `.glb`. |
| `content/` | Game data: the shapes, their looks and other groups (`groups/`), the props (placeable `.glb` models; the levels are mostly Comet's primitives, with a few simple props), the game-wide settings (`settings/game.toml`), the interface text and the zone, made by hand in the zone editor. Compiled to `content.bin` by `src/ShapeLand.ContentBuild`. |
| `src/` | The game server, the bots, the content build and `ShapeLand.Shared` (rules, world and messages used by server, bots and client). |
| `ShapeLand.Unity/` | The Unity client. |
| `tests/` | Tests for the .NET projects. |
| `docker/`, `deploy/` | Simulated-network runs and the public test build. |

## Code

How Comet's libraries fit together, and how a move travels from one player to the others, is in [Comet's README](../comet/README.md). ShapeLand adds its rules, content and screens on top.

### Shared: `src/ShapeLand.Shared`

Used by the server, the bots and the Unity client alike: `World/ShapeLandRules.cs` (tick rate, name and chat rules), `World/ShapeLandWorld.cs` (each zone's collision world, with its pads, and where shapes start), `World/TriggerCharge.cs` (timing a player standing in a spawn point, teleporter or trigger, as the server decides and the client draws pads charging), `Content/` (the shape and game settings types and the compiled content file) and `Messages/` (ShapeLand's own messages: joining, spawns, zones and flips, and chat).

### Server: `src/ShapeLand.GameServer`

`ShapeLandServer` sets up ASP.NET Core and the tick loop; `ShapeLandGame` is the game: joining and changing looks, movement checks, falls the server finishes, chat, and who sees whom. `Player` holds one connection's state. Messages arrive on each connection's own flow and are queued; the tick thread handles them in order, so the game's state belongs to it alone.

### Unity client: `ShapeLand.Unity`

The Game scene (`Assets/Scenes/Game.unity`) holds:

| Object | Components | What they do |
| --- | --- | --- |
| Game | `CometConnection`, `JoinClient`, `GameView`, `JoinScreen` | Connecting and joining, and the zone the player is in with its flipped placements (`JoinClient`); drawing the zone's terrain, placed objects and pads (charging as the player stands in them), and the players (`GameView`); the join card for name, shape and colors, with the picked shape's start zone behind it (`JoinScreen`, `Assets/UI/JoinScreen.uxml`). |
| Hud | `Hud`, `GameMenu`, `Chat`, `NameTags`, `NetStats`, `CollisionDebug` | The in-game overlay (`Assets/UI/Hud.uxml`): round menu, Change Shape (C), Return to Spawn and chat buttons at the top left (hovering one with a mouse shows its name and key; larger with touch controls in use; the look card comes up over the game without dimming it, putting chat and the network numbers away until it closes), the chat box (it stays open after sending until Esc, the chat button, Enter on an empty line or a click outside closes it), name tags and speech bubbles, the network numbers (F3), the collision view (F4: Comet's `CollisionView` of the zone, players' bodies (upright cylinders), and a red body with a chat note where the server snapped the player back), the own shape showing through walls (V turns it off and on; `ShapeBody.ShowThroughWalls` with Comet's Silhouette shader), the Esc menu (below), and on phones the touch controls (`TouchControls`, below). |
| Camera | `OrbitCamera` | The third-person camera. |

`GameView` draws primitives without a name as one mesh per chunk (Vertex Color Lit, colors in the vertices), and named and blockout ones on their own (blockout in `Assets/Materials/Blockout.mat`). Other pieces in `Assets/Scripts`: `LocalPlayer` (the player's own movement, on a fixed step, and its reports), `ShapeBody` (a drawn shape, built from its look by `GameView.BuildShape`: its colors and fading), `ShapePreview` (the join card's turning shape, `Assets/Prefabs/ShapePreview.prefab`), `Vectors` (conversions between shared code's vectors and Unity's), `ShapeFade` (how shapes fade out and back in: falling, teleports, respawns, others coming and going) and `ShapeLandControls` (generated from the bindings in `Assets/ShapeLandControls.inputactions`; edit them in the Input System's window).

The builds are the build profiles in `Assets/Settings/Build Profiles/` (File › Build Profiles). The editor scripts in `Assets/Editor` add ShapeLand's steps to every build (`Builds`: the content copied in, the page's text filled in) and keep the asset library and Comet settings in step with the content each time Comet copies it in (`ContentSync`). Comet's own Content › Build menu runs the content build, and saving a zone does too.

The play-mode tests (`Assets/Tests`) load the Game scene against a real game server and join through the card: `JoinScreenTests`, `ChatTests` and `PlayTests`. `ClientRulesTests` and `TouchControlsTests` need no server; the first also checks that every interface text key the scripts, UXML and web page use exists.

#### Phones

ShapeLand plays in phone browsers from the same web build and page, in landscape:

- **The page** (`Assets/WebGLTemplates/ShapeLand`) counts a touch screen whose short side is phone-sized as a phone (the `phone` class on its root). There the game fills the screen with no header or footer, a "Tap to play" card goes full screen and locks landscape where the browser allows it (Android), a card asks to turn a phone held upright (on iPhone, to turn off Rotation Lock), and the game draws at most 1.5 pixels per CSS pixel. iPhone Safari can't go full screen, so the card says to Add to Home Screen; `manifest.webmanifest` opens that as a full-screen landscape app (its icon comes later).
- **What the page tells the game** (`WebPage`, through `Plugins/WebGL/ShapeLandPage.jslib`): whether it's a phone, whether it's past the tap card (the join card waits behind it), and the screen's safe area. In the editor, the Device Simulator set to a phone (Game view › Simulator) counts as a phone, so the phone layout can be checked in play mode without a web build.
- **The interface on a phone** scales from an 800 × 500 design size instead of the panel settings' 960 × 600 (`Hud`), so a panel pixel is about a CSS pixel; the join card doesn't focus its name field (that would bring up the keyboard).
- **The Esc menu** (`GameMenu`, `Assets/UI/Menu.uxml` inside the HUD): Resume, Controls, Settings, Change Shape, Respawn and Leave game (which asks first). Esc or gamepad Start closes the chat input first, then the look card (Start there joins or applies instead), else opens the menu; in it Esc goes back a step and Start closes it. The look card moves through its rows with the same actions (`JoinScreen`). Keys and the gamepad move the selection with the `Menu` action map; a mouse or tap chooses directly and a tap outside closes it. Controls lists the controls for the input in use (`TouchControls.Input`, carried on the HUD as `.pad-input` or `.touch-input`, which pick the `.keys-part`, `.pad-part` and `.touch-part` elements), with tabs for the others; Settings changes `GameSettings` (show through walls, and a camera speed for each kind of input), saved as they change, and has a Debug section switching the network numbers (F3) and the collision view (F4). The menu button's tip (its name and key) stays up, outlined, until the menu is first opened.
- **Touch controls** (`TouchControls`, the `touch` layer in `Hud.uxml`): a stick wherever a thumb lands on the left half, a camera drag on the right half, and Jump at the bottom right. The stick and Jump drive a virtual gamepad of their own, so the input actions read them like a real one. They show while the player can move and the screen has been touched (from the start on a phone), keep to the safe area, and hide when a keyboard or a real gamepad is used away from a phone (the mouse alone changes nothing). Chat gets a Send button on phones, and a tap outside closes it.
- **Icons** are from published packs (Kenney, CC0; Material Symbols, Apache 2.0), credited in `Assets/UI/Icons/README.md` with their licenses beside them.

### Interface text: `content/strings/en.toml`

Every label and message players see, by key (`[join]` then `name_taken` is `join.name_taken`), with `{name}` placeholders. UXML labels hold `#key`, scripts look keys up in the content's string table, and the web page's elements carry `data-text` keys, filled in when the web build is made. Servers send codes, never text.

### Bots and the content build

`src/ShapeLand.Bots` runs headless players (honest wanderers, optional cheaters) for load and network runs. `src/ShapeLand.ContentBuild` compiles `content/` into `artifacts/content/shapeland/content.bin`.

## Building the level

ShapeLand's main zone is `content/zones/hub`, built by hand with Comet's zone editor; [Comet's README](../comet/README.md#zones-and-the-zone-editor) describes the tools.

- **Zones on a server:** a server plays every zone in the content. Each player is in one at a time and sees only the players there; chat is server-wide. Each shape starts at its own spawn (`spawn` in `content/shapes/<shape>.toml`), in any zone, unless the server's `ShapeLand:StartSpawn` names one spawn point for everyone (`--spawn` in `shapeland-dev.sh`). The server tells a player their zone with `ZoneEntered` (on joining and on going to another), and the client and the bots build its world then; the join screen shows the picked shape's start zone until then.
- **The tests' zones:** `content/zones/tests` and `tests_far` are laid out for the play-mode and end-to-end tests, which start at `spawn.tests.start` so editing the hub can't break them: flat ground straight ahead of the start spawn, then a drop, and off to the side a second spawn point, teleporters (two linked in the zone, one to `tests_far`) and a plate that opens a door for a few seconds. Change them only along with the tests (`ContentTests` checks the layout they count on).

- **Edit it:** in the Unity editor, Comet › Zones › Open… and pick the zone (or Open Recent…), then use the Terrain and Place tools. The level is built mostly from Comet's primitives and groups (such as `groups/trees.toml`), with a few simple props; mark stand-ins as blockout (the place tool's Place as blockout) and swap them for props as those are made. Ctrl+S saves the zone's files and rebuilds the content; commit the changed files under `content/zones/` and `content/groups/`.
- **Spawn points:** add them with the Place tool's new spawn point. A player standing in one for its stand time claims it (a chat line says so) and goes back to it on falling out of the world or Return to Spawn, even from another zone. Each shape's `spawn` must name one; the content build reports any that don't. Spawn pads are drawn in `unset_color` (`[spawns]` in `game.toml`) until they're the player's, then in their body color. Standing in one turns its pad toward that color, blinking once just before the claim.
- **Teleporters:** add them with the Place tool's new teleporter, and set each one's target (a teleporter in any zone; a two-way link is two teleporters naming each other). Standing in one for its stand time sends the player to its target, which doesn't send them on until they step out and back in. While a player stands in one, its pad turns toward its charge color (the teleporter's Inspector), blinking once just before it sends them.
- **Triggers:** add them with the Place tool's new trigger, give the placements to flip a tag (several may share one; a tagged group copy flips whole; turn off the objects of ones that start off, with the checkbox at the top of the Inspector), and list the tag in the trigger's effects (Toggle Tag), each with an optional revert time. A trigger has one state for everyone: it fires when the first player in it has stood there for its stand time. A placement that turns solid around a player lets them through until they're out of it. Flips last until the server restarts. A trigger has no pad until one is added in its Inspector (Add Pad); while a player stands in it, the pad turns toward its charge color and blinks once just before it fires (one that fires at once doesn't blink).
- **New ones' defaults** (stand times, pad kinds, pad and volume heights, pad colors, and teleporters' and triggers' charge colors) are `[spawns]`, `[teleporters]` and `[triggers]` in `game.toml`; each spawn point, teleporter and trigger keeps its own values after.
- **A new prop:** model it in Blender (source in `art/`), export it as a `.glb` into `ShapeLand.Unity/Assets/Models/Props/` (low-poly with flat colors, sized in meters, standing on its origin and facing +Z), and add `content/props/<name>.toml` with its `id` (`prop.<name>`), its `model` path (relative to `Assets/`) and optionally `color` (0xRRGGBB), `uniform_scale` and `display_name` (the name the Place panel shows instead of the id; a group's `.toml` can have one too). Objects named with a `-col` suffix are its collision mesh, hidden in the game; without them its whole mesh collides. Rebuild the content (Comet › Content › Build) and it appears in the Place panel.
- **Default colors** for the whole game are in `[world]` in `game.toml`: `ground_color` and `under_color` for terrain, `placed_color` for placements without their own color. The zone editor's place tool gives each new placement the color picked in its panel.
- **Kill height:** falling below `kill_height` (`[world]` in `game.toml`) sends a player back to their spawn. A zone high in the sky or deep below sets its own with `kill_height` in its `zone.toml`.
- **The polygon budget** for round primitives is `[primitives]` in `game.toml` (`facet_length`, `min_sides`, `max_sides`), also tuned from Comet › Zones › Polygon Budget.
- **The player colors** are `[colors] players` in `game.toml`, one `0xRRGGBB, # name` a line, also picked from Comet › ShapeLand › Player Colors, which shows them ten a line as on the look card and rebuilds the content on save. The game's own editor tools sit under Comet › ShapeLand.
- **Before a release:** `dotnet run --project src/ShapeLand.ContentBuild -- build --release` fails on any blockout placement left in a zone (a plain build lists them as warnings).

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
| `mantle_height` | The most a player in the air rides up onto a ledge when moving into its side, in meters. Each move climbs at most as much as the steepest terrain over that distance, so a jump up a slope keeps its speed; 0 turns this off. |
| `max_slope` | The steepest ground a player stands on, in degrees; steeper terrain and meshes are walls. |
| `ground_snap` | How far the ground may drop under the feet while staying grounded (walking downhill rather than falling), in meters. |
| `body_radius`, `body_height` | The collision body every shape shares: an upright cylinder of radius `body_radius` with a flat base, `body_height` tall, in meters. |

### How shapes look: the groups in `content/groups/shape_*.toml`

Each shape's `look` (in `content/shapes/<shape>.toml`) is a group of primitives standing on its pivot at the player's feet, facing +Z. Parts with `role = "body"` take the player's body color and `role = "eyes"` their eye color; other parts keep their own color. Edit a look like any group in the zone editor: place a copy (Groups in the place tool's panel) and use Open group or Edit in place, then delete the copy. The shape's own `scale` sizes the whole look and `hover` lifts it off the ground, in meters before scaling. These change the drawing only: every shape collides as the same body (`body_radius`, `body_height`). The 2D icon on buttons and chat lines is drawn by the kind of the body part (a box, an octahedron, or else a pyramid).

### The camera: the Camera object in the Game scene

`OrbitCamera` holds its distance, look height, turning speeds, pitch limits and how far it keeps from the ground. Change them in the inspector: the values saved in `Assets/Scenes/Game.unity` win over the defaults in `OrbitCamera.cs`.

### Server leniency: `MovementTolerances` in `comet/src/Comet.Server/Movement/MovementValidator.cs`

How much faster or higher than the rules a report may be before the server snaps the player back. They're tuned for bad networks rather than feel; `MovementHeadroom` and the bad-profile runs (`tools/shapeland-netrun.sh`) show how much room honest players need. The checks follow the shape's speed and jump and `gravity`, so tuning movement doesn't need changes here.

### Trying a change

- **In a browser:** `tools/shapeland-dev.sh` rebuilds the content and the server on every run, and the web build whenever the client's code changed. Its web builds skip compression to build quicker (into `artifacts/unity/shapeland/web-dev`).
- **On a phone:** `tools/shapeland-dev.sh --lan`, then open the address it prints on a phone on the same network (the computer's firewall must let the port in). For layout alone, the editor's Device Simulator is quicker (Phones, above).
- **In the editor (fastest):** press Play: the editor builds and starts the game server on `localhost:5080` if none is running ([Comet's local game server](../comet/README.md#the-local-game-server)), and keeps it running between plays. Comet › Server › Options… adds bots, the web build and the local network, so others can join a playtest. `tools/shapeland-dev.sh --no-web` still works instead (with `--net` profiles and cheaters); the editor uses a server it finds running and leaves it alone. Restart play mode after each change, which copies in the new content as it starts: the server and play mode each load the numbers once. If they still differ, the join screen says so.
- **Content changes** (the numbers under `content/`, zones saved from Comet › Zones) need no rerun: the editor's own server restarts after each content build, and while it runs, `shapeland-dev.sh` watches `content/`, rebuilds the content when something there changes and restarts the server and bots. Clients rejoin by themselves (the join card tries for a minute after the server shuts down), and a browser page reloads first to load the new content; play mode says to restart it. A failed build leaves the old server running. Code changes still need a rerun; `--no-watch` turns the watching off.
- **Play-mode tests** read the same copy, refreshed when they start; rebuild the content first.
