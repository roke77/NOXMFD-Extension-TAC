# TAC page — planning

## Status

Phase 1 built (build steps 1–3); the two-player match test (step 4) is next. See
[Build order](#build-order-phase-1). This doc turns
[roke77/NOXMFD#93](https://github.com/roke77/NOXMFD/issues/93) into a two-phase plan and records
the design decisions settled so far.

**This extension is an experiment.** It exists to try TAC with real players, gather feedback and
refine it. If it proves useful, TAC may later be converted into a native NOXMFD feature — which is
also why it stays fully separate from NOXMFD's own pages until then.

- **Phase 1** — the whole command loop inside this extension. NOXMFD itself gains only new
  **extension API** surface (see [NOXMFD API additions](#noxmfd-api-additions-phase-1)), no new
  NOXMFD pages or features.
- **Phase 2** — everything else in the ticket (NOXMFD-side integration). Deliberately not planned
  yet; see [Phase 2](#phase-2-deferred).

## Source ticket

A **TAC** (mission control) page: one screen for a human controlling officer to coordinate targets
across squadrons and individual pilots. MAP answers *where is everything*, TGT/TD *what can I
target*, SQD *who is available*; TAC answers **who should attack what**. A command-and-control
board, not a mission planner. Full requirements in the ticket.

## Decisions

1. **The officer and the squad leaders install the extension.** That lets phase 1 close the loop
   (assign → leader sees it → leader acknowledges) on this extension's own page, without touching
   NOXMFD's TGT/TD/SQD pages. Squad members and squadless players can install it too, but TAC has
   nothing to do with them (decision 15).
2. **The officer is a pilot in a faction, spawned in an aircraft (parked is fine), and may fly.**
   Their board uses their own faction's picture (`FactionHQ`), which the page reads from NOXMFD's
   telemetry stream: each contact's name, stale flag and whether the officer has it selected.
   ADD needs the aircraft: NOXMFD's MAP selection is a real in-game target selection, which the
   game only allows with a local aircraft. PULL and REPORT work without one. A MAP pick mode that
   works without an aircraft would be a NOXMFD feature — phase 2.
3. **One TAC officer per faction, and they must NOT be in a squad.** Any player in the faction
   with the extension can press **APPLY FOR TAC** (EXT → TAC); for now that grants the role
   directly, with no approval step. A squad leader or squad member can't apply — TAC sits above
   the squads, not inside one. Nobody can apply while the role is held — for now there's no
   takeover; the holder has to release first. Everyone in the faction sees who holds it. Released
   explicitly (**RELEASE**), or automatically when the holder leaves, disconnects, changes
   faction, or joins/creates a squad.
4. **Board sources:** targets the officer **pulls** from what a squad leader has **reported**
   (decision 8), and targets the officer **adds** from their own current selection (picked on
   NOXMFD's MAP page). Nothing is auto-populated: only the officer puts a row on the board.
5. **Messages travel over NOXMFD's existing transport** (the Steam peer relay behind squads,
   `Squadron.cs`), exposed to extensions through new API — no second networking stack.
6. **Assigning to a squadron sends it to the squad leader.** Redistributing within the squad
   is the leader's call.
7. **ENGAGED is manual.** The squad leader presses **ENGAGING** on their TAC page. Nothing about a
   leader's locks is shared automatically.
8. **REPORT fills the squadron's PULL bucket; the officer pulls from it.** A leader's REPORT
   sends one of their locked enemies to the officer, where it waits in that squadron's PULL bucket
   — it does not touch the board. The squadron's **PULL ▼** shows how many reports are waiting;
   opening it lists them, and the officer adds the ones they want. A report leaves the bucket once
   its target is on the board (from any squadron's bucket, or ADD FROM MAP) or destroyed, and the
   bucket goes with its squadron. The leader's locks are never sent without a REPORT press.
   (Changed after the first two-player test: PULL used to fetch the leader's locks without a
   press, and REPORT put the target straight on the board, which was too aggressive.)
9. **A target can have several assignees** (e.g. VIPER and COBRA on one SAM site). The row lists
   them; the target is ENGAGED once any of them has pressed ENGAGING.
10. **Destroyed targets disappear.** No DESTROYED state (ticket req 8): the row, and every
    assignee's copy, is removed once the game drops the unit.
11. **The board lives for the mission, in memory.** Cleared when the mission ends or the officer
    releases/leaves the role; leaders' assignment lists clear with it.
12. **Simultaneous claims: the lower SteamID wins.** A holder that hears another player's `claim`
    while holding the role keeps it only if its own SteamID is lower; otherwise it steps down with a
    note. No clocks involved: two PCs' clocks can be seconds apart, so a claim time can't decide a
    race.
13. **Joining a squad loses TAC.** If the officer creates a squad, or accepts an invite into one,
    TAC is released (decision 3 applied strictly). Phase 1 can't stop the squad actions themselves —
    those are NOXMFD's own SQD features — so the extension reacts to the squad state instead.
14. **Tested with a second player.** Everything interesting needs at least two NOXMFD clients in
    one faction (officer + a squad leader), so phase 1 is verified in a real match with a second
    player.
15. **TAC only deals with squad leaders.** No orders to, or data from, individual pilots or squad
    members: TAC assigns to squadrons, pulls from squadrons, and receives reports from squad
    leaders — the ticket's per-pilot assignment (req 12's `VIPER 2-1`) is out of scope.
16. **A squadron is its callsign + flight** (`VIPER 2`), not its leader — two squads can share a
    callsign, and a squad keeps its identity when leadership changes hands. **Assignments carry
    over a handover**: when a different player's `hello` arrives for a squadron the officer already
    knows, the officer's extension re-sends that squadron's assignments (with priority and ENGAGING
    state) to the new leader. A squadron that disbands, or whose leader goes silent (startup step
    5), has its assignments withdrawn — its rows fall back to UNASSIGNED if nobody else holds them.
17. **ADD, PULL and REPORT offer enemy targets only** (for now). The officer's MAP selection and a
    leader's locks can include friendly or neutral units; those aren't offered.
18. **The officer's board is sorted by priority** (CRITICAL first), then by when the target was
    added — matching the leader's assignment list.
19. **The officer's page is a squadron matrix** (design C of the TAC page alternatives): one row
    per target, one column per squadron, and each cell is the assignment control. It replaces the
    ASSIGN ▼ / UNASSIGN ▼ menus and the board-level PULL FROM SQUAD ▼ of the original mockup.
20. **No TARGETING action** on the officer's page: LOCATE and REMOVE are the only per-target
    actions.
21. **An ENGAGED cell withdraws only on a long press**; an unassigned or ASSIGNED cell toggles on a
    tap. A stray tap mid-fight must not pull a squadron off a target it's attacking.
22. **RELEASE needs the same long press.** It clears the whole board and every leader's list, so a
    tap only shows "hold to release".
23. **The squad leader's page is the canvas's "Squad leader view" board** as drawn: assignments
    with ENGAGING and ACQUIRE, ACQUIRE ALL, and REPORT on each enemy lock.

## Phase 1 scope

### Officer view (the holder of the TAC role)

The squadron matrix (decision 19), titled **TARGET CONTROL**, in NOXMFD's Lit Panel style:

- **Header**: title, `YOU ARE TAC` and **RELEASE**. Below it the counts (`12 TARGETS · 6
  SQUADRONS`), a short status line for messages, and **ADD FROM MAP ▼**.
- **Matrix**: columns **TARGET**, **OBS** (OBSERVED, or STALE in a dashed tag), **PRI** (colour-coded:
  CRITICAL red, HIGH amber, NORMAL white, LOW grey), then one column per squadron (callsign +
  flight, teal) with a **PULL ▼** button under its name. TARGET/OBS/PRI and the header row stay
  pinned while the squadron columns scroll sideways and the rows scroll down; the scrollbars are
  NOXMFD's thin grey ones. No distance (ticket req 4).
- **Cells** are lamp pushbuttons: unlit = not assigned, teal lamp `ASGN` = assigned, glowing green
  `ENGD` = engaged. Tap to assign or withdraw; an ENGAGED cell needs a long press (decision 21).
- **OBSERVED/STALE** comes from the officer's own faction picture — the same accuracy check
  NOXMFD's stale flag uses (`FactionHQ.IsTargetPositionAccurate`). A target missing from the
  frame's contacts (a jammed picture, or no longer known) shows STALE.
- **ADD FROM MAP ▼**: lists the enemy units in the officer's own current selection — the units they've selected by
  tapping them on NOXMFD's MAP page (the same in-game target selection TGT shows). Tap a unit to
  add it, or **ADD ALL**. Units already on the board show as `on board ✓` rather than being added
  twice. No long picker: the MAP is the picker.
- **PULL ▼** under a squadron, with an amber count of the targets its leader has reported and the
  officer hasn't added yet (decision 8). Opening it lists them the same way — tap to add, or
  **ADD ALL**; an added target leaves the list.
- **Bottom section**, pinned to the bottom of the page: SELECTED (the selected row's name), STATUS,
  **LOCATE** (MAP highlight via the existing `Api.SetSelectedUnit`) and **REMOVE** (drop the row,
  withdrawing every assignment) on one line; the four **PRIORITY** lamp buttons below it.
- Target identity is the game's `persistentID` everywhere (ticket reqs 15), so a target pulled
  twice, or reported by two leaders, is one row.

### Squad leader view

- **Header**: who holds TAC (the holder's player name), or `TAC: — NONE —` with a short "no TAC
  officer in your faction" note. A leader never sees APPLY FOR TAC (decision 3).
- **My squad's assignments** (only while someone holds TAC): each target TAC assigned to my
  squadron, with the priority TAC set, sorted by priority (CRITICAL first). Updates live as TAC
  assigns, reprioritises, unassigns or removes; a destroyed target disappears.
- **ENGAGING** toggle per assignment (decision 7) — pressing it again turns the officer's row back
  to ASSIGNED.
- **ACQUIRE** per row, plus **ACQUIRE ALL**: select the assigned targets in the leader's own
  in-game targeting, like TD's AQUIRE — through NOXMFD's `Api.SelectTarget`, which runs MAP's own
  tap-to-select path (detection gate, TGT filters, cockpit marker and beep) rather than this
  extension re-implementing those rules against the game.
- **REPORT TO TAC**: the leader's current locks; REPORT on one sends it to the squadron's PULL
  bucket on the officer's side (decision 8), and the row shows `SENT TO TAC ✓`.

### Everyone else (squad members, squadless players)

- Who holds TAC right now.
- **APPLY FOR TAC** while nobody holds it — only for a player who isn't in a squad (decision 3).
  The holder sees **RELEASE** instead.

### Target status

UNASSIGNED → ASSIGNED (sent, nobody has pressed ENGAGING) → ENGAGED (ticket reqs 6–9). Observation
(OBSERVED ↔ STALE) changes independently.

### Messages (extension-level protocol)

All carried by the new API's per-extension messaging, so on the wire they're namespaced to this
extension and can't be mistaken for NOXMFD's own squad messages. Each is a JSON object
`{"t": <type>, ...}`:

| Message | Direction | Meaning |
|---|---|---|
| `claim` / `release` | holder → faction | "I hold / no longer hold TAC." `claim` repeats every 3 s so late joiners learn the holder. |
| `hello` | leader → holder | Answers each `claim`: "I lead a squad and run TAC". Carries nothing: the squad's callsign/flight come from the faction table. |
| `tasks` | holder → leader | The squadron's **whole** target list (id, name, priority, engaged), sent whenever it changes and again after every `hello`. Assigning, unassigning, reprioritising and removing all reach the leader this way; a lost message or a new leader after a handover heals within one claim. |
| `engaging` | leader → holder | On/off for one assigned target. |
| `report` | leader → holder | One locked enemy for the squadron's PULL bucket. |

Trust follows the squad protocol's model: `tasks` accepted only from the current holder;
`hello`/`report`/`engaging` accepted only by the holder, only from a faction peer
that leads a squad. `claim` is believed only from a faction-mate outside any squad. The officer checks a `hello` against
`Api.GetFactionPilots`: the sender must be the leader of the squad (callsign + flight) its own
presence beat announces, which NOXMFD already broadcasts faction-wide.

### Startup and roster

How the officer learns which squadrons exist (the ASSIGN ▼ / PULL FROM SQUAD ▼ lists). The target
board itself still starts empty and fills only from ADD and PULL (decision 4).

1. **A player takes TAC.** Their extension sends `claim` (with the claim time) to every faction-mate
   running NOXMFD.
2. **Leaders answer.** Every squad leader running the extension replies with `hello` (callsign,
   flight, designation). The officer's ASSIGN/PULL lists fill from these within a second or two.
3. **`claim` repeats every few seconds.** Covers a leader who joins the match late, a player who
   creates a squad later, and a lost message; each new leader answers with its own `hello`.
4. **Squad changes.** The next `hello` after a change carries it, since the officer reads the
   squad's callsign/flight from the faction table: a renamed squadron replaces its old column at
   once, and a new leader's `hello` for a known squadron is a handover — the full `tasks` list,
   ENGAGING included, goes to the new leader (decision 16).
5. **Going stale.** A squadron whose leader hasn't answered for 10 s (three missed claims) leaves
   the matrix, and its assignments are withdrawn.
6. **Release.** When the officer releases TAC, leaves, changes faction, or joins a squad, their
   extension sends `release`, and every leader clears its assignments list.
7. **Silent holder.** A crash or dropped connection never sends `release`, so every player treats
   TAC as gone once it hears no `claim` for ~15 s: leaders clear their assignments, and APPLY FOR
   TAC reappears for squadless players. A leader that changes faction likewise stops answering and
   drops off the officer's lists (step 5).

A squad whose leader doesn't run the extension never appears: NOXMFD's faction table knows every
squad and its leader, but only a leader running TAC can receive assignments.

## NOXMFD API additions (phase 1)

The only change phase 1 makes to NOXMFD, built as extension API version 8 on NOXMFD's
`feature/ext-squad-messaging-api` branch (NOXMFD's `docs/extension-messaging.md`, `EXTENSIONS.md`
surfaces 10–12). Everything else above is this extension's own code, or game state it reads
directly.

1. **Who's out there** — `Api.LocalSteamId` and `Api.GetFactionPilots()`: the faction-mates
   currently running NOXMFD, each with SteamID, Steam name, designation and the squad they announced
   (callsign, flight, leader) — from `PlayerRoster`, `Presence` and the faction identity table.
2. **Squad state, read-only** — `Api.GetLocalSquad()`: own role (none/leader/member), squad
   callsign, flight and leader, and own designation (`VIPER 2-1`).
3. **Per-extension messaging** — `Api.SendMessage(id, to, payload)` and
   `Api.SetMessageHandler(id, handler)`. NOXMFD prefixes the wire type (`ext.<id>`), so an extension
   can't forge or intercept NOXMFD's own `sqd.*` messages; payloads up to 4096 UTF-8 bytes; sends
   only to peers known to run NOXMFD; handlers run on the main thread.
4. **Selecting a target** — `Api.SelectTarget(unitId)`, for ACQUIRE: MAP's own tap-to-select path.
5. **Docs + version** — this extension's `BepInDependency` floor raised to the NOXMFD release that
   ships API version 8, and `lib/NOXMFD.dll` refreshed from it.

Already available and used as-is: `RegisterExtension` (+ its command handler), `PublishSlice`,
`SetSelectedUnit`, `SetSelectedUnitTrack`.

## Build order (phase 1)

1. **Officer board, extension only.** The board bookkeeping in C# (add, remove, priority, assign,
   LOCATE), published as this extension's slice; the matrix page, reading names, OBSERVED/STALE and
   the officer's MAP selection from NOXMFD's telemetry stream the way the ATC extension does; a
   `tools/preview.py` harness with mock data. Works in a single-player mission with no squadron
   columns yet.
2. **NOXMFD API additions** (a NOXMFD feature branch): the four items above. Built; needs its
   two-client live check before it ships in a NOXMFD release.
3. **Role and messaging**, on `feature/tac-phase1`, one commit per piece as it works in the
   preview: claim/release, `hello` and the squadron roster, assign/unassign/priority delivery,
   ENGAGING, PULL and REPORT, the squad leader and everyone-else views, destroyed-target removal.
   Built and checked in the preview; needs step 4.
4. **Two-player test** in a real match (decision 14, [`two-player-test.md`](two-player-test.md)), covering this extension and NOXMFD's API 8
   together. NOXMFD's API branch ships in a NOXMFD release only after it passes; this extension's
   release follows, pinned to that NOXMFD version.

## Phase 2 (deferred)

**Rule: this extension never adds anything to NOXMFD's own pages** (no buttons, panels or layers on
MAP, TGT, TD, SQD, ...). Everything TAC does lives on its own EXT → TAC page; NOXMFD itself only
gains generic extension API. So these ticket requirements are **out of scope for this extension**, not deferred (a native
NOXMFD version, if the experiment validates TAC, would revisit them): SEND
TO TAC on TGT (req 16), REPORT TO TAC on the squad side (req 17), assignments shown on TD/TGT
(req 13), opening a target in TGT (req 19), and new MAP layers/labels/styling for TAC targets
(reqs 24–27, 29). Assigning to individual pilots (req 12) is out too (decision 15).

Phase 2 is whatever's left after phase 1 works — not planned yet.
