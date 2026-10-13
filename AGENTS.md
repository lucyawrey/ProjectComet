# Agent guide

Notes for coding agents (and anyone else) working in this repo. Requirements, layout, setup and the scripts are in `README.md`; the proposal is in `docs/`.

**Names:** *Comet* is the shared base (servers, libraries, Unity package, tools); *Anima* is the working name of the game built on it; *ShapeLand* is Comet's small demo and reference game. *ProjectComet* covers the whole project.

## Ground rules

- **Human-made creative content only.** Agents help with code. No AI-generated models, textures, readable in-game text, music or sound; plain interface labels and messages are the exception. An agent may generate a starting point when asked (such as meshes exported for Blender), but never commits it: it's handed to a person, who edits it and checks it in.
- **Comet keeps thin mechanisms; game policy is game code.** Anything specific to one game belongs in that game, not in `comet/`.
- **The stack:** a Unity client with web export as a main target, plain .NET servers, and WebSocket for every client. Servers and shared libraries are C#.
- **`docs/` is written by people.** Don't write or rewrite its pages unless asked.

## Building and testing

- `dotnet build ProjectComet.slnx` and `dotnet test --solution ProjectComet.slnx`.
- Run Unity batch jobs (tests, builds) through `tools/unity-batch.sh`: the editor often hangs while quitting. Close the Unity editor before a build; it fails while the editor has the project open.
- Run `tools/unity-restore.sh` after a fresh clone and whenever `shapeland/ShapeLand.Unity/Assets/packages.config` changes.
- Play mode and Unity's play-mode tests read `shapeland/ShapeLand.Unity/Assets/StreamingAssets/content.bin`, an ignored copy of `artifacts/content/shapeland/content.bin` that the editor refreshes when it loads and as play mode starts. Rebuild the content after changing it, or they use the old content.
- `TickLoopTests.RunsAtTheTickRate` can miss when every test suite runs at once; it passes alone.

## Docs and git

- `docs/` pages are Markdown named by page title, with spaces and capitals (they don't follow the repo's lowercase convention). The repo is also an Obsidian vault (config in `.obsidian/`).
- Changes go through feature branches and squash-merged pull requests into `main`, so the PR title and description become the commit message: write them like one.
