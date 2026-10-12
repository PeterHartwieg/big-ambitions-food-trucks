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
