# Project Comet

Project Comet is the working name for a three-part MMO research and development project. The three components are:

- **Comet**: The framework itself, libraries for creating a .NET game server and a Unity client for small-scale MMOs.
- **ShapeLand**: A minimalist prototype game built on top of Comet.
- **Crystal**: Only in the planning/documentation stage, the real game that would be built on top of Comet.

## Working on the docs

The project's docs are most usable when opened as an Obsidian vault.

The vault's shared config and community plugins are in the repo, so they're ready after cloning (enable community plugins if Obsidian asks).

- **Unhide for Obsidian**: shows dot-folders. `.git` stays hidden.
- **Git**: commit and pull from inside Obsidian.

## Requirements

- **.NET 10 SDK**: for the servers, libraries, bots and tools.
- **Unity 6000.6.4f1**: for ShapeLand's client (`shapeland/ShapeLand.Unity`).
- **Docker**: for local play with accounts (PostgreSQL runs in it; the editor plays without them by default), the .NET tests that need a database (Testcontainers starts a throwaway PostgreSQL for each run), and the simulated-network runs (`--net` profiles and `tools/run shapeland-netrun`). Those runs need Linux: Docker Desktop's VM on macOS has no `ifb` device, which they use to delay incoming traffic, and on Windows it depends on the WSL kernel. On Linux they need your user in the `docker` group and the traffic-shaping kernel modules loaded at boot (containers can't load them):

  ```sh
  printf 'ifb\nsch_netem\nsch_ingress\nact_mirred\ncls_matchall\n' | sudo tee /etc/modules-load.d/netem.conf
  sudo systemctl restart systemd-modules-load
  ```

- **AWS CLI and Vercel CLI**: only for the deployed game.

The tools are C# file apps run by the .NET SDK, so they work on Linux, macOS and Windows. The simulated-network runs need Linux (see Docker above), and Unity on Windows is untested.

## Layout

- `comet/`: Comet, the shared base: protocol, simulation, client and server libraries, content tools, and the Unity package. Its [README](comet/README.md) explains how they fit together.
- `shapeland/`: ShapeLand, the demo game: content, game server, bots, Unity client and deployment. Its [README](shapeland/README.md) maps the code and the tuning values.
- `tools/`: tools for building, running and testing (below).
- `docs/`: project notes and the proposal.

## Getting started

