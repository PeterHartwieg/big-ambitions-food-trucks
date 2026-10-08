# Milestone 5 checks: hires

Build `0.6.0`. The game's own employees can run a cart or truck: the "Hires" button on the vehicle's panel puts an unassigned employee on it and builds a route of stops with opening hours. The crew drives (or pushes) to each stop, opens, closes at the stop's time and moves on, and the game pays them at midnight. The player still restocks. 0.6.0 includes Milestone 4, which hasn't been played yet either: its list is below and can share the session.

Use a fresh save whose name starts with **FT**, never the series save. Other gameplay mods off. Debug keys, holding Ctrl: **F6** free truck, **Shift+F6** free licence and permits, **F7** free cart, **Shift+F9** hire a customer service worker (skill 60).

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds.
- [ ] Installed on homepc with the game closed, after the mod was parked for any recording and restored for the session.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 20 minutes, one launch)

| # | Do | Look for |
|---|---|---|
| 1 | Ctrl+F6 for a truck, Ctrl+Shift+F6, load a grill and hot dogs. Ctrl+Shift+F9 twice | Two new employees in My Employees, both "Unassigned". Is there an "unassigned employee" task for each? |
| 2 | Sit in the truck, press Hires | The list shows both employees with skill, satisfaction and wage. Put one on the truck. Does their "unassigned" task go away? |
| 3 | Stop in a legal parking space at a lunch spot. Hires, "Add this spot to the route", open 11:00, close 14:00. Drive about 500 m to another legal space, add a second stop 15:00 to 18:00 | Do the hour lists read well? The messages say "Stop added" with the hours |
| 4 | Drive to a third place, step out and walk 20 m away. Wait (or sleep) until about 10:30 | Before 11:00 the truck disappears ("sets off … arriving about 11:00"), then appears in a parking space near stop 1 with its hatch to the pavement, and the crew opens it. Did it land in a sensible spot? |
| 5 | Walk to stop 1 while it's open | The worker stands inside behind the hatch, dressed as an employee; customers queue and pay. Does the worker look right (height in the window, facing out)? |
| 6 | Sleep through to the next morning | The truck sold through the afternoon at stop 2 (the summary message for each stop), then was off duty. Is there an unassigned wage for the employee in Econoview or the transactions, dated the day they worked? |
| 7 | If you have time: Ctrl+F7 for a cart, load it, put the second employee on it, add a stop where it stands with hours starting in a few minutes, then push it 30 m away and let go | The cart goes back to its stop (a few minutes' walk) and opens there with the worker standing behind it |
| 8 | If you have time: in My Employees, assign the truck's employee to one of your buildings (if the save has one) | The truck's crew loses them with a message, and the truck stops at the next stop |

For the trailer: item 5 is the new shot, the crew at the hatch with a queue. Anything odd, write it down or note the in-game time.

## 3. Log lines (me)

- Startup: `Version 0.6.0 loading.`, and `Active` for `EmployeeHelper.RunHourly`, `NoTaskAssignedComplaint.ConditionToComplainMet`, `NoTaskAssignedComplaint.ComplaintHandled` and `TasksUI.CreateTodoTask`, with every patch from Milestone 4.
- `Debug: hired … customer service 60`, then `Crew: … on truck …` with the wage.
- `Route: stop … added …`, `Route: truck … leaves for stop 1 …`, `Route: truck … arrived at …: …, N m from the stop` with N under 60, then `Truck … open by its crew at stop 1 …` with `1 serving`.
- `Window crew: … at the … window.`
- `The crew closes` summaries in the close lines (`notification_hire_closed`), one per stop.
- Wages: the save's transactions show `ba:transaction_unassignedwage` for the crew at about the shift's hours times their wage.
- No `Hire routes`, `Hires`, `Window crew` or `Crew` errors.

Not checked this time, on purpose: a late stop past midnight (Core tests cover it), three crew in one truck, crew satisfaction over weeks (the route's hours feed the game's job demands; watch for "no task" complaints later), the uninstall step with a crew (run Ctrl+Shift+F12 at the end if you like and I check the log).

# Still open: Milestone 4 checks (truck, licence and city systems)

Build `0.5.0`. The food truck is in, with the final model: buy it at General US Trucks, drive it, stop in a parking space and open from the driving panel. Selling now needs a vendor licence and a permit for each vehicle, both bought by phone from the City of New York (the contact that sends parking tickets). The licence needs the Food Safety course. The city map has a "Street food" filter line. You look at how it looks and handles; I read the rest from `Player.log`.

This build changes save data (licence, permits, vehicle types) and replaces the test truck, so use a fresh save. Don't reopen an older FT save that still has a Ctrl+F6 test truck: that vehicle type is gone, and a save with an unknown vehicle breaks. A save from 0.4.0 or earlier gets no help: its cart needs a licence and a permit like any other.

### 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; the bundle has `FoodTruck.prefab` and the truck's textures.
- [ ] Installed on homepc with the game closed, after your recording is done and the mod was parked.
- [ ] After the session: the log lines in section 3.

### 2. In game (about 15 to 20 minutes, one launch)

A new save whose name starts with **FT**, never the series save. Other gameplay mods off. Debug keys, holding Ctrl: **F6** free food truck, **Shift+F6** free licence and permits (skips the course), **F7** free food cart, **F9** demand here.

| # | Do | Look for |
|---|---|---|
| 1 | Without the course: phone the City of New York and try to buy a licence | Does the city show up in the contacts with a call button? The call offers a licence, and refuses it without the Food Safety course. Do the texts read well? |
| 2 | Ctrl+F6 for a truck, then drive it a few blocks, park, reverse, take a corner fast | How does it look on the road (wheels on the ground, size, lights, mirrors) and how does it handle? Too floaty, too slow, tips over? |
| 3 | Ctrl+Shift+F6, then load a grill and hot dogs into the truck (box in hand, click the truck). Stop in a parking space at a lunch spot and press Open here | You step out, the hatch on the right swings up, and people line up on the pavement beside the hatch, not in the road or behind the truck. Do they face the hatch? |
| 4 | Stand at the hatch for a few minutes, then drive off | Sales come in as they reach the hatch. Driving off closes the hatch and the line walks away |
| 5 | Try Open here while moving, in the road outside a parking space, and parked with the hatch towards the road (on the left side of a one-way street) | The truck refuses each time and says why |
| 6 | Open the city map, turn on the Street food filters one by one | Offices, factories and warehouses, nightlife, theatres and cinemas light up; "Your food carts" puts a marker on each cart, green while open (trucks have the game's usual car marker; a cart bought or spawned this session also has the game's marker, always shown, and the filter only turns it green). Does any of it look wrong (homes lit as offices, say)? |
| 7 | If you have time: buy a cart with real money, phone the city and buy its permit ($500), and a licence after the course | The money and the call's messages look right; a cart without a permit refuses to open |
| 8 | If you have time: push a cart into a subway station | Does the game let you take the cart on the subway? (The code says it refuses vehicles; you said it works.) |

For the trailer: item 3 is the new shot. Anything off, write it down or note the in-game time.

### 3. Log lines (me)

- Startup: `Version 0.5.0 loading.`, `Vehicle registered: foodtrucks:vehicletype_foodtruck ... price 15000`, `Demand data loaded ... truck 10; licence $250 for 60 days, permit $500`, `Active` for every patch, including `CityMapFilters.InitializeFilters`, `CityMapFilters.ApplyFilters` and `CityMapFilters.ApplyFilters(CityBuildingController)`, no errors.
- `City of New York: licences and permits by phone.` on each load; `licence refused, no Food Safety course` from item 1.
- `Open: truck ... parking Legal`, then `Truck ... open on day ...`, and for item 5 `parking Illegal`, `NotAvailable` or `no pavement beside its hatch`.
- `Queue view: line at cart ...: N of 14 places on the right side ...` for the truck, with N close to 14.
- `Map filters: Street food line with 5 filters.` and no `Map filters` errors.
- Parking: the day's `ba:transaction_publicparking` charge and any ticket for the truck show in the save's transactions (Econoview).
- No `City of New York` or `City reminder` errors.

Not checked this time, on purpose: delivery of a truck to a warehouse (refused, as for the cart), the licence reminder texts (they need 50 in-game days; Core tests cover the days), the uninstall step with a truck (same code path as the cart; run Ctrl+Shift+F12 at the end if you like and I check the log).
