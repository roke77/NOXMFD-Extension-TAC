# Two-player test (phase 1, build step 4)

One match that checks this extension and NOXMFD's extension API 8 together (docs/tac-plan.md,
decision 14). Nothing below can run in the browser preview: it needs two NOXMFD clients in one
faction.

## Setup

Both players, before launching:

- **NOXMFD** built from NOXMFD's `feature/ext-squad-messaging-api` branch (`dotnet build -c
  Release`), in `BepInEx/plugins`. The released NOXMFD doesn't have API 8: TAC then logs
  `[TAC] this NOXMFD has no extension messaging (API 8) — TAC disabled` and does nothing.
- **TAC** built from this repo's `feature/tac-phase1` branch (`dotnet build TacModule.csproj -c
  Release`), in `BepInEx/plugins`.

In the match: both players in the **same faction**, both **spawned in an aircraft** (the officer can
sit parked). **A** plays TAC officer and stays **out of any squad**. **B** creates a squad
(SQD → CREATE SQUAD, e.g. `VIPER 1`) and so becomes its leader. Each opens EXT → TAC.

If anything fails, keep both players' `BepInEx/LogOutput.log` and note the clock time of the step.
Grep them for:

- `[TAC]` — role changes (`TAC holder: X -> Y`), squadrons joining/leaving the matrix, refusals
  with their reason (APPLY refused, a `hello` the faction table doesn't back, a sender NOXMFD
  doesn't list yet).
- `[TAC diag` — time-stamped message traffic: tasks sent/received, ENGAGING, REPORT (and where it went), ACQUIRE.
- `[NOXMFD ext-link` — the first TAC message each way per player, and `[NOXMFD] message for
  extension 'tac' … dropped: not installed here` when the other side lacks TAC.
- `[NOXMFD squad-link` — the Steam transport underneath (sessions, failed sends).

The first line from TAC, `TAC <version> loaded`, confirms the right build is installed.

## Steps

| # | Do | Expect |
|---|---|---|
| 1 | Both in the faction, nobody applied | A: NO TAC OFFICER IN YOUR FACTION + APPLY FOR TAC. B: the leader's "no TAC officer" text, no APPLY. |
| 2 | A presses APPLY FOR TAC | A gets TARGET CONTROL / YOU ARE TAC. Within ~3 s B's page shows TAC: A (A's callsign, or Steam name) and B's assignment page. |
| 3 | Wait ~3 s on A | A's matrix shows a `VIPER 1` column, `1 SQUADRONS`. |
| 4 | A selects two or three enemies on MAP, then ADD FROM MAP → ADD ALL | They appear on A's board at NORMAL. A friendly selected on MAP is not offered. |
| 5 | A taps a cell in the `VIPER 1` column | Cell turns teal ASGN; the target appears in B's list within a second, with its priority. |
| 6 | A changes that target's priority | B's list re-sorts and shows the new priority. |
| 7 | B presses ENGAGING on it | B's button lights; A's cell turns green ENGD within a second. |
| 8 | A taps the ENGD cell once, then holds it | A tap only shows the "hold to withdraw" hint; the hold withdraws it, and it leaves B's list. |
| 9 | B presses ACQUIRE on an assigned target, then ACQUIRE ALL | The targets become selected in B's own targeting, with the cockpit marker and beep. |
| 10 | B locks an enemy A doesn't have, then REPORT on it | A's board doesn't change; `VIPER 1`'s PULL ▼ shows an amber `1`. B's row reads `SENT TO TAC ✓`. |
| 11 | A presses PULL ▼ under `VIPER 1`, then ADD on the reported target | The panel lists what B reported; the added target lands at NORMAL, unassigned, and leaves the list (the count goes). |
| 12 | Destroy an assigned target | Within ~1 s it disappears from A's board and B's list. |
| 13 | B disbands the squad | Within ~10 s the `VIPER 1` column leaves A's matrix and its assignments are withdrawn (status-line note). |
| 14 | A taps RELEASE once, then holds it | A tap only shows "hold to release"; the hold frees the role on both pages and empties A's board. |
| 15 | A applies again, then quits the game (or kills the process) | B's page shows the role free again after ~15 s. |
| 16 | Both apply at the same moment (with B out of any squad) | Within a few seconds one holds TAC: the lower SteamID. The other sees "TAC taken by …". |
| 17 | Throughout | NOXMFD's own squad features (SQD roster, TD designations, WPT route sharing) keep working. |

If a third player is around: a squad **member** (not leader) sees only who holds TAC and can't
apply (step 1 and 2), and a handover inside B's squad (SQD → promote the member) keeps the `VIPER 1`
column and its assignments, moving them to the new leader (decision 16).

## After a pass

NOXMFD's API branch can merge and ship in a NOXMFD release; this extension's `BepInDependency`
floor then rises to that version (docs/tac-plan.md, build order step 4).
