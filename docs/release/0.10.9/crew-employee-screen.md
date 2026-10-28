# Crew in the employee list, 0.10.9

Player report (Workshop comment, 28 October 2026): employees were "not shown as assigned to a business" and showed "no task" despite 49 scheduled hours.

## Cause

A vehicle's crew stays unassigned in the game: no building, so the game's schedules, stations and sick days leave them alone (`HireCrew`). The mod sets their weekly hours from the vehicle's shifts. The game's employee list (MyEmployees) reads the business and the task from a building only, so a healthy crew member with 49 scheduled hours showed "Unassigned" and "No task". Work, routes and wages were not affected. Neither 0.10.7 nor 0.10.8 introduced this.

## Change

Display only, in `unity/FoodTrucks/Scripts/CrewEmployeeScreen.cs`, deciding through `Core/Fleet/CrewScreen.cs`:

- `EmployeeModel(EmployeeInstance)` constructor postfix: for an employee on a vehicle's roster and not in a building, the business column shows the vehicle's name and the task column shows "Street food crew" (`foodtrucks:myemployees_task_crew`). The game's own training and off-sick tasks are kept. The status filter counts crew as Assigned; In training stays. Search finds crew by vehicle name.
- `MyEmployees.ShowEmployee(EmployeeInstance)` postfix: the detail panel's business dropdown shows the vehicle as its selected first option, followed by the same buildings as the game lists. Picking a building moves the employee there, as before, and off the crew.
- `MyEmployees.ManageSchedule(EmployeeInstance)` prefix: for crew, opens BizMan on the vehicle's Street food page (the to-do click's route, `BizManVehiclePage.OpenFor`). Its Schedule button opens the game's Schedule tab for the vehicle. Opening that tab directly was left out: it puts an in-memory stand-in on BizMan's business page and is only exercised from the vehicle page, so a jump from another app is untested.

Not changed: `IsAssignedToAnyBusiness`, `GetCurrentTask`, work shifts, payroll, crew admission and removal (`HireCrew.Available`, `HireCrew.Check`) and the no-task complaint and unassigned to-do patches. No save schema change.

Game members were checked with ILSpy against the cached `BigAmbitions.dll` (SHA-256 `9427561B81AAAB2ADA09C22A3C98DD9A4017A65225B389513885BF336852E911`): the constructor's `instance` parameter and the `currentBusinessName`, `task` and `statuses` fields; `ShowEmployee(EmployeeInstance employeeInstance)` with the private `assignedAddressDropdown` (`UI.Elements.Dropdown`) and `_businessRegistrations`; and `public static void ManageSchedule(EmployeeInstance employeeInstance)`. `EmployeeScrollerController` builds every row, including refreshed ones, with that constructor. The status filter reads `statuses`, and the search reads `currentBusinessName`.

## Evidence

- **Checked (automated):** `tests/Fleet/CrewScreenTests.cs` covers the Core decision: 49 hours, training and absence, filter statuses, other employees, removal and save/reload through `SaveCodec`. `tools/tests/test_crew_employee_screen.py` compiles the production adapter methods, `HireCrew.IsCrew` and the complaint patches against stand-ins that follow the game's constructor, cell, filter, detail panel and Manage schedule. It covers the reported row without and with the patch, training and absence, shop and idle employees, filter and search, detail panel, Manage schedule, reload, fallback name, building job, removal, no session and an adapter failure. Complaint suppression is tested separately from the labels.
- **Pending (in game):** the checks in `CHECKS.md` (CS1 to CS4). The Harmony patches have not been compiled against the full game and Unity references or run in the game for this change. The Unity build happens on the Mac.
