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

1. **Everyone installs the extension.** The officer *and* the pilots run it. That lets phase 1
   close the loop (assign → pilot sees it → pilot acknowledges) on this extension's own page,
   without touching NOXMFD's TGT/TD/SQD pages.
2. **The officer is a pilot in a faction, and may fly.** Their board uses their own faction's
   picture (`FactionHQ`), read directly by this extension.
3. **One TAC officer per faction, and they must be a squad leader.** Claimed from the TAC page;
   everyone in the faction sees who holds it. Released explicitly, or automatically when the holder
   leaves, disconnects, or stops leading a squad.
4. **Board sources:** targets **pulled** from a squadron/pilot, targets **reported** by a pilot,
   and targets the officer **adds manually** from their own faction's picture. Nothing is
   auto-populated.
5. **Messages travel over NOXMFD's existing transport** (the Steam peer relay behind squads,
   `Squadron.cs`), exposed to extensions through new API — no second networking stack.
6. **Assigning to a squadron sends it to the squad leader only.** Redistributing within the squad
   is the leader's call.
7. **ENGAGED is manual.** An assignee presses **ENGAGING** on their TAC page; for a squadron
   assignment that's the leader. Nothing about a pilot's locks is shared automatically.
8. **PULL FROM SQUAD answers automatically.** The pilot's extension replies to the officer's
   explicit request with its current lock list; the officer picks which to add. A one-off answer
   to a request, not a continuous share.
9. **A target can have several assignees** (e.g. VIPER and COBRA on one SAM site). The row lists
   them; the target is ENGAGED once any of them has pressed ENGAGING.
10. **Destroyed targets disappear.** No DESTROYED state (ticket req 8): the row, and every
    assignee's copy, is removed once the game drops the unit.
11. **The board lives for the mission, in memory.** Cleared when the mission ends or the officer
    releases/leaves the role; pilots' assignment lists clear with it.
12. **Simultaneous claims: the earlier claim wins.** Each `claim` carries its claim time; a holder
    that sees an earlier claim from another leader steps down.
13. **Losing squad leadership loses TAC.** If the officer's leadership passes to someone else
    mid-mission, TAC is released (decision 3 applied strictly).
14. **Tested with a second player.** Everything interesting needs at least two NOXMFD clients in
    one faction (officer + pilot), so phase 1 is verified in a real match with a second player.

## Phase 1 scope

### Officer view (the holder of the TAC role)

- **Target board**: every target on the board, header count (`12 TARGETS`); each row shows the
  target, **OBSERVED/GHOST**, **priority**, and the assignees with their state
  (`VIPER / ENGAGED`, `— / UNASSIGNED`). No distance (ticket req 4).
- **OBSERVED/GHOST** comes from the officer's own faction picture — the same accuracy check
  NOXMFD's stale flag uses (`FactionHQ.IsTargetPositionAccurate`).
- **Detail strip** on the selected row: SELECTED, STATUS, **PRIORITY** (LOW / NORMAL / HIGH /
  CRITICAL), **ASSIGN ▼** (a squadron or a pilot), **PULL FROM SQUAD ▼**, **LOCATE** (MAP
  highlight via the existing `Api.SetSelectedUnit`/`SetSelectedUnitTrack`), **TARGETING** (select
  it in the officer's own in-game targeting), **UNASSIGN** (withdraw one assignee) and **REMOVE**
  (drop the row, withdrawing every assignment).
- **ADD**: pick any enemy in the officer's faction picture onto the board.
- Target identity is the game's `persistentID` everywhere (ticket reqs 15), so a target pulled
  twice, or reported by two pilots, is one row.

### Pilot view (everyone else running the extension)

- **My assignments**: targets TAC assigned to me (or to my squad, if I lead it), with priority.
- **ENGAGING** toggle per assignment (decision 7).
- **ACQUIRE**: select the assigned targets in-game in one press, like TD's AQUIRE — the extension
  calls the game directly.
- **REPORT TO TAC**: send one of my currently locked targets to the officer's board.
- Who holds TAC right now; a squad leader also sees **TAKE TAC** / **RELEASE**.

### Target status

UNASSIGNED → ASSIGNED (sent, nobody has pressed ENGAGING) → ENGAGED (ticket reqs 6–9). Observation
(OBSERVED ↔ GHOST) changes independently.

### Messages (extension-level protocol)

All carried by the new API's per-extension messaging, so on the wire they're namespaced to this
extension and can't be mistaken for NOXMFD's own squad messages. Sketch:

| Message | Direction | Meaning |
|---|---|---|
| `claim` / `release` | holder → faction | "I hold / no longer hold TAC." Re-announced periodically so late joiners learn the holder. |
| `hello` | pilot → holder | "I run TAC": my designation, my squad, whether I lead it. Builds the officer's ASSIGN/PULL lists. |
| `assign` / `unassign` | holder → assignee | Target id, name, priority. |
| `priority` | holder → assignees | Priority changed. |
| `engaging` | assignee → holder | On/off for one assigned target. |
| `pull` / `pull-reply` | holder → pilot → holder | Request for, and the pilot's current lock list. |
| `report` | pilot → holder | One target to add to the board. |

Trust follows the squad protocol's model: `assign`/`priority` accepted only from the current
holder; `report`/`engaging`/`pull-reply` accepted only by the holder, only from faction peers.

## NOXMFD API additions (phase 1)

The only change phase 1 makes to NOXMFD. Everything else above is this extension's own code, or
game state it reads directly.

1. **Who's out there** — own SteamID, plus the faction-mates currently running NOXMFD (name +
   SteamID). Built from what already exists: `PlayerRoster` and `Presence.HasNoxmfd`.
2. **Squad state, read-only** — own role (none/leader/member), callsign, flight, own designation
   (`VIPER 2-1`), leader, members with their slots. The same data `/squad` already serves.
3. **Per-extension messaging** — send a small text payload to one peer, and register a handler for
   payloads addressed to this extension. NOXMFD prefixes the wire type with the extension id, so an
   extension can't forge or intercept NOXMFD's own `sqd.*` messages; same size cap and envelope
   rules as the squad transport; handlers run on the main thread.
4. **Docs + version** — an `EXTENSIONS.md` section for the new surfaces, and this extension's
   `BepInDependency` floor raised to the NOXMFD version that ships them.

Already available and used as-is: `RegisterExtension` (+ its command handler), `PublishSlice`,
`SetSelectedUnit`, `SetSelectedUnitTrack`.

## Phase 2 (deferred)

Everything the ticket asks of NOXMFD's own pages — e.g. SEND TO TAC on TGT (req 16), assignments
showing the way a TD designation does (req 13), REPORT TO TAC from the squad side (req 17) — and
anything else not listed in phase 1. Not planned until phase 1 works.
