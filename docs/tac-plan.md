# TAC page — planning

## Status

Planning. Scaffold only (an empty "COMING SOON" TAC page). This doc turns
[roke77/NOXMFD#93](https://github.com/roke77/NOXMFD/issues/93) into a two-phase plan and records
the design decisions settled so far.

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
   Their board uses their own faction's picture (`FactionHQ`), read directly by this extension.
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
4. **Board sources:** targets **pulled** from a squadron, targets **reported** by a squad leader,
   and targets the officer **adds** from their own current selection (picked on NOXMFD's MAP page).
   Nothing is auto-populated.
5. **Messages travel over NOXMFD's existing transport** (the Steam peer relay behind squads,
   `Squadron.cs`), exposed to extensions through new API — no second networking stack.
6. **Assigning to a squadron sends it to the squad leader.** Redistributing within the squad
   is the leader's call.
7. **ENGAGED is manual.** The squad leader presses **ENGAGING** on their TAC page. Nothing about a
   leader's locks is shared automatically.
8. **PULL FROM SQUAD answers automatically.** The leader's extension replies to the officer's
   explicit request with the leader's current lock list; the officer picks which to add. A one-off
   answer to a request, not a continuous share.
9. **A target can have several assignees** (e.g. VIPER and COBRA on one SAM site). The row lists
   them; the target is ENGAGED once any of them has pressed ENGAGING.
10. **Destroyed targets disappear.** No DESTROYED state (ticket req 8): the row, and every
    assignee's copy, is removed once the game drops the unit.
11. **The board lives for the mission, in memory.** Cleared when the mission ends or the officer
    releases/leaves the role; leaders' assignment lists clear with it.
12. **Simultaneous claims: the earlier claim wins.** Each `claim` carries its claim time; a holder
    that sees an earlier claim from another player steps down.
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
17. **PULL and REPORT offer enemy targets only** (for now). A leader's locks can include friendly or
    neutral units; those aren't offered.
18. **The officer's board is sorted by priority** (CRITICAL first), then by when the target was
    added — matching the leader's assignment list.

## Phase 1 scope

### Officer view (the holder of the TAC role)

- **Target board**: every target on the board, header count (`12 TARGETS`); each row shows the
  target, **OBSERVED/STALE**, **priority**, and the assignees with their state
  (`VIPER / ENGAGED`, `— / UNASSIGNED`). No distance (ticket req 4).
- **OBSERVED/STALE** comes from the officer's own faction picture — the same accuracy check
  NOXMFD's stale flag uses (`FactionHQ.IsTargetPositionAccurate`).
- **Board-level actions** (header): **ADD ▼** and **PULL FROM SQUAD ▼** — both only put targets on
  the board; assigning is a separate step.
- **ADD ▼**: lists the officer's own current selection — the units they've selected by tapping
  them on NOXMFD's MAP page (the same in-game target selection TGT shows), read straight from the
  game. Tap a unit to add it, or **ADD ALL**. Units already on the board show as `on board ✓`
  rather than being added twice. No long picker: the MAP is the picker.
- **PULL FROM SQUAD ▼**: pick a squadron; its leader's extension replies with the leader's current
  locks (decision 8), listed the same way — tap to add, or **ADD ALL**.
- **Detail strip** on the selected row: SELECTED, STATUS, **PRIORITY** (LOW / NORMAL / HIGH /
  CRITICAL), **ASSIGN ▼** (a squadron), **LOCATE** (MAP highlight via the existing
  `Api.SetSelectedUnit`/`SetSelectedUnitTrack`), **TARGETING** (select it in the officer's own
  in-game targeting), **UNASSIGN** (withdraw one assignee) and **REMOVE** (drop the row,
  withdrawing every assignment).
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
  in-game targeting, like TD's AQUIRE — the extension calls the game directly.
- **REPORT TO TAC**: the leader's current locks; pick one to send it to the officer's board.
- A brief "TAC pulled your targets" note when a PULL is answered (decision 8), since that reply
  goes out without a press.

### Everyone else (squad members, squadless players)

- Who holds TAC right now.
- **APPLY FOR TAC** while nobody holds it — only for a player who isn't in a squad (decision 3).
  The holder sees **RELEASE** instead.

### Target status

UNASSIGNED → ASSIGNED (sent, nobody has pressed ENGAGING) → ENGAGED (ticket reqs 6–9). Observation
(OBSERVED ↔ STALE) changes independently.

### Messages (extension-level protocol)

All carried by the new API's per-extension messaging, so on the wire they're namespaced to this
extension and can't be mistaken for NOXMFD's own squad messages. Sketch:

| Message | Direction | Meaning |
|---|---|---|
| `claim` / `release` | holder → faction | "I hold / no longer hold TAC." Re-announced periodically so late joiners learn the holder. |
| `hello` | leader → holder | "I lead a squad and run TAC": my squad's callsign/flight and my designation. Builds the officer's ASSIGN/PULL lists. |
| `assign` / `unassign` | holder → assignee | Target id, name, priority. |
| `priority` | holder → assignees | Priority changed. |
| `engaging` | assignee → holder | On/off for one assigned target. |
| `pull` / `pull-reply` | holder → leader → holder | Request for, and the leader's current lock list. |
| `report` | leader → holder | One target to add to the board. |

Trust follows the squad protocol's model: `assign`/`priority` accepted only from the current
holder; `report`/`engaging`/`pull-reply` accepted only by the holder, only from a faction peer that
announced itself as a squad leader (`hello`). That's self-reported — the officer has no way to see
another squad's roster — which is acceptable for a same-faction coordination board.

### Startup and roster

How the officer learns which squadrons exist (the ASSIGN ▼ / PULL FROM SQUAD ▼ lists). The target
board itself still starts empty and fills only from ADD, PULL and REPORT (decision 4).

1. **A player takes TAC.** Their extension sends `claim` (with the claim time) to every faction-mate
   running NOXMFD.
2. **Leaders answer.** Every squad leader running the extension replies with `hello` (callsign,
   flight, designation). The officer's ASSIGN/PULL lists fill from these within a second or two.
3. **`claim` repeats every few seconds.** Covers a leader who joins the match late, a player who
   creates a squad later, and a lost message; each new leader answers with its own `hello`.
4. **Squad changes.** A leader sends a fresh `hello` when its squad changes (new callsign/flight,
   disband, leadership passing on). A new leader starts sending `hello`; the old one stops — and
   the officer re-sends the squadron's assignments to the new leader (decision 16).
5. **Going stale.** A leader that stops answering `claim` drops out of the ASSIGN/PULL lists.
6. **Release.** When the officer releases TAC, leaves, changes faction, or joins a squad, their
   extension sends `release`, and every leader clears its assignments list.
7. **Silent holder.** A crash or dropped connection never sends `release`, so every player treats
   TAC as gone once it hears no `claim` for ~15 s: leaders clear their assignments, and APPLY FOR
   TAC reappears for squadless players. A leader that changes faction likewise stops answering and
   drops off the officer's lists (step 5).

A squad whose leader doesn't run the extension never appears: NOXMFD's faction-wide presence beacon
only says who runs NOXMFD, not who leads which squad.

## NOXMFD API additions (phase 1)

The only change phase 1 makes to NOXMFD. Everything else above is this extension's own code, or
game state it reads directly.

1. **Who's out there** — own SteamID, plus the faction-mates currently running NOXMFD (name +
   SteamID). Built from what already exists: `PlayerRoster` and `Presence.HasNoxmfd`.
