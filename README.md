# NOXMFD Extension: TAC Module

[![NOXMFD](https://img.shields.io/badge/Requires-NOXMFD-blue)](https://github.com/roke77/NOXMFD)
![Version](https://img.shields.io/badge/Version-0.1.0-green)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Adds a **TAC** page under [NOXMFD](https://github.com/roke77/NOXMFD)'s EXT nav — a
target-coordination board for one player per faction acting as the TAC officer (mission control),
working with the leaders of the faction's squads. The officer collects targets onto a shared
board (ADD from their MAP selection, PULL FROM SQUAD, or a leader's REPORT), sets each one's
priority, and assigns it to squadrons; squad leaders see their assignments, ACQUIRE them and mark
them ENGAGING. Every target shows OBSERVED/STALE and UNASSIGNED/ASSIGNED/ENGAGED.

**Status: planning.** The page is an empty "COMING SOON" scaffold. The design is settled in
[`docs/tac-plan.md`](docs/tac-plan.md); phase 1 first needs new NOXMFD extension API (faction
roster, read-only squad state, per-extension messaging), listed there.

This extension is an experiment: try TAC with real players, gather feedback and refine it. If it
proves useful, TAC may later become a native NOXMFD feature.

Built entirely through NOXMFD's public extension API — see NOXMFD's
[`EXTENSIONS.md`](https://github.com/roke77/NOXMFD/blob/main/EXTENSIONS.md). This repo does
**not** modify NOXMFD's own source.

## What's here

- `src/plugin/Plugin.cs` — registers the **TAC** EXT page.
- `src/plugin/TacPageAssets.cs` — embedded-resource lookup for `src/web/`'s HTML/CSS/JS.
- `src/web/tac.{html,css,js}` — the page itself, currently an empty "COMING SOON" placeholder.
- `docs/` — planning doc (design decisions, phase-1 scope, message protocol, the NOXMFD API it
  needs) — see [`tac-plan.md`](docs/tac-plan.md).
- `lib/NOXMFD.dll` — compile-time reference only, not shipped to players (NOXMFD is already
  installed as its own plugin; `Private=false` in the `.csproj` keeps this project from bundling a
  second copy). Committed to this repo since it's this project's own dependency.

## Building

Requires a local Nuclear Option install with BepInEx 5 and NOXMFD already installed. Create a
gitignored `GameDir.props` next to the `.csproj` if your install isn't the default Steam path:

```xml
<Project><PropertyGroup>
  <GameDir>D:\SteamLibrary\steamapps\common\Nuclear Option</GameDir>
</PropertyGroup></Project>
```

Then:

```bash
dotnet build TacModule.csproj -c Release
```

The build's `DeployToGame` target copies the built DLL straight into
`$(GameDir)\BepInEx\plugins\` for you.

## Installing

1. Install BepInEx 5 and [NOXMFD](https://github.com/roke77/NOXMFD).
2. Drop `NOXMFD.TacModule.dll` into `BepInEx/plugins/`.
3. Launch the game — a **TAC** entry appears under NOXMFD's EXT nav.
