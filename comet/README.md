# Comet

The shared base for games built on Comet: the network protocol, the movement simulation that clients and servers share, the client and server libraries, the content pipeline and the Unity package. Game rules live in each game (ShapeLand's are in [`shapeland/`](../shapeland/README.md)); Comet keeps the mechanisms. Requirements, setup and the scripts are in the [repository README](../README.md).

## Projects

| Project | What it does | Used by |
| --- | --- | --- |
| `src/Comet.Protocol` | Frames and messages: a frame is a tick (4 bytes) and messages, each a varint ID, a varint length and a MessagePack payload (`MessageWriter`, `FrameReader`). Comet's own messages (`MessageIds`, `Messages/`) take IDs 1 to 63; games number theirs from 64. | Everything |
| `src/Comet.Simulation` | The movement every side runs the same way: `PlayerMotor` steps a player through a `CollisionWorld` (a zone's terrain pieces, placed collision meshes and a kill height) under the game's `MovementRules`. Also `ZoneTerrain` (a zone's pieces as whole grids across their chunk tiles), `TerrainMesher` (each chunk's flat-shaded, vertex-colored mesh with its skirts, the same data the server collides with) and `Spawns` (spawn points and arriving at one). | Client, server, bots |
| `src/Comet.Client` | The client's side of a connection, polled from one thread on every platform: `ClientSession` (decodes frames, pings, reports the player's position), `ServerClock` (the server's tick, from pings), `RemoteEntities` and `InterpolationDelay` (drawing others a little in the past), `FixedStep`, `PositionReporter` and `CorrectionBlend` (the player's own movement), and `WebSocketTransport` for desktop and bots. | Unity client, bots |
| `src/Comet.Server` | The server's side: `Connection` (one client's WebSocket, with flood limits), `ConnectionRegistry`, `TickLoop` (the fixed-rate game loop), `MovementValidator` (checks each position report against the rules) and `StateStamp` (stamps others' states with the tick they were sent at). `MapCometWebSocket` wires it into ASP.NET Core. | Game servers |
| `src/Comet.Content` | Compiled content as games load it: `ContentTable` (entries by key and number), `Asset` (a placeable model with its baked collision and bounds), `Zone` (terrain pieces, chunks with their tiles, placements, spawns and volumes; `ZoneGrid` for chunk and sample coordinates), `Heightmap` and `ColorMap` with their file formats, and `StringTable` (interface text by key). | Client, server, content build |
| `src/Comet.ContentBuild` | The content build: `ContentBuilder` reads a game's TOML files, checks them against their C# types, assigns each key a permanent number (`IdRegistry`, the committed `ids.toml`) and reads the string file (`strings/en.toml`). It also bakes each asset's collision triangles and bounds from its `.glb` (`ModelBaker`) and compiles zone folders, checking placements against the assets. Errors point at the file, line and column. | Each game's content build tool |
| `src/Comet.ZoneFiles` | A zone's folder of files, read and written the same way by the content build and the zone editor (`ZoneFolder`, on Tomlyn), and what they must mean (`ZoneValidation`). | Content build, zone editor |
| `unity/` | The Unity package: `CometConnection` drives a `ClientSession` from the frame loop (received frames before any `Update`, sends after every `LateUpdate`), `BrowserWebSocketTransport` is the web build's transport, on the browser's own WebSocket, `UiText` fills UXML text from a string table (`text="#key"`), `TerrainMeshes` turns `TerrainMesher`'s chunks into Unity meshes, `AssetLibrary` maps asset keys to their models and `PlacedColors` draws placed objects in their colors. `Zones/` holds the components of a zone opened in the editor. | Unity client |
| `unity/Editor/` | The zone editor (below), the content build menu (`ContentBuild`) and `CometSettings`, each game's settings for these tools. | Unity editor |
| `tests/` | xUnit tests, one project per library. | |

`Comet.Protocol`, `Comet.Simulation`, `Comet.Client`, `Comet.Content` and `Comet.ZoneFiles` also target `netstandard2.1` with C# 9, since Unity loads them as source packages (each folder has a `package.json` and an `.asmdef`; `Comet.ZoneFiles` is editor-only). The rest target .NET 10 only.

## How a move travels

1. **On the player's machine**, the game steps its own player with `PlayerMotor` at a fixed rate (`FixedStep`, 60 steps a second in ShapeLand), driven by the server's clock from `ClientSession.ServerSeconds`, so every other step falls on a server tick. `PositionReporter` decides which of those steps to report (about 15 a second while moving, at once on starting, stopping or turning, none while standing). `ClientSession.ReportPosition` sends the position, velocity and facing in a frame stamped with that step's tick.
2. **On the server**, the `Connection` reads the frame on its own receive flow and hands it to the game (`IConnectionHandler`), which queues it for the tick thread. Each tick, the game runs the report through the player's `MovementValidator`: the client is the authority on its own movement, within the rules. Speed is checked against a distance budget, height against the arc of a jump from the last ground stood on. A report that breaks them gets a `PositionCorrection` (a snap-back), and is logged for moderation.
3. **An accepted position** becomes an `EntityState` for every other player, stamped by `StateStamp` with the tick the mover sent it at. `Connection.EntityStates` keeps only the newest state per entity, so a slow connection catches up instead of falling behind. At the end of the tick each connection flushes one frame.
4. **On every other client**, `ClientSession` adds the state to `RemoteEntities`, which draws each entity `InterpolationDelay` ticks in the past, between the two states around that time. The delay adapts to how late states arrive. When states run out, the entity is carried on by its velocity (dead reckoning) and blended back once the late state comes.
5. **A snap-back** reaches the mover's `ClientSession.Corrected`: the simulation jumps to the corrected position at once, so its next reports pass, and `CorrectionBlend` lets the drawn shape glide after it.

The server also finishes the fall of a player who goes silent in the air, so going quiet mid-jump can't be used to hover.

## Threads and time

- **Client:** one thread. Call `ClientSession.Update` each frame, write messages, then `Flush`; events fire inside `Update` in the order the server sent them. The session never reads a clock itself: callers pass in their own monotonic time, since web builds have neither threads nor working timers.
- **Server:** each connection receives on its own flow; game state belongs to the tick thread. Games queue what connections hand them and handle the queue at the start of each tick (`ShapeLandGame` is the example).

## Connections ending

`IClientTransport.CloseReason` and `ClientSession.CloseReason` say why a connection ended as a code (`CloseReason`: lost, server closed, server shut down, fell behind and so on), and the game puts it in its own words; `Error` holds English details for logs. Servers likewise send codes, such as a game's join rejection reasons, never text.

## Content and interface text

A game's content folder holds one TOML file per entry, in folders by type, plus `ids.toml` (each key's permanent number) and `strings/en.toml` (interface text). The game's content build tool, built on `ContentBuilder`, compiles everything into one MessagePack file (`content.bin`) that servers and clients load. Network messages and saved data use the numbers; names and text come from the loaded content. Interface text is looked up by key, with `{name}` placeholders (`StringTable.Format`).

