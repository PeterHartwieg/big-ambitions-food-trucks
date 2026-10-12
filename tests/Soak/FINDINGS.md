# Findings

No Core/plan disagreement found in the checked 20-seed × 60-day matrix.
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

The optional 200-seed × 365-day run has not been executed. See README.md for
coverage and exclusions. No conclusion here implies an in-game check.
