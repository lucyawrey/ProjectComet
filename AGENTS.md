# Agent guide

Notes for coding agents (and anyone else) working in this repo. Requirements, layout, setup and the tools are in `README.md`; how code is written and reviewed is in `CONTRIBUTING.md`; the proposal is in `docs/`.

**Names:** *Comet* is the shared base (servers, libraries, Unity package, tools); *Crystal* is the working name of the game built on it; *ShapeLand* is Comet's small demo and reference game. *ProjectComet* covers the whole project.

## Ground rules

- **Human-made creative content only.** Agents help with code. No AI-generated models, textures, in-game text, music or sound. Interface text (buttons, menus, hints, messages) is written by people too: where a change needs some, leave a placeholder in square brackets that says what's needed, such as `[button that saves the zone]`, and `tools/run check` lists any that remain. Labels that name a data field one to one are the exception. An agent may generate a starting point when asked (such as meshes exported for Blender), but never commits it: it's handed to a person, who edits it and checks it in.
- **Comet keeps thin mechanisms; game policy is game code.** Anything specific to one game belongs in that game, not in `comet/`.
- **The stack:** a Unity client with web export as a main target, plain .NET servers, and WebSocket for every client. Servers and shared libraries are C#.
- **American English** in code, interface text and technical docs (color, center, meter, normalize).
- **`docs/` is written by people.** Don't write or rewrite its pages unless asked.

## Building and testing

- `dotnet build ProjectComet.slnx` and `dotnet test --solution ProjectComet.slnx`.
- Format C# with `dotnet csharpier format .` before committing; `tools/run check` runs the formatting check, the content build and the tests.
- Run Unity tests and builds through `tools/run unity-batch` (`build <profile> <output>`, `test`): it uses the open editor when one has the project, else batch mode, where the editor often hangs while quitting. Builds are the build profiles in `Assets/Settings/Build Profiles/`.
- `tools/run unity-restore` restores Unity's NuGet packages: `unity-batch` runs it when a checkout has never been restored or `shapeland/ShapeLand.Unity/Assets/packages.config` changed; run it yourself before opening such a checkout in the editor.
- Play mode and Unity's play-mode tests read `shapeland/ShapeLand.Unity/Assets/StreamingAssets/content.bin`, an ignored copy of `artifacts/content/shapeland/content.bin` that the editor refreshes when it loads and as play mode starts. Rebuild the content after changing it, or they use the old content.
- Play mode joins the editor's server without accounts unless Comet › Server › Use Accounts is on; play-mode tests set it themselves. Tests that need the Login server (and Docker) are in the `Accounts` category: `tools/run unity-batch test PlayMode -- -testCategory "!Accounts"` skips them.

## Docs and git

- `docs/` pages are Markdown named by page title, with spaces and capitals (they don't follow the repo's lowercase convention). The repo is also an Obsidian vault (config in `.obsidian/`).
- Changes go through feature branches and squash-merged pull requests into `main`, so the PR title and description become the commit message: write them like one.
- No AI attribution in history: commits are authored by the person running the agent, with no `Co-authored-by` trailers for agents, and PR descriptions have no "Generated with/by" or session-link footers. Remove any that a tool adds before opening the PR.
