# In-game checks

The native acceptance checklist for Street Food. Each section lists what to do, what to look for and the evidence so far. Decisions behind the checks are in [docs/design.md](docs/design.md). Rig, bridge and command details are in [docs/test-bridge.md](docs/test-bridge.md).

## Rig preflight and proof

Every in-game check follows these rules. A run that skips one is not evidence.

1. Use a disposable FT save. Load an existing FT fixture and Save As a new FT name before changing anything. Never load a personal save.
2. Hold the rig's exclusive lease (`agent1` on Windows, `agentmac` on the Mac) and confirm the normal game is closed and no other Big Ambitions process runs.
3. Take a fresh backup of the rig profile and install receipt before installing.
4. Install the exact candidate. Compare every installed file's SHA-256 with the package manifest and note the game build and any rig-only bridge adaptation.
5. Record the date, rig, game build, package SHA-256, bridge session, save names, screenshots and the full Player.log. Run the log check ([log checks](docs/test-bridge.md#log-checks)). Each Harmony patch logs one `Active` line at startup.
6. Afterwards stop the game, restore the original install, verify the restored hashes against the backup and release the lease.

Evidence labels:

- **Checked**: seen in game, with date, rig and build. Automated tests and code reading are not checks.
- **Pending**: not yet run in game.
- **Unverified**: not run, and the named rig cannot show it (for example full-frame UI on the headless Mac rig).

Keep the tested candidate and the final package apart. If the final package differs from the tested bytes in anything but README text, the final package is unverified in game until it is run. The headless Mac rig gives native callbacks, UI trees and world-camera shots, but not full game-frame capture, so it cannot check visual layout or scrolling.

## Vehicle marketing

Per-vehicle marketing on the BizMan vehicle page ([design](docs/design.md#marketing), [price rationale](docs/balance/marketing.md), prices in [data/rules.json](data/rules.json)). MK1 to MK7 passed on the Windows rig on 2026-11-07 with the PR #36 build; the proof is on PR #36. The 0.10.12 package with its bumped version was not run in game (Peter, 2026-11-01: no separate smoke when the feature PR has proof). These are native feature checks. Modeled income from marketing is a calculator estimate, not a check.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| MK1 | Open a cart's BizMan page and its Marketing dropdown, then a truck's | None, Flyers, Local ads, Citywide ads with "+10% passers, $20 a day" and the cart prices; the truck shows $60, $140, $240 | Checked 2026-11-07, Windows rig |
| MK2 | Pick Flyers | Nothing in the bank history; the box shows "From tomorrow: +10% passers, $20 charged at midnight" and no Today row | Checked 2026-11-07, Windows rig |
| MK3 | Sleep or skip past midnight | One "Marketing for ..." bank entry of $20, tax-deductible as Street food marketing. The box shows "Today: Paid, +10% passers". Econoview lists the cart's Marketing line; the fleet page's average daily income includes the fee | Checked 2026-11-07, Windows rig |
| MK4 | Pick Citywide, then None, then Local, all the same day | Nothing charged; Today stays "Paid"; From tomorrow follows each pick. One $45 charge at midnight | Checked 2026-11-07, Windows rig |
| MK5 | Keep a level on with less money than its fee at midnight | The level turns off with "...'s marketing stopped: the day's $... couldn't be paid."; nothing charged | Checked 2026-11-07, Windows rig |
| MK6 | Skip several days with marketing off, then pick a level | No charge for skipped days or on picking; one charge at the next midnight | Checked 2026-11-07, Windows rig |
| MK7 | Save and load the same day after picking; then load a save from before marketing | No charge after the load and the booking survives. The old save has no level and loads without error | Checked 2026-11-07, Windows rig |

## Daily shift limit

A crew member can work at most `fleet.schedules.maxShiftHours` (12 in [data/rules.json](data/rules.json)) per calendar day on a vehicle's Schedule tab. Issue #14, shipped in 0.10.11 by PR #33. Core and production-method harness checks pass. All rows are pending in game, and the 0.10.11 package was not run in game (Peter dropped the combined smoke on 2026-11-01).

Making a bar: a new shift runs from the clicked hour to the end of the route's opening slot (at most 12 hours), and hours outside the route can't be clicked. Click near the end of a slot so the new bar is short, move it by its body to the start hour, then stretch its right handle to the end hour. Each release is judged alone. A release that passes the limit on the way to a valid target is refused and snaps back; retry from the accepted bar. That is not a failure.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| SL1 | On a weekday other than today, make a 0-12 bar, then stretch it to 13 and separately to 24. Stretch to 13 again and close the tab at once. Reopen, stretch to 13, then open another real business without leaving BizMan (a to-do of that business that opens it, or the "show schedule" action of its auto-fill notice; `ui list`/`ui click` if needed) | Notice "at most 12 hours a day". Next frame the bar is back at 0-12 on the same weekday, hours label 12; state and log (`change refused`) say 0-12, also after reopening. After closing or switching, no exception, the other business's page works, and the log has `the Schedule tab for <id> closed` or `moved on from <id>'s Schedule tab`. If no path reaches the other business, record the switch as pending, not reachable | Pending |
| SL2 | Same crew member and day: 0-8, then a second bar dragged to 10-16 (same row, and a truck's second row) | Refused once the day passes 12 hours; the second bar returns to its last accepted hours (or goes, if its add was refused); weekly hours unchanged | Pending |
| SL3 | 0-6 plus 8-14 for one crew member; on a truck two crew at 0-12 on their own rows; copy the 0-6/8-14 day and paste it to another day | Accepted with no notice; all shifts shown and in state, the pasted day too | Pending |
| SL4 | Sunday 14-24 and Monday 0-4, then a Monday bar 6-15, then 6-14 instead | Night split accepted; 6-15 refused (13 hours on Monday); 6-14 accepted. No limit across midnight | Pending |
| SL5 | After one valid and one refused edit, save to a new FT name, reload, reopen the tab | Same accepted shifts and weekly hours; refused edit absent | Pending |
| SL6 | On a real shop's Schedule tab drag a shift past 12 hours | The game's behaviour; no mod notice and no refusal line in Player.log | Pending |

## Private-driver uninstall

Safe uninstall (Ctrl+Shift+F11 twice) must find, remove and refund vehicles at the private driver as well as on the street. Issue #13, shipped in 0.10.10. The production-linked harness passes. All rows pending. Precondition: first check whether the private driver accepts a Street Food truck at all. If not, record that and use a labelled stored-state fixture.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| UN1 | Leave the only truck, with paid stock, at the private driver; save, reload, uninstall | The confirmation counts 1 vehicle; done notice; one refund of purchase price plus paid stock; the driver's list no longer shows the truck | Pending |
| UN2 | One cart on the street, one truck and one ordinary car at the driver; uninstall | Confirmation counts 2; both removed and refunded once; the car stays; done notice only after both are gone | Pending |
| UN3 | Run the uninstall twice more, save, reload, run it again | No further refund or money change; each run reports the save clean | Pending |
| UN4 | Close the game with the cleaned save, disable only Street Food, load it, save, reload, cross midnight | Normal world interaction; driver list shows only vanilla vehicles; no unknown vehicle type errors | Pending |

## Serving and quit

Player service at a cart window or truck hatch, the work panel and teardown at quit. Startup must log `Active` for `PlayerActivityUI.SetUpSpeedOptions`, `PlayerActivityUI.OnPlayerActionPressed`, the serving `GameManager.RunMainGameTick`, `ItemPanelUI.UpdateButtonsVisibility`, `ItemPanelUI.SetupCargoSlots` and `VehicleController.DriveVehicle`. Core and mock tests cover the sale clock and controls. QS1 to QS3 fix issue #19 (0.10.10). All rows pending in game.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| SW1 | Open a cart, then a truck. Use the native fast-forward (mouse and key), skip button and work slider (5-720 minutes); then the bridge `play vehicle ID fast_forward on/off`, `skip_work 30`, `stop_work`, `work_window`, `back_to_hatch` (was SW1, SW2, SW9) | Native controls; cart keeps its pavement stance and truck its hatch. Clock, world and customers follow `TimeSpeed.FastForward`; no held-order pile-up or duplicate takings. Bridge commands use the same controls, give no needs top-ups and refuse away from the work panel | Pending |
| SW3 | From one save, compare 60 minutes of normal service with a 60-minute work skip at the same pitch, menu and staffing, with drink add-ons, player plus crew and a second crewed vehicle elsewhere | Same Core orders, stock used, sold counts, stock cost and unpaid takings; nothing paid before midnight | Pending |
| SW4 | Skip 15 minutes, then through midnight; then skip with little stock, at a route close, a licence expiry and zero energy (was SW4, SW5) | Chosen duration respected; needs drain as register work; final minute sold once; midnight pays each day once. A skip stops at the first sold-out or closing minute; nothing sold or charged after it | Pending |
| SW6 | Cancel a running skip; stop work; step out of the truck; close or grab the cart; save, load, leave the scene. Stop at a cart and choose Work the window; step out of a truck and use Back to the hatch (was SW6, SW7) | Completed sales survive once; speed and cargo panel release; load restores no skip or attachment; returning restores controls; crew serve on alone; the player is never teleported into the street | Pending |
| SW8 | Watch the cargo panel during normal and fast service; try its buttons, shortcuts and dragging; then enter or push the vehicle | Counts refresh per sale; no taking, selling, unloading or transfer while serving. Afterwards the normal cargo panel and driving cargo HUD work | Pending |
| NC1 | Open a cart yourself, then a truck with crew | No "... is open with N items" notice on screen, while the log has `Open: cart ... opened by the player with N items; serving: ...`. Premium spot, park and refusal notices still show | Pending |
| QS1 | Serve at a cart with the stock panel showing and a queue forming; send bridge `quit` without stopping work; repeat at a truck hatch | No `Serving work, stock panel:` line and no Toggle exception; the stance release (and queue removal) is logged; the log check passes | Pending |
| QS2 | While serving, use the pause menu's Main menu, Quit to desktop, and Save and exit (one run each) | Same clean log; Main menu reaches the menu and the save reloads; Save and exit saves and exits | Pending |
| QS3 | Cart: work, stop work; work, walk away; open, close the business. Truck: work, step out | The stock panel closes each time; driving afterwards shows the native cargo HUD | Pending |

## Schedule paste

Pasting a day on a vehicle's Schedule tab keeps each destination day's route hours. Issue #15, shipped in 0.10.10. The production-method harness passes. All rows pending. Setup: a truck and a cart whose Monday, Tuesday and Wednesday routes have different hours and Thursday closed.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| SP1 | Start the game and read the log | `Patch ScheduleHelper.CalculateOpeningHoursState: Active.` with the other Schedule patches, then `Schedule: on (...)` | Pending |
| SP2 | Copy Monday, paste on Tuesday; Back and reopen. Truck and cart | Tuesday keeps its route hours with Monday's shifts; log `Schedule: ... now has N shifts`; same after reopening | Pending |
| SP3 | Copy Monday, paste to all, confirm; select each day | Every open day keeps its own hours with Monday's shifts; Thursday stays closed; the Week plan route is unchanged | Pending |
| SP4 | Paste an empty day onto a day with shifts; repeat on a vehicle with no crew | Shifts cleared; + buttons at the destination's opening hour; with no crew the hours stay the route's | Pending |
| SP5 | On a real shop, paste a day onto one with different hours | Hours and shifts copied as in the game | Pending |

## Crew employee screen

Vehicle crew show as assigned in MyEmployees. Display only, from a Workshop report of 2026-10-28, shipped in 0.10.9. Core and production-method harness checks pass. All rows pending.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| CS1 | Add an employee to a truck's crew with seven 7-hour shifts; open MyEmployees | 49 hours, the truck's name and "Street food crew" in white; a shop employee's row unchanged | Pending |
| CS2 | Use the Assigned/Unassigned/In training filters, search the vehicle name, select the crew member; with two or more businesses pick the first business in the dropdown | Crew under Assigned; search finds them; the dropdown shows the vehicle; opening the panel changes nothing; picking assigns exactly the named business and takes them off the crew. Log has `Patch EmployeeModel..ctor(EmployeeInstance): Active.` and the ShowEmployee and ManageSchedule lines | Pending |
| CS3 | Send one to training; wait for one to call in sick; press Manage schedule for a healthy one | Training and sick labels as for any employee; Manage schedule opens the vehicle's Street food page, whose Schedule button opens its shifts | Pending |
| CS4 | Save, reload, then remove the employee from the crew | Same row after reload; after removal "Unassigned" and "No task" with the game's idle complaint and to-do | Pending |

## Commissary addresses

Commissary contacts and BizMan's Commissary space panel show the host building's address, View on map and Set destination. Display only, from a Workshop report of 2026-10-26, shipped in 0.10.10.

Checked on 2026-10-28 in the Windows `agent1` rig with draft PR #23 (source `c01090782c5837295df672e9cb22ded1a1fc307f`), against Workshop 0.10.9 as the before build. The log check passed with 115 active patches and no errors. Addresses seen: Industry City 2 21st Street, Lower Manhattan 5 12th Street, Garment District 51 2nd Street, Hell's Kitchen 8 2nd Avenue, Murray Hill 12 5th Street. Limits: physical driving is unverified (bay arrival used teleport, then real delivery and loading); the 23:00 call tested a vacant host, not every occupied tenant's hours; a city reload with the phone open left stale menu state until the phone was closed and reopened, after which every check passed. Raw provenance: [original evidence note](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/e97400846d190da299239e129fbcef50c3f4d6ef/docs/evidence/commissary-address/README.md).

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| CA1 | On a pre-change save with an Industry City space, open its contact far away; View on map; again with Voogle Maps already open; switch contacts and back | Header shows 2 21st Street and View on map; the map opens (or moves) on the building with its card selected; the call works outside the host's hours; no stale address or target after switching; `Patch ContactsApp.SetUpHeader(Contact): Active.` | Checked 2026-10-28, agent1: [before](docs/evidence/commissary-address/CA1-before-industry-contact.jpg), [after](docs/evidence/commissary-address/CA1-after-industry-contact.jpg), [map already open](docs/evidence/commissary-address/CA1-after-industry-map-already-open-card.jpg) |
| CA2 | BizMan, Street food, the vehicle, Commissary space; Set destination; go to the bay | Address matches the contact, with the line that the bay is beside the building; GPS leads to the building; delivered stock loads at the bay | Checked 2026-10-28, agent1, except physical driving (unverified): [before](docs/evidence/commissary-address/CA2-before-industry-bizman.jpg), [after](docs/evidence/commissary-address/CA2-after-industry-bizman.jpg) |
| CA3 | Repeat CA1 and CA2 at the other four commissaries | Each shows and targets its own building; money, rent, stock, orders and ledger unchanged | Checked 2026-10-28, agent1 |
| CA4 | Save, reload, reopen contacts and the panel; call, rent or move a space; visit a locked commissary | Controls work after reload; the call, rent, move and new welcome texts name the address; old texts still read | Checked 2026-10-28, agent1: [reload](docs/evidence/commissary-address/CA4-after-reload-verified-bizman.jpg), [welcome](docs/evidence/commissary-address/CA4-after-new-visit-welcome.jpg), [night call and rental](docs/evidence/commissary-address/CA4-after-night-call-rental-text.jpg) |
| CA5 | Turn on Voogle Maps, Street food, Commissaries; pan to Industry City and Lower Manhattan | Both host buildings carry the commissary marker; record a missing one with the log's `Map filters` lines | Checked 2026-10-28, agent1, missing marker not reproduced: [Industry City before](docs/evidence/commissary-address/CA5-before-industry-marker.jpg), [after](docs/evidence/commissary-address/CA5-after-industry-marker.jpg), [Lower Manhattan](docs/evidence/commissary-address/CA5-after-lower-marker.jpg) |

## Event and catering addresses

City events and catering show real building addresses, keep GPS for fully booked events, refund only unused event places once and repair only identifiable legacy messages. Shipped in 0.10.8.

The native passes below ran on 2026-10-25 in the Windows `agent1` rig with rig TEST packages (dotnet-built, not SDK release builds). The round-3 package had ZIP SHA-256 `9231eafba88eb007c1f4d3a05548702b20b27f3e9f62fa7287a8a456398d3313`, 111 active patches and no errors. Later rounds 4 and 6 report native results only outside the repository, so they are not counted here. Round 7 changed tests and docs only and did not rerun native. Final acceptance of the shipped 0.10.8 build is pending. Automated and package results: [review-fix3-verification.json](docs/release/0.10.8/review-fix3-verification.json), [review-fix4-verification.json](docs/release/0.10.8/review-fix4-verification.json).

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| EA1 | Compare city announcements, confirmations, reminders and catering offers and confirmations with native building cards; toggle Street food, Premium spots and events, and Catering bookings | Same house number and native street name; the right building highlights; no raw IDs or missing placeholders. An accepted confirmation flips to "This booking is no longer active." in the same session after native cancellation and a phone refresh | Checked 2026-10-25, agent1, round-3 TEST |
| EA2 | Fully book an event; call again, Set destination; Back with one event, Next with several; save and reload | The booked event stays reachable; GPS points to the same address after reload | Pending for 0.10.8. Seen on the pre-review build `38675ff` (agent1, 2026-10-24) at 34 4th Avenue |
| EA3 | Invalidate a paid city location with two bookings: unused, one used by a distinct vehicle, repeated visits by one vehicle; repeat ready-at-load and with addresses delayed, with a closed vehicle's hold starting in the load minute; include running and ended events; reload. Invalidate a booked catering gig | Two fees back when unused, one when a distinct vehicle used a place; a same-minute closed hold costs nothing in both paths; no duplicate refund after reload; catering cancels without a miss or reputation loss | Ready matrix checked 2026-10-25, agent1, round-3 TEST ($960 once, no penalty). Delayed and mixed holds pending |
| EA4 | Load real 0.10.7 history: city announcement, booked and reminder for unended and ended events; orphaned catering offers and confirmations (19 5th Avenue, 11 10th Avenue). Wait six minutes, save, reload | Unended identifiable city messages repaired; ended history unchanged; orphans show their own address and inactive wording and keep "60 metres"; keys, order, counts, payloads and money unchanged on repeat load | Primary and early-market fixtures checked 2026-10-25, agent1, round-3 TEST |
| EA5 | With a permitted catering truck and past sales, uninstall, wait five game minutes, cross midnight; save, disable the mod, reload | Street Food Bookings stays absent; no new offers; no returning contact or raw `foodtrucks:` text | Pending |
| EA6 | Leave an unaccepted offer past its reply day through two day rollovers | Only that offer reads "This offer is no longer available."; accepted offers keep the map hint | Checked 2026-10-25, agent1, round-3 TEST |

## Cart sale and rosters

Unlimited vehicle rosters (0.10.4) and selling a closed cart at a commissary bay (0.10.5). Retained receipts: `docs/release/0.10.4/native-verification.json`, `docs/release/0.10.5/native-assertions.json`, `docs/release/0.10.5/standing-order-assertions.json`, `docs/release/0.10.5/windows-smoke-2026-10-23.json`.

Rosters were checked on 2026-10-22 on the Mac `agentmac` rig (0.10.4, reviewed source `1ea6cdd`): 12 truck and 3 cart employees through native BizMan callbacks, auto-fill rotating six truck and two cart employees, three successive Monday shifts with handovers, overlap refusals, save and reload, off-shift service stopping, and exact midnight payroll for the nine scheduled employees only. Mouse scrolling and layout at other resolutions are unverified on that headless rig.

The stocked cart sale was checked on the Mac rig (0.10.5, session `6e5ada0d`): a cart with equipment, 45 dumplings, three crew, a week of route stops, permit and bay quoted $3,247.13 and paid that plus $337.50 outstanding sales; the crew became unassigned with $116.67 earned wages paid at midnight; the other truck's takings stayed pending; shared stock and an 80-dumpling delivery stayed and arrived next morning; reload matched. A standing order prepared in a separate fixture was retained through load, sale and save; standing-order entry through the native item picker is unverified. On 2026-10-23 the Windows `agent1` rig ran only an empty-cart sale: quote $600, cancel, confirm, reload, no repeat payout. Venue partnership and pitch release have Core coverage only.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| CR1 | Add four or more employees to a truck and three to a cart; scroll the crew panel; replace and remove a later one | All reachable; Add employee stays; header separates assigned staff from the simultaneous limit | Partly checked 2026-10-22, Mac: assignment, header, replace and remove. Scrolling and visual layout unverified |
| CR2 | One truck pair on weekdays and another at weekends; rotate cart staff; stack shifts on one serving row | All roster employees offered; two truck rows, one cart row; overlap on a full row refused | Checked 2026-10-22, Mac |
| CR3 | Auto-fill a week with more employees than places; run through handovers | Staff rotate; at most two truck workers and one cart worker serve at once | Checked 2026-10-22, Mac |
| CR4 | Save and reload large rosters, then remove an employee | Rosters and shifts survive; removal clears only that employee's shifts | Checked 2026-10-22, Mac |
| SC1 | Park a closed, stocked cart in a commissary bay, let go, click it on foot | Sell cart offered; quote covers cart, equipment and nested stock at the game's resale rate; Cancel changes nothing | Checked on Mac (0.10.5) and Windows 2026-10-23 (empty cart only) |
| SC2 | Confirm a cart with crew, route, unpaid sales and a rental | Cart and cargo gone; one resale payout and separate sales payout; crew unassigned with wages kept; route, permit, rental, partnership and warnings end | Checked on Mac (0.10.5). Partnership and pitch release Core only |
| SC3 | Inspect another vehicle and the shared commissary | Other vehicles, shared stock and orders, licence, accounts and wages intact; no later rent or operation for the sold cart | Checked on Mac (0.10.5) |
| SC4 | Save, reload, pass midnight | No revival and no duplicate payout; accounts and wages kept | Checked on Mac (0.10.5) and Windows 2026-10-23 (reload only) |
| SC5 | Move or change the cart after opening the confirmation; try with a read-only save | Sale refused; new quote needed; nothing destroyed in read-only state | Unverified in game; production-adapter harness only |

## Truck logos and cooler

Custom truck logos from the native shop editor (BizMan, Street food, truck, Branding), a dedicated drinks-fridge place, and water removal. Early smoke ran on 2026-10-22 on the Mac rig. The 0.10.6 release ran on 2026-10-23 on the Mac rig (session `e0080384`) and as a short Windows regression smoke (session `df2115ec`); platform details and the per-check remainder are in [docs/release/0.10.6/final-verification.txt](docs/release/0.10.6/final-verification.txt). The 0.10.6 runtime and tuning must be built together; a mixed build failed eight tests. Before widening any claim about old saves, rerun with a genuine saved file from before the feature.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| LG1 | Choose built-in and imported PNG shapes, colours, fonts and text; save, leave, reopen | Preview and body signs match; logo text is separate from the fleet name; edits apply only after Save logo changes | Partly checked: Mac 2026-10-22 (text, font, imported PNG, colour, restart, remove) and 2026-10-23 (text, reopen). Every combination and unsaved-draft view pending |
| LG2 | View both sides with the hatch open and closed; drive, hide and show, despawn, reload | Signs stay on their panels without clipping or duplicates; no collisions | Partly checked: Mac native camera 2026-10-22, both sides, hatch open and closed. Driving, hide and show, despawn pending |
| LG3 | Give two trucks different designs; save and restart; remove one; open a normal shop's editor | Designs independent; removal affects one truck; shops keep their controls and artwork | Partly checked 2026-10-23, Mac: two-truck isolation, removal and reload. Shop editor regression pending |
| LG4 | Load old or newer-version logo data; delete a custom PNG from the shared folder | Old trucks unbranded; unknown logo data kept without financial damage; missing files don't corrupt the save | Unverified in game; Core tests only |
| CSF1 | Fit two general pieces on a cart or four on a truck, then a small or large drinks fridge from commissary stock; repeat with the fridge first and stocked | One fridge fits; another general piece is refused; the menu still opens with soda | Partly checked: Mac 2026-10-22 full cart and truck plus fridge, extra piece refused; 2026-10-23 two-fridge transfer and refit with 318 cans kept. Every stocked-fridge order pending |
| CSF2 | View both cart queue sides and the truck with the hatch open and closed; push or drive, hide and show, respawn | Cart fridge on the worker side, truck fridge on the customer side; cooking equipment visible; wheels, handle, crew, register and hatch clear; no duplicates | Cart worker side checked 2026-10-24, Windows agent1, 0.10.7 ([centre-worker evidence](docs/release/0.10.7/center-worker.md)). Movement, visibility and respawn pending |
| CSF3 | View BizMan with the fridge empty, fitted and removed; fill soda; fit Extra Storage | Dedicated place, tooltip, layer and fill bar; 80 and 160 cans, doubled with Extra Storage; general pieces never use the place | Partly checked: Mac 2026-10-22, 160 cart and 320 truck cans with Extra Storage. Hover, layer and removed state pending |
| CSF4 | Load a genuine pre-feature multi-fridge FT save; take and return equipment; save as new and reload | First fridge in the dedicated place, extras in general places; stock and capacity right; no invalid-menu closure | Current-build two-fridge save and reload checked 2026-10-23, Mac. Genuine old save pending |
| CSF5 | Compare matched setups with and without a fridge at quiet, busy and saturated pitches | Record food orders, soda add-ons and standalone sales, revenue, stock cost and restocks; note whether drink orders displace food | Pending in game. Offline only (`docs/release/0.10.6/cooler-economics.txt`) |
| WR1 | Call a commissary, Order stock; open a vehicle Menu with a fridge | Soda offered; water absent from the catalogue and menu | Pending |
| WR2 | Load an older save with water on a vehicle and in commissary stock; serve, Fill up, save, reload | Water is never sold or loaded; existing water stays; soda sells | Pending |
| WR3 | Let a water one-off order already booked arrive; let an existing water standing order reach its next booking | The booked order delivers as booked. The standing order books no water and no fee for a water-only day; other lines book normally | Pending |

## Crew driving probe

A developer-only physical driving probe, compiled only with `FOODTRUCKS_CREW_DRIVING_DEV`. Shipped crews use timer-based offscreen journeys, and normal builds contain no probe: on 2026-10-23 both release rigs rejected `crew_drive_probe` start as an unknown command. Setup, commands and limits: [crew driving probe](docs/test-bridge.md#crew-driving-probe). Decisions and gates: [docs/design.md](docs/design.md).

Native results so far, all on the Mac `agentmac` rig on 2026-10-21 with Persistent City and Driven Rides installed and a 1 m/s cap on straight routes. Short straight drives of about 8.1 m ended by physical braking with no contacts or damage. After the side clearance was cut to the measured body envelope, a 30.97 m drive passed a mailbox and trash bag that the earlier sweep had stopped for, ending by physical braking with zero contacts, zero damage, no forced hold and clean handback. A real cart placed in the lane made the truck brake and hold about 2.20 m clear; after the cart was moved away the truck resumed by itself and completed 8.11 m. Adapter control timing peaked at 306 ms in that run, unattributed; it is no performance evidence.

Report physical stops separately from forced interruption holds. Keep pose, fuel and damage across every stop. Run each case with Persistent City enabled and absent.

| Check | Native evidence required | Evidence |
| --- | --- | --- |
| CD0 | Body and trigger certificate: common trigger-height band, raised-floor models, wheel guard, refusal branches | Partly checked 2026-10-21: 30 trigger profiles, common band, raised-floor coverage, endpoint braking. Refusal branches pending |
| CD1 | All probe hooks active; an owned truck reaches stationary readiness without player entry | Checked 2026-10-21, Mac (15 hooks) |
| CD2 | Start, stop and takeover restore input, engine, rigidbody and wheel settings; replacement controllers and save sessions stay isolated | Pending |
| CD3 | Low-speed straight drives end by physical braking with zero contacts and no forced hold | Checked 2026-10-21, Mac, short and 30.97 m runs. Wider route campaign pending |
| CD4 | Brake, hold and resume for a solid obstacle; prepared native turn; signal stop and resume; moving traffic. A late stop flag is a failed run | Solid obstacle checked 2026-10-21 (about 2.20 m, resumed for 8.11 m). Turns, signals and moving traffic pending |
| CD5 | Traffic detects the truck; junction ownership coexists with Driven Rides and clears after exit or full reversal | Partly checked 2026-10-21: one Gley car stopped for the stationary truck and resumed after it moved. Junctions pending |
| CD6 | Native tank and damage survive stop, save and reload; no refill, repair or payment | Partly checked 2026-10-21: save and reload kept pose, 77.55 L and zero damage after a short drive. Collision damage pending |
| CD7 | Pause, speed change, skip, save, destruction and unload clean up synchronously; teleport and restart cancellation; 50 native lifecycle cycles leave no records | Pending |
| CD8 | 20 contact-free controlled route completions with Persistent City enabled and absent; matched CPU, allocation and frame measurements at the scoped fleet sizes | Pending |
| CD9 | Calibrate journey, parking, clock and fuel inputs natively, then compare profit with adapted schedules | Pending. Offline only: [route economics study](docs/balance/crew-route-economics-2026-10-21.md). Modeled profit is not gameplay evidence |

## Park pitches

Two premium pitches by parks, 23 Eleventh Street (Lower Manhattan) and 5 Second Avenue (Hell's Kitchen); coordinates and limits in [docs/balance/park-premium-pitches.md](docs/balance/park-premium-pitches.md) and [data/pitches.json](data/pitches.json). On 2026-10-14 the Mac bridge checked only legal truck parking and the hatch rule there. The bridge's `hatch_to_pavement` also returns true for an unclear verdict, so it is no visual check. Industry City's premium pitch was dropped after every legal truck pose failed the hatch rule; its ordinary park demand stays.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| PK1 | At each pitch, park a permitted vehicle legally within 40 m facing the park approach and open it | The pitch name on the Street food map and route picker; `premium spot <id>` in the opening log; for a truck a legal lane, hatch to pavement and a clear queue | Parking and hatch rule checked 2026-10-14, Mac bridge. Name, marker, queue pending |
| PK2 | At Lower Manhattan move between 23 Eleventh Street and the Lower Twelfth Street pitch | The nearer entrance picks its pitch; no stacked bonus; both names discoverable; nearby events don't capture the park approach | Pending |
| PK3 | Open a second vehicle at a held pitch; close the first and reopen the second | Only the first holds it; the second can claim it after | Pending |
| PK4 | Compare a dry weekend daytime with a weekday, night and rain at a pitch (Ctrl+Alt+L) | `park` crowd present with the park demand pattern; weekend afternoon busiest | Pending |

## Commissary delivery and settlement

Phone orders pay on delivery at 08:00, with one delivery fee per commissary and morning. All window sales go into one pot per vehicle and day and are paid once at midnight on the sold day (save schema 2). Startup must log `Active` for `BusinessHelper.RunDaily` and `VehicleHelper.Delete(VehicleInstance, VehicleController)`. Scenarios: `tests/game/scenarios/commissary-pay-on-delivery.json` (CO1, CO2), `commissary-refused-payment.json` (CO3) and `crew-midnight-payout.json` (CO4, CO5); see [scenarios](docs/test-bridge.md#scenarios). All rows pending on the current build.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| CO1 | Rent a space, call, order 2 boxes, confirm | Read-back ends "paid on delivery here at 08:00 tomorrow"; money unchanged; no transaction | Pending |
| CO2 | Wait for 08:00 (or Ctrl+Alt+F3); order two foods at one commissary for the same morning | "Your delivery is in: ... paid."; one "Commissary order from ..." transaction for goods plus one delivery fee for that commissary and morning; Econoview shows the fee; stock at the commissary | Pending |
| CO3 | Order, then drop below the order's total before 08:00 | "We couldn't deliver ..."; no money or stock moves; 0 orders on the way; nothing taken later | Pending |
| CO4 | Give a truck crew and a route; stay more than 100 m away all day | No sale notices; money doesn't move with its sales; Econoview shows today's sales | Pending |
| CO5 | Let midnight pass with a crewed truck selling at 23:59 | One "Street food sales: ..." payment per vehicle, dated the sold day, before the day summary; day summary and Econoview match | Pending |
| MS1 | Serve at a cart, then a truck hatch, with a drink add-on | Stock falls; cash sound and sale log stay; no balance change or popup per sale; bridge `ledger_today.sales_waiting` rises | Pending |
| MS2 | Crew and player sell on one vehicle and day; pass midnight while serving, and with a time skip | One payment per vehicle for the finished day combining both; new-day sales wait for the next midnight | Pending |
| MS3 | Save and load before midnight, also while customers leave the window; load a schema-1 save with unpaid crew takings | Unpaid takings and sold stock survive; loading pays nothing; midnight pays once. The schema-1 takings are paid once at the next midnight | Pending |
| MS4 | Sell a truck with unpaid takings before midnight; separately leave one with the private driver | Sold truck's sales paid once on the next sales tick, books kept on the sold day. The stored truck waits for midnight | Pending |
| MS5 | Run the uninstall with unpaid cart and truck sales; load without the mod | Every unpaid sale paid once on top of vehicle and stock refunds. A failed payment stops cleanup with a retry notice and keeps the data | Pending |

## Commissary visual and logistics

Bay paint, sign and props on the door's side of the street, and warehouse logistics plans delivering to a commissary. Scenarios: `tests/game/scenarios/bay-shots.json` (BV1 to BV4, B2 to B5) and `logistics-commissary.json` (W0, W3, W6; the fixture sets up what W2 does by hand). Visual art rules: [art/README.md](art/README.md).

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| BV1 | Walk to the Hell's Kitchen bay by day and by night | White worn outline like the city's road lines with LOADING ZONE readable from the pavement; no purple | Pending on current build |
| BV2 | Stand on the pavement beside the paint, then on the road | Grey pole behind the kerb with COMMERCIAL VEHICLES ONLY, LOADING ZONE, double arrow, FOOD VENDORS and the commissary name; street-sign size; pole on the pavement | Pending on current build |
| BV3 | At the door; walk into each prop; watch pedestrians | Pallet, roll cage, hand truck and dumpster against the facade, 2 m from every entrance; you stop at them; nobody walks through or gets stuck | Pending on current build |
| BV4 | Visit all five commissaries; walk 150 m away and back; go indoors next door | Each has paint, its own sign and props; nothing in the road, on a doorstep, floating or sunk; no popping at distance; nothing shows indoors | Pending on current build |
| B2 | Hell's Kitchen (8 2nd Avenue): `teleport player place=commissary:hells-kitchen`, walk to the door, `shoot` `door`, `bay-pavement`, `bay-road`; then send a truck with `teleport <truck> place=bay:hells-kitchen` and park one by hand | Paint on the kerb in front of the door, no road between. Both trucks LEGAL with `commissary.in_bay` true, Ctrl+F3 `in the bay`, hatch to the pavement | Pending on current build |
| B3 | Lower Manhattan, the same | Paint on the door's side; props and sign by the door | Pending on current build |
| B4 | Garment District and Murray Hill, the same | Measured bays used (`(set in data)`) on the door's side; no `across the road from the door` error | Pending on current build |
| B5 | Industry City (2 21st Street), the same, plus a truck parked nose-in by hand; then a truck on the opposite kerb within 15 m | Nose-in bay in the lot facing 180 degrees; both LEGAL, `in_bay` true, Fill up works. Opposite kerb: `outside the bay: across the road from the door`, `in_bay` false, no Commissary button | Pending on current build |
| W0 | Run `tools/scenario run tests/game/scenarios/logistics-commissary.json` on a hand-made `FT logistics` save, then BizMan on the headquarters | Every step `done` or `already`; warehouse pallets with 200 hot dogs (160 after the run); the plan names the warehouse and commissary. Leave the Unassigned manager dropdown and the headquarters' schedule alone | Pending |
| W2 | By hand: BizMan, the warehouse's logistics plan, add a destination, open its dropdown | Each commissary with a space under its own name, none without a space; its foods list with stock and "runs out in"; targets accept typed numbers and plus and minus | Pending |
| W3 | Set a target above the commissary's stock for a food the warehouse has; run past 02:00; a second night at target | `Commissary logistics: <warehouse> to <id>: ...`; pallets drop by those units; the order form shows them; Fill up loads them. Second night `nothing brought (0 units)` | Checked 2026-10-14, Mac, candidate `021fd34`: 200/40 became 160/80, repeat delivery changed nothing |
| W6 | Save and load | `Commissary logistics: N plan destinations at commissaries: ...`; the dropdown still names the commissary | Checked 2026-10-14, Mac, candidate `021fd34`: a fresh process kept 160/80, the target and the manager |

## Truck hatch and art

Truck service only from the hatch, the truck interior and surface look, and the cart's centred worker. Scenarios: `tests/game/scenarios/hatch-only-selling.json` (HO1 to HO3) and `truck-look-shots.json` (TA1 to TA4, TI1, TI2). Art references: [art/README.md](art/README.md) sections "Surface look", "Paint mask" and "Driving".

The centred worker was checked on 2026-10-24 in the Windows `agent1` rig (game build 3682, session `596be1fd`, commit `b19c36a`): kerb edge at 90 degrees, right queue at 180 degrees and left queue at 37 degrees all passed, with the worker 0.85 m behind the counter at the worktop midpoint; see [docs/release/0.10.7/center-worker.md](docs/release/0.10.7/center-worker.md). An earlier 0.10.7 design that let the server stand at the cart's end or drift along the counter put people in the parking lane and was dropped.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| HO1 | Truck with stock, no crew: Open here; wait for sales | You stand at the hatch and sales come in | Pending |
| HO2 | Step out (Escape) and stay beside the truck | "Nobody's at the truck's window. ..." at once; nobody served; waiting customers leave after about 20 minutes | Pending |
| HO3 | Click the truck on foot; choose Back to the hatch | "Back to the hatch" beside Enter vehicle and Manage storage; you return to the hatch and sales resume; not offered twice while walking, nor on a closed truck, while holding a box or from another vehicle | Pending |
| HO4 | Park with the rear door against a wall; step out, walk 10 m, Back to the hatch | "You can't reach the truck's rear door from here."; beside the truck it works | Pending |
| TA1 | By day put the cart with a hot dog grill and coffee piece next to the game's own machines; repeat at night under a lamp | Steel reads like the game's, soft highlights on rounded edges, darker crevices; colours and night brightness no louder than the game's | Pending |
| TA2 | Park the truck next to the game's delivery truck by day and night | Rounded edges catch light; faint shading at seams and arches; paint no brighter than the van's | Pending |
| TA3 | Look at the cart counter from the street with two pieces fitted | The register stands on the customer-side front corner, facing the queue, clear of the pieces, pole and push bar | Pending |
| TA4 | Open the truck and stand at the hatch; look in from the pavement | The register on the serving counter at the cab end, facing the street; nobody stands in it | Pending |
| TA5 | Buy a truck in a strong colour; view all four sides, hatch closed and open; walk back 30 m; hover the mouse over it | Body panels in the colour; teal bands, steel, glass, lights and kitchen keep their own colours; no tint bleed; the game's white hover outline | Pending |
| TI1 | Park legally, hatch to the pavement, Open here; look in from 2-3 m; close the window | The window is clear; serving counter at the sill, the cooking line on the back wall under a hood; fitted pieces face the hatch; the closed hatch is flush | Pending |
| TI2 | Stay at the hatch; then put three workers on the truck and step out | You stand at the middle of the window; with you two crew beside you and the third further back; after you step out three crew across the window; nobody inside the counter, sink or fridge | Pending |
| CW1 | Open a cart with one employee on shift, queue on the right; walk round it | Employee behind the cart at the worktop midpoint facing customers; the first customer across from them | Checked 2026-10-24, agent1 |
| CW2 | Repeat with the queue on the left | Register on the left front corner facing customers; employee on the right at the midpoint; log `Cart register: cart ... register on the left front corner` | Checked 2026-10-24, agent1 |
| CW3 | Push the cart to about 0, 90, 180, 270 degrees and an odd angle; open each time | Same place relative to the cart every time, never diagonal | Pending |
| CW4 | Open on foot yourself with the employee off shift, then on shift | You stand at the midpoint (log `behind the cart at the equipment centre`); on-shift crew wait further along the same side, not overlapping | Pending |
| CW5 | While open, save, quit to the menu, load, walk back | Employee back at the midpoint; no stale queue | Pending |
| CW6 | Open and work a truck with crew | Crew and player inside behind the hatch as before | Pending |
| CW7 | Park a legal cart perpendicular to the kerb at x=42, z=82, heading 90 degrees, with an employee; then 0.9 m inland | Everyone on pedestrian ground, out of parking lanes and road exclusions; any fallback within 0.35 m along and 1.5 m across; no safe point means no visible employee or queue | Checked 2026-10-24, agent1 (kerb edge, six queue places) |
| CW8 | Inspect each fitted cart machine from both sides at right/180 and left/37 | Controls and openings face the worker; popcorn and ice-cream rear doors face the worker; fridge on the worker side; register faces the queue | Partly checked 2026-10-24, agent1: HotDogRoller, BurgerStation and fridge. Other models offline only |
| CW9 | Remove one or both worktop pieces, keep the fridge; reopen on both queue sides | Employee, player and first customer use the counter centre; the fridge does not count | Pending; offline coverage only |
| CW10 | Open beside a wall, tree or bench, then a fence with disconnected ground beyond, then block both ends | A reachable fallback behind the counter or none; never an end position or the far side of a fence; log `fallback behind the cart` with `complete player path`, or `no safe reachable serving point ... player left where they stand` | Pending |
| CW11 | From the stance use WASD, click to walk, walk 8 m away and back; close, grab, sell out, skip, save and load | Walking works at once with no snap back; solo sales pause beyond 8 m; the stance releases each time; no stuck NavMesh agent | Pending |
| QP1 | Open a cart at about 20, 45 and 70 degrees to the kerb, both ways | The longest line that fits stays on pavement; nobody in the parking strip or carriageway | Pending |
| QP2 | Repeat parallel and perpendicular to the kerb, then with a legal truck | Lines still work; truck keeps hatch-side spacing; everyone on pavement | Pending |
| QP3 | Open beside a junction, a wide parking strip and a park path | No place on the road; a park path supports a queue; no safe place means no queue while sales continue | Pending |
| QP4 | Watch arrivals, moving up, leaving and closing near a tree, bench or crossing | Every walk stays on pavement; no detour through the street; a customer with no safe way out disappears | Pending |
| QP5 | Read the `Queue view: line at cart/truck ...` and first customer's agent lines | `NavMesh agent 0, area mask -1`; rejected candidates listed by test name and count | Pending |

## Fleet recruiting

The native recruitment agency can run a campaign for the street food fleet with Basic HR and no building business. All rows pending. Startup must log `Active` for `RecruitmentAgencyDialog.NoBusinesses`, `...OnRecruitmentSettingsSet`, `RecruitmentSettings.OnEnable`, `RecruitmentSettings.SelectBusiness(Int32)` and `RecruitmentCampaignsList.SetUpContract(RecruitmentCampaign)`. Use a fresh FT character with a cart and no building business.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| FR1 | Without Basic HR, talk to an agency receptionist or call it | The game's refusal, unchanged | Pending |
| FR2 | With Basic HR, no food vehicle and no building business | The game's "no businesses" answer | Pending |
| FR3 | With Basic HR and a cart | The campaign form opens; log `Fleet recruiting: the agency offers a campaign...`; the list ends with "Street food fleet" | Pending |
| FR4 | Pick "Street food fleet"; Confirm without a schedule | Customer Service only; part-time and full-time toggles; the game's "select a schedule" | Pending |
| FR5 | Set deadline and candidates; Confirm | The game's price charged (`Recruitment campaign`); log `Fleet recruiting: campaign for ...` | Pending |
| FR6 | Open the agency's campaign list | "Street food fleet" as business with candidates and days left; cancelling works | Pending |
| FR7 | Let the days pass and hire | Candidates arrive unassigned; the vehicle crew list and `tools/play crew assign` take them | Pending |
| FR8 | With a named shop as well | Shop and fleet both listed; a shop campaign works as before | Pending |
| FR9 | Save with a fleet campaign running; load | The campaign still runs as the fleet's | Pending |
| FR10 | Uninstall with a fleet campaign running; separately sell the last vehicle first, then uninstall (was FR10a) | Campaign gone; the price of unfound candidates paid back (`Uninstall: fleet recruitment campaign ended ...`); an open fleet form's Confirm then says "select a business" and charges nothing | Pending |
| FR11 | `tools/play --help` | The hiring hint names the agency, its HR course and the fleet, with no numbers | Pending |

## Vendor API v1

The public contract and its own pending native list are in [docs/vendor-api.md](docs/vendor-api.md). Core is covered offline in `tests/Vendor/`; the Unity glue is not yet checked in game, so every native vendor assertion is pending. Contract-mismatch and fault builds (`ContractVersion=2`, VendorFaultHook) are test builds only, described in [test builds](docs/test-bridge.md#test-builds). Their results don't count toward v1 acceptance.

## Release smoke

Run on the exact final package before an upload, after the focused checks above. Patch lines come from the log check.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| RS1 | Install the package on Windows and on macOS; start a new FT game and load an existing FT save on each | Bundles load; every patch `Active`; no mod errors or exceptions in the log check | Pending for each new package |
| RS2 | Buy a cart and a truck, stock, open, serve, close; listen and look | Native models, sounds and panels; sales and stock move | Pending for each new package |
| RS3 | Save, reload, cross midnight | Vehicles, stock, crew and takings survive; one midnight payout | Pending for each new package |
| RS4 | Start without `-foodtrucks-test`; press Ctrl+K/J/U, Ctrl+V/X and their Alt variants; with `-console` list commands | `Debug actions off`; nothing spawns, grants or books; only `foodtrucks_uninstall` among `foodtrucks_*`; uninstall still asks twice | Pending |
| RS5 | Run the uninstall twice, save, close, disable the mod, load the save | Refunds paid once; the save loads without the mod with no errors | Uninstall checked 2026-10-14, Mac, candidate `021fd34`. Loading without the mod pending |

Building entry with a carried bag (issue 34). The bridge enters buildings through native navigation and `Interact`; see [docs/test-bridge.md](docs/test-bridge.md). No entry patch exists, so these check the game and the bridge together.

| Check | Do | Look for | Evidence |
| --- | --- | --- | --- |
| HB1 | Rent Third Street 9, buy and pay for 5 banana bags at a grocery, return holding the paper bag; leave the pointer over the bag HUD and `use` the entrance ID; repeat with the pointer over the world and with empty hands | You enter home with the same 5 paid bags; no money or cargo change | Pending |
| HB2 | Repeat with a shopping bag and a paid cardboard box; enter a grocery holding the paper bag | Native walk and door loading; cargo and paid flags survive each entry and exit; hand-truck pickup and release still work | Pending |
| HB3 | Try another person's home, a closed shop, a building being installed, and with the phone or map open | The game's refusal or rental screen; no forced entry; the bridge names the refusal | Pending |
| HB4 | Try an unreachable entrance, a lingering Map navigation blocker, and a walk still pending at the bridge timeout | A cannot-reach notice or the named blocker; `still_walking` or `still_entering` says to wait; keep the `Test bridge: use result ...` log line if entry fails | Pending |