2. **Squad state, read-only** — own role (none/leader/member), callsign, flight and own designation
   (`VIPER 2-1`). A subset of what `/squad` already serves; nothing about other members is needed,
   since TAC only deals with leaders.
3. **Per-extension messaging** — send a small text payload to one peer, and register a handler for
   payloads addressed to this extension. NOXMFD prefixes the wire type with the extension id, so an
   extension can't forge or intercept NOXMFD's own `sqd.*` messages; same size cap and envelope
   rules as the squad transport; handlers run on the main thread.
4. **Docs + version** — an `EXTENSIONS.md` section for the new surfaces, and this extension's
   `BepInDependency` floor raised to the NOXMFD version that ships them.

Already available and used as-is: `RegisterExtension` (+ its command handler), `PublishSlice`,
`SetSelectedUnit`, `SetSelectedUnitTrack`.

## Phase 2 (deferred)

**Rule: this extension never adds anything to NOXMFD's own pages** (no buttons, panels or layers on
MAP, TGT, TD, SQD, ...). Everything TAC does lives on its own EXT → TAC page; NOXMFD itself only
gains generic extension API. So these ticket requirements are **out of scope**, not deferred: SEND
TO TAC on TGT (req 16), REPORT TO TAC on the squad side (req 17), assignments shown on TD/TGT
(req 13), opening a target in TGT (req 19), and new MAP layers/labels/styling for TAC targets
(reqs 24–27, 29). Assigning to individual pilots (req 12) is out too (decision 15).

Phase 2 is whatever's left after phase 1 works — not planned yet.
