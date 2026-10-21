# Project Comet

<!-- Project description: to be written by Lucy -->

## Working on the docs

The project's docs are most usable when opened as an Obsidian vault.

The vault's shared config and community plugins are in the repo, so they're ready after cloning (enable community plugins if Obsidian asks).

- **Unhide for Obsidian**: shows dot-folders. `.git` stays hidden.
- **Git**: commit and pull from inside Obsidian.

## Requirements

- **.NET 10 SDK**: for the servers, libraries, bots and tools.
- **Unity 6000.6.4f1**: for ShapeLand's client (`shapeland/ShapeLand.Unity`).
- **Docker**: for the simulated-network runs (`--net` profiles and `tools/shapeland-netrun.sh`).

- **AWS CLI and Vercel CLI**: only for the public test build.

## Layout

- `comet/`: Comet, the shared base: protocol, simulation, client and server libraries, content tools, and the Unity package. Its [README](comet/README.md) explains how they fit together.
- `shapeland/`: ShapeLand, the demo game: content, game server, bots, Unity client and deployment. Its [README](shapeland/README.md) maps the code and the tuning values.
- `tools/`: scripts for building, running and testing (below), and the stack benchmark.
- `docs/`: project notes and the proposal.

## First setup

```sh
tools/unity-restore.sh
dotnet build ProjectComet.slnx
dotnet test --solution ProjectComet.slnx
```

Run `tools/unity-restore.sh` again whenever `shapeland/ShapeLand.Unity/Assets/packages.config` changes, before opening the project in the editor. The scripts that run Unity (`unity-batch.sh`, and `shapeland-dev.sh` through it) run it themselves when it's needed.

## Scripts

Run them from the repository root. Each script's options are listed below and in its header comment; `shapeland-dev.sh` and `shapeland-netrun.sh` also print theirs with `--help`.

| Script | What it's for |
| --- | --- |
| `tools/blender-export-glb.py` | Export each top-level object in a `.blend` file as a `.glb`, into the folder named by the file's `export_dir` property. Run it from Blender's Text Editor, or with `blender -b <file> --python tools/blender-export-glb.py`; the script's header has the setup. |
| `tools/shapeland-dev.sh` | Run ShapeLand locally: the web client in a browser, served by a local game server, without opening the Unity editor; or, with `--no-web`, just the server, for the editor's play mode or a desktop build. It restarts the server when the content changes. The Unity editor can run the server itself for play mode, with optional bots, web build and local network ([Comet › Server](comet/README.md#the-local-game-server)), so most level work needs no terminal. |
| `tools/shapeland-netrun.sh` | Headless bot runs under a simulated network profile, printing what the bots measured (snap-backs, cheaters caught, ping, interpolation delay). |
| `tools/shapeland-net-profiles.sh` | The network profiles the other scripts use: `none`, `good` (about 80 ms round trip, 1% loss), `bad` (about 200 ms, 2% loss) and `awful` (about 200 ms, 5% loss). |
| `tools/shapeland-testbuild.sh` | The public test build: the page on Vercel and the game server on an on-demand AWS instance. |
| `tools/unity-batch.sh` | Run a Unity build or tests and return when the work is done: in the open editor when it has the project open, else in batch mode (where the editor often hangs while quitting). |
| `tools/unity-restore.sh` | Restore a Unity project's NuGet packages (see First setup). Takes the project as an optional argument (default `shapeland/ShapeLand.Unity`). |
| `tools/StackBench/` | The stack benchmark: Comet's message layer under load, with no game logic. See its [README](tools/StackBench/README.md). |

### Play locally in a browser

```sh
tools/shapeland-dev.sh                      # build if needed, start the server, open the page
tools/shapeland-dev.sh --bots 10 --bots-chat
tools/shapeland-dev.sh --net bad --bots 5   # everyone gets about 200 ms round trip and 2% loss (Docker)
tools/shapeland-dev.sh --lan                # let other devices on the network join
tools/shapeland-dev.sh --spawn spawn.tests.start   # start everyone at one spawn point, in any zone
```

| Option | What it does |
| --- | --- |
| `--rebuild` | Make the Unity web build even if it looks up to date. |
| `--no-build` | Use the last web build as it is (content still rebuilds). |
| `--bots N` | Start N honest bots (default 0). |
| `--cheaters N` | Start N speed-cheating bots (default 0). |
| `--bots-chat` | Let the bots chat now and then (they stay quiet otherwise). |
| `--port P` | The game server's port (default 5080). |
| `--lan` | Listen on every network interface, so other devices on the network can join. |
| `--no-open` | Don't open the page in a browser. |
| `--net P` | Run the server and bots in Docker with a network profile on the server's link, so every client gets it (desktop builds too). |
| `--bots-net P` | With `--net`, a profile the bots get on top of it (default `none`), for example for high-ping bots. |

