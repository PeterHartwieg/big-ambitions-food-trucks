# Daily shift limit on the vehicle Schedule tab, 0.10.9

Issue #14: the game's Schedule tab could save a vehicle crew shift longer than the configured daily maximum (`fleet.schedules.maxShiftHours`, 12 in the shipped `data/rules.json`). Dragging a bar's handle to 00:00–24:00, or adding a second shift so a day totalled more than 12 hours, was accepted, survived save and reload, and was paid.

## Cause

The game caps a new shift at 12 hours when it is created, but not when its handles are dragged. The mod reads the tab's shifts back in `VehicleSchedule.SyncBack`, which bounded them to the day and dropped overlaps (`WeekPlan.Sanitize`) but never applied `FleetRules.MaxShiftHours`.

## Change

- `Core/Fleet/WeekPlan.cs`, `OverMaxHours`: for each crew member and weekday whose hours a proposed shift list changes, adds up all their shifts that weekday on any row and returns the groups over the loaded rules' `MaxShiftHours`. Exactly the maximum is allowed. A shift ends at 24:00, so night work is a shift on each weekday (for example Monday 20–24 and Tuesday 0–3), and each part counts toward its own weekday, Sunday into Monday included. There is no rolling 24-hour or cross-midnight limit. The limit applies per crew member, not per vehicle.
- `unity/FoodTrucks/Scripts/VehicleSchedule.cs`, `SyncBack` and `Refuse`: before anything is written, the tab's shifts are judged against the vehicle's accepted shifts. If any group is over the limit, the whole change is refused. The save keeps the last accepted shifts, and the stand-in's days are rebuilt from them. A notice says how many hours the limit is. On the next frame, after the game's drag or paste has finished, `Redraw` reselects the selected day the way the game's own clear button does (`ScheduleDaySelectionController.OnDaySelected` with its selected index). That rebuilds the shift caches and rows without jumping to today. `LoadScheduler` is the fallback. Every sync point goes through `SyncBack`: drag release, adding or removing a shift, paste (weekly hours), auto-fill (before it runs), closing the tab and moving to another business.
- `unity/FoodTrucks/Locales/en.json`: `foodtrucks:notification_schedule_shift_too_long`, "A crew member can work at most {max} hours a day. The change was not saved."

Not changed:

- Valid edits.
- Several shifts per crew member per day within the limit.
- Auto schedule, which already keeps within the limit.
- The test bridge's shift setter. It replaces a crew member's day with one shift, already refused as `TooLong` over the limit by `WeekPlan.CheckShift`, so its result obeys the same daily total.
- Payroll, the save schema, and real businesses' Schedule tabs (the patches stay inert unless the vehicle stand-in is showing).

No new Harmony patch.

## Limitation: existing saves

Saves that already hold an over-limit day are **not** changed. On load they are not repaired, warned about or validated, and the over-limit hours keep being worked and paid as saved. That policy is pending a separate decision. Only edited groups are judged: an edit to another day or another crew member is accepted next to such a day, and moving its shifts to another row without changing their hours is accepted too. Changing that day's hours is judged like any edit. A result still over the limit is refused, and bringing it within the limit, or removing it, is accepted. On a legacy day such as 0–8, 9–16 and 17–24 (22 hours), every single reducing edit can still be over the limit and so is refused. Removing one shift leaves 14 or 15 hours, and shortening one to an hour leaves 15 or 16. The ways out are Clear day (which clears every crew member's shifts that day), auto-fill for that day, or pasting a day within the limit onto it. This is input for the pending decision on legacy saves.

The game's own length for a new shift is not changed either: an added shift runs from the clicked hour to the end of the route's opening slot, at most 12 hours, so an add inside a long slot can take the day over the limit and is refused. Hours outside the route can't be clicked on a vehicle row. To add a short second shift, click an hour near the end of its slot (or just before an existing shift), move it by its body to the start wanted (a move keeps its length), and stretch its right handle to the end wanted.

## Evidence

- **Checked (automated):** `tests/Fleet/ShiftCapTests.cs` covers the cap from the rules (8, 12, 24), one shift over, several shifts adding up across rows, exactly the cap, per crew member rather than per vehicle, midnight splits including Sunday into Monday, and an untouched over-limit group that is not judged. It also characterises an over-limit save loading unchanged. `tools/tests/test_vehicle_schedule_shift_cap.py` compiles the unmodified production `SyncBack`, `Refuse`, `Shifts` and the employee callbacks against the real Core. Stand-ins replace only the game's drag, shift creation, employee update and redraw. It covers a cart and a truck:
  - stretching to 13 and 24 hours, and two shifts totalling 14 (on either row): refused, with Core and the save unchanged, the stand-in days restored, one notice naming the cap, the redraw on the next frame (not inside the callback), no return on a later sync, and the accepted shifts after reload;
  - a two-shift day at exactly 12 hours: accepted and kept after reload;
  - a custom cap of 8;
  - a midnight split;
  - an over-limit save with an unrelated valid edit: accepted, with the old day untouched;
  - closing the tab before the redraw;
  - paste and paste to all of an over-limit day from an old save, through the production weekly-hours postfix once per crew member: one notice, Core unchanged, every destination day restored and later callbacks doing nothing; a valid paste is accepted.

  Red on `d2e2b3e`, green on this branch. An optional test re-reads the redraw's private game members with ILSpy when `BA_GAME_DLLS` is set. Checked against the cached `BigAmbitions.dll`, SHA-256 `9427561B81AAAB2ADA09C22A3C98DD9A4017A65225B389513885BF336852E911`.
- **Pending (in game):** the checks in `CHECKS.md` (SL1 to SL6). The adapter has not been compiled against the full game and Unity references or run in the game. In particular, the redraw timing after a drag and after a paste is unverified.
