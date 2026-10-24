# Findings

No Core/plan disagreement found in the checked 20-seed × 60-day matrix or the
corrected seed-78 × 365-day replay.
There are no skipped regression cases awaiting a Core decision.

Two brief assumptions were resolved against the current documented contracts,
rather than recorded as defects:

- Vehicles **without** a commissary space can be fined too. The design's
  “Commissary and new foods” section explicitly requires this, as does
  `Core/Commissary/Commissaries.cs` (`NightCheck`). The test checks that rule and
  all supplied exemptions, including the player's property.
- “Stock only” applies to the **commissary** refund. The full Unity uninstall
  also refunds vehicle purchase price and paid cargo
  (`unity/FoodTrucks/Scripts/Uninstall.cs`, `RefundCents`). The soak checks the
  public Core refund contract and does not claim to test that game calculation.

## Resolved harness failure: seed 78, day 43, 08:00

The reported full long run stopped at seed 78 (`build/soak-long.txt`). A single-seed
replay reproduced its exact failure before the assertion was corrected.

At 07:00 vehicle v2 opened stop 0, whose hours were 07:00–08:30. At 08:00 the
player edited **stop 1**, from 19:00–21:30 to 18:30–21:30. Replanning from the
truck's real position produced a one-minute `Travel` leg for stop 0 at absolute
minutes [60960, 60961), followed by its remaining open leg. The truck had opened
within the stop radius without an `Arrival` placement; its small distance from
the stop therefore remained in the new plan.

The old assertion (`tests/Soak/SoakWorld.cs:308` before this fix) required
`HireRunner.Want(...).Kind == LegKind.Open`. That mistook a planned approach for
an actual departure. Core explicitly keeps an already-open window open while a
new leg names the same stop (`Core/Fleet/HireRunner.cs:181–184`). The adapter
calls that decision after `PlanFrom`/`EnsurePlan` and performs no action for
`None` (`unity/FoodTrucks/Scripts/HireRoutes.cs:301–322`). The hatch adapter
checks whether the window closed or the truck physically moved, not the leg kind
(`unity/FoodTrucks/Scripts/TruckWindow.cs:349–370`). The sale at 08:00 was within
the original 07:00–08:30 hours, with no actual transit. This is a harness false
positive, not a Core or plan defect.

`SoakWorld.RouteSaleAllowed` now accepts this specific same-stop approach case
while retaining the hours and actual-transit checks. The passing focused test
`RouteReplanTests.AnOpenWindowKeepsServingDuringAReplanApproachWithinItsHours`
also checks that actual transit, approach before opening and sale at closing are
refused. Core is unchanged; no skipped Core regression is needed for this case.

Seed 78 now passes all 365 days. The full 200-seed matrix has not been rerun
after the correction. See README.md for coverage and exclusions. No conclusion
here implies an in-game check.

## Resolved harness coverage gap: midnight sold-through gate (review L6)

The harness previously set every vehicle's `SoldThrough` to the hour boundary
after `RunVehicle`. Its payout gate therefore never saw an incomplete old day.
This was a harness coverage gap, not evidence of a Core defect.

Open-window clocks now move only in `Advance`; a separate route cursor is
combined with the clock as in `CartSelling.SoldThroughOf`. Seeded runs sometimes
leave 23:45–midnight unprocessed until after the midnight payout attempt. The
expected payment day is recorded independently: the whole taking must stay
held, real late sales must accrue to their original calendar day, and the complete
taking must pay once at the following midnight. A focused regression guarantees
those events across day 28 (a tax-period end), including save/load while held.

The game normally catches up at midnight and retries incomplete takings on
later sales ticks (`unity/FoodTrucks/Scripts/CartSelling.cs`, `CatchUpDay`,
`PayCrewTakings`, `SoldThroughOf`). Deliberately missing that first catch-up and
retrying at a later midnight is fault injection for the gate, not an assertion
that the normal game waits an extra day. Core remains unchanged.

## Owed drive back and the midnight replan (17 October 2026)

On the blind-playtest fixes (33f14b1) the long run failed 19 of 200 seeds: 17 on "owed the drive back for three
nights", 2 on "route sale outside stop hours" (seed 35 showed it once the Core fix moved the random sequence).

**One Core defect.** Seed 3: a truck the player had moved to x 769, sold out on Sunday evening. A sold-out crew never
leaves for a pitch, so it stayed there, but the plan's drive back after the stop was timed from the stop (7 minutes)
while the real drive was 31, past the shift's end at 24:00. The next shift was on Thursday, so the truck stood out
for four nights, fined at 04:00 each time. `HireRunner.Step` now sends a sold-out crew with a rented bay straight
back there (while someone is on shift for the whole drive) instead of waiting. Test:
`CrewRestockTests.ASoldOutTruckWithABayDrivesBackThereInsteadOfWaitingForTheDriveBack`.

**The owed-nights check was too strict.** The other failures had no chance to drive back: shifts with days off (one
three-hour shift a week, say), or a failed arrival (the harness's 1-in-10 misfit) whose retry fell after the shift's
end. The runner rightly never drives without crew on shift for the whole drive. A night now counts only if, during
the day before it, the soak saw the vehicle idle, owing the drive, with nothing planned, free to go and with crew on
shift for the whole drive (`HomeChance`). That alone hardly ever fires, so at each such moment the runner's step must
now be the drive back. Both checks together catch a runner with the drive back removed, or with only the delayed
drive back removed, within three seeds.

**The sale-hours check was too strict.** Seeds 127 and 192 (and 35): shifts scheduled again at midnight replan a late
stop that is still open, with a short Travel or Restock leg first. `HireRunner.Step` keeps the window open
through that leg by design, and the sales were within the stop's hours, but `RouteSaleAllowed` measured yesterday's
plans from the replanned opening instead of the stop's opening time. It now uses the stop's hours on the plan's own
day. Test: `RouteReplanTests.ALateStopReplannedAfterMidnightKeepsServingWithinItsHours`.

## Full matrix rerun — 23 October 2026

The 0.10.6 cooler integration candidate passed all 200 seeds × 365 days using
`FT_SOAK_LONG=1 dotnet test tests/FoodTrucks.Core.Tests.csproj --no-build --filter Category=SoakLong --nologo`.
VSTest reported one passed matrix test, zero failures, no skips, in 28 min 44 sec.
The normal suite also passed all 2,124 tests with only this opt-in test skipped.
This supersedes the earlier pending full-matrix status, not the native-game
coverage exclusions. Release evidence: `docs/release/0.10.6/verification.txt`;
raw log: ignored `build/cooler-release/soak-long.log` in the release worktree.
