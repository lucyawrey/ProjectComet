# Project Comet

<!-- Project description: to be written by Lucy -->

## Working on the docs

The project's docs are most usable when opened as an Obsidian vault.

The vault's shared config and community plugins are in the repo, so they're ready after cloning (enable community plugins if Obsidian asks).

- **Unhide for Obsidian**: shows dot-folders, including `.claude/` (claude agent notes). `.git` stays hidden.
- **Git**: commit and pull from inside Obsidian.

## Requirements

- **.NET 10 SDK**: for the servers, libraries, bots and tools.
- **Unity 6000.6.4f1**: for ShapeLand's client (`shapeland/unity`).
- **Docker**: for the simulated-network runs (`--net` profiles and `tools/shapeland-netrun.sh`).

- **AWS CLI and Vercel CLI**: only for the public test build.

## Layout

- `comet/`: Comet, the shared base: protocol, simulation, client and server libraries, content tools, and the Unity package.
- `shapeland/`: ShapeLand, the demo game: content, game server, bots, Unity client and deployment.
- `tools/`: scripts for building, running and testing (below), and the stack benchmark.
- `docs/`: project notes and the proposal.

## First setup

```sh
tools/unity-restore.sh
dotnet build ProjectComet.slnx
dotnet test --solution ProjectComet.slnx
```

Run `tools/unity-restore.sh` again whenever `shapeland/unity/Assets/packages.config` changes.

## Scripts

Run them from the repository root. Each script's header comment lists all its options.

| Script | What it's for |
| --- | --- |
| `tools/shapeland-web.sh` | Build ShapeLand's web client and play it in a browser, served by a local game server, without opening the Unity editor. |
| `tools/shapeland-netrun.sh` | Headless bot runs under a simulated network profile, printing what the bots measured (snap-backs, cheaters caught, ping, interpolation delay). |
| `tools/shapeland-net-profiles.sh` | The network profiles the other scripts use: `none`, `good` (about 80 ms round trip, 1% loss), `bad` (about 200 ms, 2% loss) and `awful` (about 200 ms, 5% loss). |
| `tools/shapeland-testbuild.sh` | The public test build: the page on Vercel and the game server on an on-demand AWS instance. |
| `tools/unity-batch.sh` | Run Unity in batch mode (tests, builds) and return when the work is done, since the editor often hangs while quitting. |
| `tools/unity-restore.sh` | Restore a Unity project's NuGet packages (see First setup). |
| `tools/observe.cs` | Join a local server as a player standing still and print how other players' movement arrives. |
| `tools/StackBench/` | The stack benchmark: Comet's message layer under load, with no game logic. See its [README](tools/StackBench/README.md). |

### Play locally in a browser

```sh
tools/shapeland-web.sh                      # build if needed, start the server, open the page
tools/shapeland-web.sh --bots 10 --bots-chat
tools/shapeland-web.sh --net bad --bots 5   # everyone gets about 200 ms round trip and 2% loss (Docker)
tools/shapeland-web.sh --lan                # let other devices on the network join
```

Close the Unity editor first: the build fails while it has the project open. Ctrl+C stops everything.

### Network runs

```sh
tools/shapeland-netrun.sh good
tools/shapeland-netrun.sh bad --seconds 300 --bots 20 --cheaters 2
```

Results also go to a file stamped with the profile and commit. The default port (5091) lets a `shapeland-web.sh` session keep running alongside.

### Watching movement

With a server and moving players (bots, for instance) running, and the content built:

```sh
dotnet run tools/observe.cs -- --url ws://localhost:5080/ws --seconds 20
```

### Unity batch jobs

```sh
tools/unity-batch.sh -- -runTests -testPlatform PlayMode -testResults Logs/results.xml
tools/unity-batch.sh -- -buildTarget WebGL -executeMethod ShapeLand.Client.Editor.Builds.WebGame -quit
```

The project defaults to `shapeland/unity` (`-p` to change it). The exit code is the editor's (tests: 0 passed, 2 failed), 1 if it gave no result, and 124 on the time limit.

### Public test build

```sh
tools/shapeland-testbuild.sh setup          # once per AWS account
tools/shapeland-testbuild.sh page           # build the web page and publish it on Vercel
tools/shapeland-testbuild.sh up --hours 24  # start the game server; it stops itself after 24 hours
tools/shapeland-testbuild.sh status
tools/shapeland-testbuild.sh down           # terminate it when done
```

The server costs about 20 cents a day while it's up. When it's down, the page says the game isn't open for testing. Settings that aren't in the repo live in `~/.config/shapeland-testbuild/env`.

## Known quirks

- `TickLoopTests.RunsAtTheTickRate` can fail when every test suite runs at once; it passes on its own.
