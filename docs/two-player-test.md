# Two-player test (phase 1, build step 4)

One match that checks this extension and NOXMFD's extension API 8 together (docs/tac-plan.md,
decision 14). Nothing below can run in the browser preview: it needs two NOXMFD clients in one
faction.

## Setup

Both players, before launching:

- **NOXMFD 0.77.0 or later** (the GitHub release, NOMNOM or Thunderstore), in `BepInEx/plugins`.
  With an older NOXMFD, BepInEx doesn't load TAC at all and logs a missing-dependency error.
- **TAC** built from this repo's `feature/tac-phase1` branch (`dotnet build TacModule.csproj -c
  Release`), in `BepInEx/plugins`.

In the match: both players in the **same faction**, both **spawned in an aircraft** (the officer can
sit parked). **A** plays TAC officer and stays **out of any squad**. **B** creates a squad
(SQD → CREATE SQUAD, e.g. `VIPER 1`) and so becomes its leader. Each opens EXT → TAC.

If anything fails, keep both players' `BepInEx/LogOutput.log` and note the clock time of the step.
Grep them for:

- `[TAC]` — officers appearing and going (`TAC officer X is available`), link changes
  (`TAC link: X -> Y`, `link to … confirmed`), squadrons joining/leaving the matrix, refusals with
  their reason (APPLY or LINK refused, a `hello` the faction table doesn't back, a sender NOXMFD
  doesn't list yet).
- `[NOXMFD] message for extension 'tac' … dropped: not installed here` — the other side lacks TAC.
- `[NOXMFD squad-link` — the Steam transport underneath (sessions, failed sends).

The first line from TAC, `TAC <version> loaded`, confirms the right build is installed.

## Steps

| # | Do | Expect |
|---|---|---|
| 1 | Both in the faction, nobody applied | A: NO TAC OFFICER IN YOUR FACTION + APPLY FOR TAC. B: the leader's "no TAC officer" text, no APPLY. |
| 2 | A presses APPLY FOR TAC | A gets TARGET CONTROL / YOU ARE TAC, with no `VIPER 1` column yet. Within ~5 s B's page shows LINK TO A TAC OFFICER, with A listed and a LINK button. |
| 3 | B presses LINK on A | B's row reads LINKING…, then within a second or two B's assignment page opens with TAC: A. A's matrix shows a `VIPER 1` column, `1 SQUADRONS`. |
| 4 | A selects two or three enemies on MAP, then ADD FROM MAP → ADD ALL | They appear on A's board at NORMAL. A friendly selected on MAP is not offered. |
| 5 | A taps a cell in the `VIPER 1` column | Cell turns teal ASGN; the target appears in B's list within a second, with its priority. |
| 6 | A changes that target's priority | B's list re-sorts and shows the new priority. |
| 6a | A selects that row and presses LOCATE, with MAP open beside TAC | MAP rings the target, turns FLW off and centres on it once (zooming in if it was at full extent); panning MAP afterwards stays where A leaves it. |
| 7 | B presses ENGAGING on it | B's button lights; A's cell turns green ENGD within a second. |
| 8 | A taps the ENGD cell once, then holds it | A tap only shows the "hold to withdraw" hint; the hold withdraws it, and it leaves B's list. |
| 9 | B presses ACQUIRE on an assigned target, then ACQUIRE ALL | The targets become selected in B's own targeting, with the cockpit marker and beep; they don't show up in B's REPORT list (TAC has them already). |
| 10 | B locks an enemy A doesn't have, then REPORT on it | A's board doesn't change; `VIPER 1`'s PULL ▼ shows an amber `1`. B's row reads `SENT TO TAC ✓`. |
| 11 | A presses PULL ▼ under `VIPER 1`, then ADD on the reported target | The panel lists what B reported; the added target lands at NORMAL, unassigned, and leaves the list (the count goes). |
| 12 | Destroy an assigned target | Within ~1 s it disappears from A's board and B's list. |
| 13 | B disbands the squad | Within ~15 s the `VIPER 1` column leaves A's matrix and its assignments are withdrawn (status-line note). |
| 14 | B recreates the squad and links to A again, then taps UNLINK once and holds it | A tap only shows "hold to unlink"; the hold returns B to the officer list, and `VIPER 1` leaves A's matrix at once (not after 15 s). |
| 15 | B links again; A taps RELEASE once, then holds it | A tap only shows "hold to release"; the hold empties A's board, and B's page drops back to NO TAC OFFICER IN YOUR FACTION. |
| 16 | A applies again, B links, then A quits the game (or kills the process) | After ~25 s B's page drops A from the list and shows the "went silent" note. |
| 17 | Throughout | NOXMFD's own squad features (SQD roster, TD designations, WPT route sharing) keep working. |

If a third player **C** is around:

- C stays out of any squad and presses APPLY FOR TAC while A is an officer: both are officers, and
  B's list shows two. B links to C: `VIPER 1` leaves A's matrix at once and appears on C's, and B's
  assignments are now C's (empty until C assigns).
- As a squad **member** (not leader) of B's squad, C sees SQUAD LEADERS WORK WITH TAC, with no
  officer list, LINK or APPLY. A handover inside B's squad (SQD → promote C), followed by C linking
  to the same officer within ~15 s, keeps the `VIPER 1` column and its assignments, moving them to C (decision 16).

## After a pass

TAC's `feature/tac-phase1` can merge to its `main` and ship as TAC's first release, requiring
NOXMFD 0.77.0.
