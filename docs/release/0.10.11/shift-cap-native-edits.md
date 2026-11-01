# Daily shift limit on the vehicle Schedule tab, 0.10.11

Fixes #14; supersedes #27 with the shift-cap fix on current main (229bb0f).

## Cause

The game's Schedule tab caps a newly created shift at 12 hours but does not cap handle drags. `VehicleSchedule.SyncBack` read those edits into Core after bounding hours to the weekday and dropping overlaps with `WeekPlan.Sanitize`; it did not apply the loaded `FleetRules.MaxShiftHours`. A long bar or several separate shifts could therefore exceed the daily cap and be saved and paid.

## Change

- `WeekPlan.OverMaxHours` adds up all shifts on any row for each crew member and weekday whose time intervals the edit changes. It returns groups exceeding the loaded `fleet.schedules.maxShiftHours` (12 in shipped `data/rules.json`). Exactly the cap is allowed; different crew members have separate totals.
- Shifts end at 24:00. Night work counts on each weekday it falls on, including Sunday into Monday. Each part joins that weekday's other shifts; there is no rolling or cross-midnight limit.
- After `Sanitize`, `VehicleSchedule.SyncBack` validates before writing anything to Core, including before creating a schedule. A refusal restores all stand-in `ScheduleDay.workShifts` from the accepted plan, refreshes crew weekly work and shows the localized notice: "A crew member can work at most {max} hours a day. The change was not saved."
- On the next frame, after the game's callback has finished using the edited shift objects, `Redraw` reselects the current weekday through `ScheduleDaySelectionController.OnDaySelected`, rebuilding shift caches and rows. `LoadScheduler` is the fallback. The deferred callback checks that the same vehicle page is still showing.

The existing sync points cover native edits, weekly-hours updates after paste, closing the tab and switching business. No new Harmony patch is needed. Valid edits, auto scheduling, payroll, the save schema, the bridge setter and real businesses keep their existing behavior.

## Evidence

- **Checked (automated):** `tests/Fleet/ShiftCapTests.cs` covers the shipped and custom caps (8, 12, 24), long shifts, several shifts across rows, exactly the cap, separate crew totals, and midnight splits including Sunday into Monday.
- **Checked (automated):** `tools/tests/test_vehicle_schedule_shift_cap.py` compiles the production `SyncBack`, `Refuse`, `Shifts` and employee callbacks against the real Core, using controlled game stand-ins. On carts and trucks it checks refusal of 13- and 24-hour bars and a 14-hour total, unchanged Core and serialized save, restored tab data and weekly-work refresh, one localized notice, next-frame redraw, later callbacks and reload. It also covers a refused first shift without a schedule, valid edits and paste, custom cap 8, midnight splits, closing before redraw and two crew members each at 12 hours.
- **Checked (automated):** the required `C:/Users/Peter/HART-Builds/street-food-0.10.11/focused-tests.ps1` completed successfully: 89 .NET tests passed (including 13 `ShiftCapTests` cases); tools discovery ran 7 tests, with 5 passed and 2 optional assembly checks skipped because the required `BA_GAME_DLLS`/`ilspycmd` setup was unavailable. The existing paste harness now supplies a refusal boundary that fails if reached during its valid-paste scenarios.
- **Checked (automated before/after):** the shift-cap harness using `VehicleSchedule.cs` from 229bb0f failed because stretching Monday 0-12 to 0-13 wrote `e1@Monday:0-13/serving1` to Core. The same scenario passes with this change. These are controlled harness results, not native game evidence.
- **Pending in game:** CHECKS SL1-SL6. In-game checks pending (no rig run in this PR, per Peter). Native redraw timing and the full game/Unity build remain unverified by this run. Before merge/upload, the required PC rig session must add verifiable before/after proof to the PR.