On Windows, the tools run as `tools\run <tool>` (see [Tools](#tools)).

### Making levels and playing in the editor

Unity and the .NET SDK are all it takes: no Docker, no accounts.

1. Install Unity Hub with Unity 6000.6.4f1 (add Web Build Support for web builds), and the .NET 10 SDK.
2. Restore the Unity project's NuGet packages, once after cloning and again whenever `shapeland/ShapeLand.Unity/Assets/packages.config` changes: `tools/run unity-restore`. The editor can't compile the project without them.
3. In Unity Hub, add the project from disk: `shapeland/ShapeLand.Unity`.
4. Build the content once (Comet › Content › Build), open `Assets/Scenes/Game.unity` and press Play. The editor builds and starts its own game server ([the local game server](comet/README.md#the-local-game-server)), and the join card asks for a name and a look.

With accounts off (the editor's default), the editor's server keeps the characters in `artifacts/dev-characters.json`: a name comes back with its look, where it stood, after stopping play mode, restarting the server or reopening the editor. Delete the file to start over. Building the level is described in [ShapeLand's README](shapeland/README.md#building-the-level), with props; the zone editor's tools are in [Comet's](comet/README.md#zones-and-the-zone-editor).

### Working on the code

```sh
dotnet build ProjectComet.slnx
dotnet test --solution ProjectComet.slnx
```

The tests that use PostgreSQL need Docker. To play with accounts in the editor, as builds do, turn on Comet › Server › Use Accounts (per computer): the editor's server then runs with the Login server and PostgreSQL in Docker. Play-mode tests run with `tools/run unity-batch test PlayMode`; adding `-- -testCategory "!Accounts"` skips the ones that need Docker. The code guides are in [Comet's README](comet/README.md) and [ShapeLand's](shapeland/README.md).

### Putting the game online

The deployed game needs the AWS CLI and the Vercel CLI, logged in; see [Deploying](#deploying) below.

## Tools

Run them from the repository root with the launcher: `tools/run <tool> [arguments]` on Linux and macOS, `tools\run <tool> [arguments]` on Windows (Command Prompt or PowerShell; PowerShell wants `.\tools\run`). Each tool's options are listed below and in its header comment, and `--help` prints them. The launcher passes the arguments through untouched. A tool also runs as `dotnet tools/<tool>.cs -- [arguments]`; without the `--`, dotnet takes some arguments for itself (`--help`, `--no-build`, `-p`, `--`).

| Tool | What it's for |
| --- | --- |
| `tools/run comet-postgres` | The shared development PostgreSQL, `comet-postgres` on `127.0.0.1:55432` (a Docker container that stays running, data in the `comet-postgres-data` volume), with a database per local stack. The other tools and the Unity editor start it themselves; `tools/run comet-postgres` alone lists its commands. |
| `tools/blender-export-glb.py` | Export each top-level object in a `.blend` file as a `.glb`, into the folder named by the file's `export_dir` property. Run it from Blender's Text Editor, or with `blender -b <file> --python tools/blender-export-glb.py`; the script's header has the setup. |
| `tools/run shapeland-dev` | Run ShapeLand locally: the web client in a browser, served by a local game server, without opening the Unity editor; or, with `--no-web`, just the servers, for the editor's play mode or a desktop build. It starts PostgreSQL (`tools/run comet-postgres`, with a database for the checkout and port), the Login server (on the game server's port + 10) and the game server, and restarts both servers when the content changes. The Unity editor can run the server itself for play mode, with optional bots, web build and local network ([Comet › Server](comet/README.md#the-local-game-server)), so most level work needs no terminal. |
| `tools/run shapeland-netrun` | Headless bot runs under a simulated network profile, printing what the bots measured (snap-backs, cheaters caught, ping, interpolation delay). |
| `tools/shared/NetProfiles.cs` | The network profiles `shapeland-dev --net` and `shapeland-netrun` use: `none`, `good` (about 80 ms round trip, 1% loss), `bad` (about 200 ms, 2% loss) and `awful` (about 200 ms, 5% loss). |
| `tools/run shapeland-deploy` | The deployed game: the page on Vercel and the game server on an on-demand AWS instance. |
| `tools/run unity-batch` | Run a Unity build or tests and return when the work is done: in the open editor when it has the project open, else in batch mode (where the editor often hangs while quitting). |
| `tools/run unity-restore` | Restore a Unity project's NuGet packages (see First time setup). Takes the project as an optional argument (default `shapeland/ShapeLand.Unity`). |

### Play locally in a browser

```sh
tools/run shapeland-dev                      # build if needed, start the server, open the page
tools/run shapeland-dev --bots 10 --bots-chat
tools/run shapeland-dev --net bad --bots 5   # everyone gets about 200 ms round trip and 2% loss (Docker)
tools/run shapeland-dev --lan                # let other devices on the network join
tools/run shapeland-dev --spawn spawn.tests.start   # start everyone at one spawn point, in any zone
```

| Option | What it does |
| --- | --- |
| `--rebuild` | Make the Unity web build even if it looks up to date. |
| `--no-build` | Use the last web build as it is (content still rebuilds). |
| `--bots N` | Start N honest bots (default 0): guest accounts named `Bot0001` and on, whose tokens are kept in `artifacts/bot-tokens`, so later runs play the same accounts. |
| `--cheaters N` | Start N speed-cheating bots (default 0). |
| `--bots-chat` | Let the bots chat now and then (they stay quiet otherwise). |
| `--port P` | The game server's port (default 5080); the Login server takes P + 10. |
| `--lan` | Listen on every network interface, so other devices on the network can join. |
| `--no-open` | Don't open the page in a browser. |
| `--net P` | Run the server and bots in Docker with a network profile on the server's link, so every client gets it (desktop builds too). |
| `--bots-net P` | With `--net`, a profile the bots get on top of it (default `none`), for example for high-ping bots. |

Close the Unity editor first: the build fails while it has the project open. Ctrl+C stops everything.

### Network runs

```sh
tools/run shapeland-netrun good
tools/run shapeland-netrun bad --seconds 300 --bots 20 --cheaters 2
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

Results also go to a file stamped with the profile and commit. The default port (5091) lets a `shapeland-dev` session keep running alongside.

### Unity batch jobs

```sh
tools/run unity-batch build Web ../../artifacts/unity/shapeland/web
tools/run unity-batch test PlayMode
tools/run unity-batch -- <Unity arguments>
```

`build` builds one of the build profiles in `Assets/Settings/Build Profiles/` (`Web`, `Linux`; the same ones as File › Build Profiles in the editor) into the given folder. `test` runs `PlayMode` (the default) or `EditMode` tests and writes the results (`Logs/results.xml`, or `Logs/results.json` from an open editor). Everything after `--` goes to a batch-mode editor as it is.

When a Unity editor has the project open, `build` and `test` run in it, through the [Unity CLI](https://docs.unity.com/en-us/unity-cli) (`unity` on the path) and the project's Unity Pipeline package. A build there needs the editor on the profile's platform already, since switching reimports the whole project. Without an open editor they run in batch mode. Options, before the job:

| Option | What it does |
| --- | --- |
| `-p project` | The Unity project (default `shapeland/ShapeLand.Unity`). |
| `-l logfile` | The editor's log, or an open editor's build report (default `Logs/batch.log`). Relative paths, here and in the job's arguments, are relative to the project. |
| `-t minutes` | The time limit (default 30). |

The exit code is the editor's (tests: 0 passed, 2 failed), 1 if it gave no result, and 124 on the time limit.

### Deploying

```sh
tools/run shapeland-deploy setup          # once per AWS account
tools/run shapeland-deploy page           # build the web page and publish it on Vercel
tools/run shapeland-deploy up --bots 1    # start the game server, with a bot so visitors see someone moving
tools/run shapeland-deploy status         # what's running, and whether the server answers
tools/run shapeland-deploy log            # the server's boot log (from its console)
tools/run shapeland-deploy down           # terminate it when done
tools/run shapeland-deploy redeploy       # page, then up, replacing a server that's already up
```

`up --hours H` sets when the server shuts itself down (0, the default, for never). `page --no-build` publishes the last web build as it is.

The server costs about 20 cents a day while it's up. When it's down, the page says the game isn't open for testing. Settings that aren't in the repo live in `~/.config/shapeland-deploy/env`: the first run on a computer creates it and stops until `ACME_EMAIL` (the contact address Caddy gives Let's Encrypt and ZeroSSL for the server's certificate) is filled in.

## Known quirks

- `TickLoopTests.RunsAtTheTickRate` can fail when every test suite runs at once; it passes on its own.

## AI usage

This project uses AI assisted programming in it's development. Code is reviewed by people and and they are responsible for any agent generated code.

Non-code creative work is made by people. This means game design docs, art, audio, level design, and in game text.