## Zones and the zone editor

A zone is a grid of 64 m chunks. Its terrain is made of pieces: heightmaps on a 1 m grid, stored as one tile per chunk they cover, that can stack (an island above the ground). Where a piece's ground ends, the mesher adds skirts: walls down to a flat bottom `skirt_depth` below the piece's lowest ground, or to its underside map if it has one, stopping on any terrain below. Placed assets, spawn points and collision volumes belong to the chunk holding their pivot. The folder layout is in `ZoneFolder`; the content build compiles it into `content.bin`, and servers and clients only ever read that.

The zone editor (Comet › Zones) opens a zone's folder into a working scene and saves it back:

- **Open… and Save.** The scene is a working copy in an ignored folder, named after the zone; the zone's files are the source of truth. Saving the scene (Ctrl+S) saves the files, and the content build then runs in the background. Play mode always starts the game's own scene, which plays the zone from content.
- **Terrain Tool** (also in the Scene view's Tools overlay): add, pick and delete pieces; brushes to raise, lower, smooth and flatten (on the ground or the underside), cut holes and fill ground back, and paint colors; each piece's settings: skirt depth, rim and hole skirts, its top and underside colors. The tool warns where two pieces' ground is closer than a body height. Every stroke is one undo step.
- **Place Tool:** pick an asset or a spawn point in its panel and click to place it on whatever is under the mouse. Placed objects are moved, turned and scaled with Unity's own tools. A new spawn point gets a marker (the settings' marker asset), kept as its child.
- **Colors** fall back in order: a placed object's own color (its Inspector), its asset's `color`, then the game's default; a terrain piece's own top and underside colors, then the game's. Each color field has a reset button that goes back to the default.

`CometSettings` (Create › Comet › Settings, one per game) tells the tools where the zones, the content build and the game's scene are, and holds the defaults they draw with; a game may fill some of it in from its content (ShapeLand's `ContentSync`).
