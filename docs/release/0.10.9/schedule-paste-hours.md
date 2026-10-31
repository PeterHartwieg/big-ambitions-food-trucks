# Schedule paste keeps the route's hours, 0.10.9

Issue #15 (17 October 2026 audit): pasting a day on a vehicle's Schedule tab copied the source day's opening hours into the destination, although the destination's route did not change.

## Cause

The vehicle's Schedule tab is the game's own, on an in-memory stand-in registration whose opening hours are the route day's (`VehicleSchedule.Days`, `WeekPlan.RouteHours`). The game's `ScheduleDayButton.OnPasteScheduleClick` copies a day's opening hours together with its shifts, for a single paste and for each day of paste to all. The mod read back only the shifts (`SyncBack`); nothing restored the hours until the tab was opened again. The header, the hour rows, the add-employee buttons and a new shift's default end then followed the source day's hours. Core's route, shifts, save and wages were not affected by the copied hours.

## Change

In `unity/FoodTrucks/Scripts/VehicleSchedule.cs`, registered in `Patches.cs` with the other Schedule patches:

- `ScheduleHelper.CalculateOpeningHoursState()` prefix (`OpeningStatePrefix`): when the stand-in is the business on the page, all seven days get the route's opening hours back in place (the existing `OpeningHours`) before the game reads them. Both paste paths select a day afterwards, and that selection runs this calculation first. It does not depend on employee callbacks, so it also works with no crew, an empty source day or unchanged shifts.
- The prefix is in the Schedule guards: without it the Schedule tab is not offered (fail closed). The startup log shows `Patch ScheduleHelper.CalculateOpeningHoursState: Active.`

Not changed: the game's paste, confirmation and shift callbacks; pasted shifts (copied as before, not clipped to the route's hours); the days' open state and closed days (the game's paste skips a closed destination); real businesses (the prefix does nothing unless the stand-in is showing). No Core or save schema change. The 12-hour shift cap on read-back (#14) is separate.

The game member was checked with ILSpy against the cached `BigAmbitions.dll` (SHA-256 `9427561B81AAAB2ADA09C22A3C98DD9A4017A65225B389513885BF336852E911`): `public static void CalculateOpeningHoursState()`, no overloads; and the paste path in `ScheduleDayButton.OnPasteScheduleClick(bool)`, `OnPasteToAllDaysClick` and `ScheduleDaySelectionController.PasteToAllDays`.

## Evidence

- **Checked (automated):** `tools/tests/test_vehicle_schedule_paste.py` compiles the production `Days`, `Shifts`, `OpeningHours`, `SyncBack`, `WeeklyHoursPostfix`, `OpeningStatePrefix` and `HireRoutes.HomePoint` against the real Core, with stand-ins for the game's schedule data, the paste and paste to all, the employee callbacks and the calculation (which calls the prefix as Harmony would). For a cart and a truck it covers single paste, paste onto the day after a past-midnight route, paste to all, an empty source day, no crew, unchanged shifts and a real business. The hours, opening state and add-button hour match the route; shifts are copied or cleared in Core; stops, closed days and the closed destination stay as they were; save and reload agree. Red on the pre-fix tree (d2e2b3e), green with the fix. Static checks cover the registration and the guard entry.
- **Pending (in game):** the checks in `CHECKS.md` (SP1 to SP5). The patch has not been compiled against the full game and Unity references or run in the game.
