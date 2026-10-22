# NOXMFD Extension: TAC Module

[![NOXMFD](https://img.shields.io/badge/Requires-NOXMFD%200.52.0%2B-blue)](https://github.com/roke77/NOXMFD)
![Version](https://img.shields.io/badge/Version-0.1.0-green)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Adds a **TAC** page under [NOXMFD](https://github.com/roke77/NOXMFD)'s EXT nav — a
target-coordination board for one player per faction acting as the TAC officer (mission control),
working with the leaders of the faction's squads. The officer collects targets onto a shared
board (ADD from their MAP selection, PULL FROM SQUAD, or a leader's REPORT), sets each one's
priority, and assigns it to squadrons; squad leaders see their assignments, ACQUIRE them and mark
them ENGAGING. Every target shows OBSERVED/STALE and UNASSIGNED/ASSIGNED/ENGAGED.

**Status: in development.** Built against NOXMFD's unreleased extension API 8 (faction roster, own
squad, messaging, target selection). Phase 1 is built: the TAC role, the officer's squadron matrix
(ADD FROM MAP, PULL, assignments, priority, LOCATE, REMOVE), the squad leader's page (ENGAGING,
ACQUIRE, REPORT) and destroyed-target removal. It has only run in the browser preview so far; the
two-player match test is next. The design and build order are in
[`docs/tac-plan.md`](docs/tac-plan.md).

This extension is an experiment: try TAC with real players, gather feedback and refine it. If it
proves useful, TAC may later become a native NOXMFD feature.

Built entirely through NOXMFD's public extension API — see NOXMFD's
[`EXTENSIONS.md`](https://github.com/roke77/NOXMFD/blob/main/EXTENSIONS.md). This repo does
**not** modify NOXMFD's own source.

## What's here

- `src/plugin/Plugin.cs` — checks NOXMFD has extension messaging (API 8), registers the **TAC** EXT
  page and message handler, and spawns the persistent `TAC_Worker` that ticks the session.
- `src/plugin/TacRole.cs` — who holds the TAC role: APPLY/RELEASE, the lower-SteamID claim race,
  losing the role on joining a squad or leaving the faction, the silent-holder timeout. Pure.
- `src/plugin/TacSession.cs` — the live glue: claim/release messages to faction-mates, the board
  gated to the role's holder, and the page's slice (role + board).
- `src/plugin/TacRoster.cs` — the officer's squadron roster: one entry per squadron whose leader
  runs TAC, keyed by callsign + flight, with handover, rename and silence. Pure.
- `src/plugin/TacLeader.cs` — a squad leader's side: the assignments TAC sent, ENGAGING, ACQUIRE,
  REPORT, and the reply to a PULL.
- `src/plugin/TacGame.cs` — game reads the API doesn't cover: the pilot's locked enemies, whether a
  target is gone, whether a reported unit is an enemy.
- `src/plugin/TacBoard.cs` — the officer's in-memory board and its commands (add, remove, priority,
  assign, unassign, locate), and each squadron's task list.
- `src/plugin/TacJson.cs` — JSON string escaping for the slice and messages. Pure.
- `src/plugin/TacPageAssets.cs` — embedded-resource lookup for `src/web/`'s HTML/CSS/JS.
- `src/web/tac.{html,css,js}` — the page, in NOXMFD's Lit Panel style: the officer's squadron
  matrix, the squad leader's assignments and REPORT list, and everyone else's role view (holder,
  APPLY FOR TAC). Reads names, OBSERVED/STALE and the pilot's own selection from NOXMFD's telemetry.
- `src/web/tac-board.js` — pure board helpers (sort, status, cell state, ADD list), checked by
  `node src/web/tac-board.test.js`.
- `tools/preview.py` — browser preview with a mock board, 6 squadrons, mock squad leaders that
  answer PULL, and every role state, no game needed (`python tools/preview.py [port]`).
- `tools/tests/` — xUnit tests for the pure C# (`dotnet test tools/tests`), compiled straight from
  `src/plugin/` like NOXMFD's own `tools/tests`.
- `docs/` — planning doc (design decisions, phase-1 scope, message protocol, the NOXMFD API it
  needs) — see [`tac-plan.md`](docs/tac-plan.md) — and the two-player match test,
  [`two-player-test.md`](docs/two-player-test.md).
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