Close the Unity editor first: the build fails while it has the project open. Ctrl+C stops everything.

### Network runs

```sh
tools/shapeland-netrun.sh good
tools/shapeland-netrun.sh bad --seconds 300 --bots 20 --cheaters 2
```

The first argument is the profile on the server's link: `none`, `good`, `bad` or `awful`.

| Option | What it does |
| --- | --- |
| `--seconds S` | How long the bots run, warmup included (default 180). |
| `--bots N` | Honest bots, whose network numbers are recorded (default 10). |
| `--cheaters N` | Speed-cheating bots, which should all be caught (default 1). |
| `--cheat-speed F`, `--cheat-jump F` | How much faster cheaters move and jump than allowed (default 2.5 and 1); small values make subtle cheaters, for tuning the tolerances. |
| `--seed N` | The bots' seed, so runs before and after a change are comparable (default 1). |
| `--warmup S` | Seconds after joining before numbers are recorded, while the delay settles (default 10). |
| `--bots-net P` | A profile the bots get on top, for example for high-ping bots; they then run in their own container. |
| `--far N` | Also N far players: honest bots named "Far" in their own container with `--far-net` on top, to see how they affect the others (the summary is the others'; theirs follows). |
| `--far-net P` | The far players' extra profile (default `bad`). |
| `--port P` | The server's port on this machine (default 5091). |

Results also go to a file stamped with the profile and commit. The default port (5091) lets a `shapeland-dev.sh` session keep running alongside.

### Unity batch jobs

```sh
tools/unity-batch.sh build Web ../../artifacts/unity/shapeland/web
tools/unity-batch.sh test PlayMode
tools/unity-batch.sh -- <Unity arguments>
```

`build` builds one of the build profiles in `Assets/Settings/Build Profiles/` (`Web`, `Linux`; the same ones as File › Build Profiles in the editor) into the given folder. `test` runs `PlayMode` (the default) or `EditMode` tests and writes the results (`Logs/results.xml`, or `Logs/results.json` from an open editor). Everything after `--` goes to a batch-mode editor as it is.

When a Unity editor has the project open, `build` and `test` run in it, through the [Unity CLI](https://docs.unity.com/en-us/unity-cli) (`unity` and `jq` on the path) and the project's Unity Pipeline package. A build there needs the editor on the profile's platform already, since switching reimports the whole project. Without an open editor they run in batch mode. Options, before the job:

| Option | What it does |
| --- | --- |
| `-p project` | The Unity project (default `shapeland/ShapeLand.Unity`). |
| `-l logfile` | The editor's log, or an open editor's build report (default `Logs/batch.log`). Relative paths, here and in the job's arguments, are relative to the project. |
| `-t minutes` | The time limit (default 30). |

The exit code is the editor's (tests: 0 passed, 2 failed), 1 if it gave no result, and 124 on the time limit.

### Public test build

```sh
tools/shapeland-testbuild.sh setup          # once per AWS account
tools/shapeland-testbuild.sh page           # build the web page and publish it on Vercel
tools/shapeland-testbuild.sh up --hours 24  # start the game server; it stops itself after 24 hours
tools/shapeland-testbuild.sh status         # what's running, and whether the server answers
tools/shapeland-testbuild.sh log            # the server's boot log (from its console)
tools/shapeland-testbuild.sh down           # terminate it when done
```

`up --hours H` sets when the server shuts itself down (default 24; 0 for never). `page --no-build` publishes the last web build as it is.

The server costs about 20 cents a day while it's up. When it's down, the page says the game isn't open for testing. Settings that aren't in the repo live in `~/.config/shapeland-testbuild/env`: the first run on a computer creates it and stops until `ACME_EMAIL` (the contact address Caddy gives Let's Encrypt and ZeroSSL for the server's certificate) is filled in.

## Known quirks

- `TickLoopTests.RunsAtTheTickRate` can fail when every test suite runs at once; it passes on its own.

## AI usage

This project uses AI tools to assist with development. This means primarily code and tooling. Code is reviewed by maintainers and individual maintainers are responsible for any agent generated code.

Non-code creative work is made by people. This means game design docs, models, textures, audio, and in game text.
