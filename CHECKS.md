# Truck colour (next build)

Branch `truck-paint`; no version bump. The colour picked at the dealer now paints the truck's body: the base colour's alpha is 1 only on the body paint, and the material uses the game's paint mask again (art/README.md, "Paint mask"). The livery's teal bands, the diamond plate, trims, glass, lights, wheels and the kitchen inside keep their own colours. A truck without a colour (the debug spawn) is cream, as before. Previews of the mask and a few tints: `build/truck-paint/sheet.png` in the worktree. Do it in a throwaway save with money for three trucks, at General US Trucks (1 Seventh Avenue).

| # | Do | Look for |
|---|---|---|
| C1 | At the dealer, pick the food truck, choose a strong colour (red or blue), buy it | The picker shows the game's swatches. There is no 3D preview for a mod vehicle (no showcase model), so nothing changes on screen until the truck spawns outside |
| C1a | Buy another truck without touching the picker | It comes in the picker's first swatch (`vehicleColors[0]`; Black if the list follows the game's asset order), not cream. Note which colour it is |
| C2 | Look at the C1 truck outside in daylight, all four sides, hatch closed and open | Box, cab, roof, hood, front fenders, rear door and both faces of the hatch in the chosen colour; the teal skirt and the bands under the awning still teal; diamond plate, rear fenders, rails, ladder, trims and wheel rims steel; glass dark; lights and the menu board unchanged. No cream left on the outside body, no colour on the steel. Then walk back about 30 m: no coloured fringes on trims, rails or the teal bands next to painted panels |
| C3 | Open the truck and look in through the hatch, and from the hatch place | Kitchen walls and ceiling cream, counters, hood and fridge steel: nothing inside in the truck's colour |
| C4 | Buy a second truck in another colour (white, black or a light one) | Same split as C2 in that colour. A light colour shows the panel shapes by shading only: the drawn seam lines and rivets go with the tint, as on the game's cars |
| C5 | Ctrl+K (debug spawn) | Flat cream, teal livery (the drawn seams and grime on the body are gone, the shapes still show); no red anywhere and no red rim along the roof edge |
| C6 | Park one coloured truck in the shade of a building and look at it at noon and at 18:00 | Darker in shade but its colour, never near black; the cream truck too |
| C7 | Save, quit to the menu, load | Both trucks keep their colours (the game reapplies the save's colour name on load) |
| C8 | Drive the coloured truck a day or two, or until the dirt shows | The game's dirt sits on top of the colour as on its own cars |
| C9 | A hired crew drives the coloured truck on its route | Still its colour after the crew's respawn (`HireRoutes` respawns it from the same saved vehicle) |
| L1 | Load, C1 to C9 | No mod `ERROR:` lines; no `RemapShaders` warning for `SH_Vehicle` |

# Commissary supply and crew restock (next build)

Branch `crew-supply`; no version bump, so the log says 0.9.10. A phone order can repeat (every day, or every week on the weekday of its first delivery), the call lists standing orders and cancels them, and each food's boxes from phone orders are limited per game week at each commissary (`rules.json` `commissary.foodBoxesPerWeek`, 10 by default; machines have no limit). A crewed vehicle in its own commissary's bay restocks before it sets off. Peter's rows take about 15 minutes in a throwaway save with a rented truck space and one hire on a route (FT bridge 2 has both); the L rows are for whoever reads the log afterwards.

| # | Do | Look for |
|---|---|---|
| S1 | Call the commissary, Order stock, open the item list | Each food reads "Hot dog, box of 500 (… here, N more boxes this week)"; machines read as before, with no limit |
| S2 | Order 2 boxes of a food, Order, Confirm order | A new question, "Just this once, or should we repeat it?", with Just tomorrow, Repeat it and Hang up. Just tomorrow books it as before ("Ordered: … arrives here at 08:00 tomorrow") |
| S3 | Order 1 box of another food, Confirm order, Repeat it, Every week | The commissary names tomorrow's weekday in the question, then texts "Standing order: 1 box of …, every <weekday> at 08:00 from day N, …". The call's start now says 1 standing order |
| S4 | Hang up, call again | A Standing orders button beside Order stock. It shows the standing order with its next day; Done goes back to the menu |
| S5 | Sleep to 08:00 (or Ctrl+Alt+F3) | "Your delivery is in" for both orders, paid as usual; the standing order's next day is a week later |
| S6 | Order more boxes of one food than the item list says are left | Order (or Every day) is refused with "We can only send N more boxes of … in the week from day …"; the form stays open to change it; Just tomorrow with fewer boxes works |
| S7 | Call, Standing orders, Cancel this one | "Cancelled your standing order: …"; the call's start says 0 standing orders and nothing arrives on its weekday |
| S8 | Leave the hired truck in its commissary's bay overnight, its route starting in the morning, with its food and a spare machine in stock there | Before the truck sets off its food goes up (the panel's storage, or BizMan's fleet card); the commissary's food stock goes down, the machine stays there (crews never fit equipment); no message pops up for it |
| S9 | Same, but with none of one of the truck's foods in stock | The truck loads what there is and sets off; the commissary texts once that day "Your crew loaded the … here (…), but we had no … for it" |
| S10 | A crewed truck with nothing on board in its bay, the commissary empty | It stays in the bay and doesn't drive to its stop (as before), and the commissary texts once that day what it had none of |
| S10a | Then order its food by phone and wait for the 08:00 delivery (or Ctrl+Alt+F3), before its stop closes | The truck loads the delivered food and sets off for its stop; no second shortage text that day |
| S11 | Give up the last space at a commissary that has a standing order (Commissary button, give up), then call it | The call says you have no space but still have the standing order, with only Standing orders and Hang up; Cancel this one works. At its next 08:00 the commissary texts once that it wasn't delivered; not again on the following days |
| S12 | Order with Repeat it, Every day, for more boxes than the week allows | The commissary says how many boxes are left and asks again "Just tomorrow / Repeat it"; Just tomorrow books it if it fits |
| L1 | Load | `Commissary state: … N standing orders (…)` in the startup state line |
| L2 | S1 | `Commissary order at <site>: the order form lists N items; boxes left this week by food: <item> N, …` (or `off (…)` when the game's wholesale limits are switched off) |
| L3 | S3 | `Commissary standing order N at <site>: Weekly, 1 <item>, first delivery day D at 08:00, paid on each delivery at that day's prices.` |
| L4 | S5 | `Commissary standing order N (Weekly) at <site>: day D booked order M 500 <item>; next day D+7.` before the usual `Commissary delivery: order M …` |
| L4a | Ctrl+Alt+F3 with a standing order due tomorrow | `Debug: N commissary orders brought forward and M standing deliveries due by tomorrow booked early; all delivered now (paid on delivery).`; the standing order's next day after that is its usual one (a week on for a weekly one), and nothing more comes tomorrow |
| L5 | S6 | `Commissary order at <site>: <item> over its weekly limit (Once): N boxes left in the week of day W, M needed.` and no `Commissary order N:` line |
| L6 | S7 | `Commissary standing order N at <site>: cancelled by phone.` |
| L7 | S8, S9, S10 | `Crew restock: truck <id> at <site> loaded …; [short of …;] … units left in stock; storage …`, once per route leg (S10: `loaded nothing; short of …`), then `Route: truck <id> leaves for stop 1 …` when it has food. S9 and S10 also `Crew restock: <id> short at <site>; the commissary texts it (once today).`, only once that day. Never a machine in the loaded list |
| L8 | A standing order refused for lack of money, or cut by the limit | `… not delivered: the payment of $… was refused (not enough money); the order is cancelled, standing order N stays.` with the text "Only today's delivery is cancelled; the standing order stays."; or `…; weekly limit cut <item> 1/2 boxes; …` with the commissary's text naming what was left out |
| L9 | Uninstall step (Ctrl+Shift+F11) in a throwaway save | `Uninstall: commissary refund …; N open orders cancelled, unpaid; M standing orders ended; …` |

# Commissary from a logistics plan (next build)

Branch `logistics`; no version bump. A commissary where the player rents a space is a destination of the game's warehouse logistics plans (`LogisticsCommissary`, Core `CommissaryLogistics`). Needs a save with a rented warehouse holding food (tacos, hot dogs or any catalogue food) on its pallets, a logistics manager with a plan from it, and a vehicle with a space at a commissary. A commissary destination's address is the game's parking street with a negative number, so the game finds no building there.

The test bridge sets that save up: `tools/scenario run tests/game/scenarios/logistics-commissary.json` on a hand-made `FT logistics` save (a food vehicle, the player outside) runs `fixture logistics`, checks one `logistics run`, saves `FT logistics ready` and loads it again (W6). W0 below is the fixture's own first live check.

| # | Do | Look for |
|---|---|---|
| W0 | The scenario above, then BizMan on the headquarters | Every fixture step `done` or `already`; the warehouse page shows the pallets with 200 hot dogs (160 after the run), the vehicle and driver in slot 1; the plan names the warehouse and the commissary. Its manager dropdown reads Unassigned: the fixture's manager has no work shift. Don't pick a manager there and don't edit the headquarters' schedule, or the game unassigns the manager and nothing is delivered. Look inside the warehouse: pallets placed beside the delivery spot (the step's detail says so) may stand in a wall |
| W1 | Startup log | `Patch LogisticsManagerPlan.DeliverDestination(LogisticsManagerPlanDestination): Active.`, `Patch LogisticsManagerPlanUI.GetListOfAvailableProducts(LogisticsManagerPlanDestination): Active.`, `Patch LogisticsManagerPlanUI.IsProductInUse(Address, String): Active.`, `Patch LogisticsManagerDestinationUI.SetUp: Active.`, then `Commissary logistics: on (…).` |
| W2 | Peter: BizMan, the warehouse's logistics plan, add a destination and open its dropdown | The player's businesses as before, then each commissary with a space under its own name (`Hell's Kitchen Commissary`), never the name of whoever occupies its building; no commissary without a space. Picking it lists the commissary's foods with the warehouse's stock and "runs out in" as for a shop; targets take typed numbers and the plus and minus buttons. Log: `Commissary logistics: destination N of the plan from <warehouse> set to <id>.` |
| W3 | Set a target above what the commissary holds for a food the warehouse has; run the clock past 02:00 | One line per commissary destination: `Commissary logistics: <warehouse> to <id>: 40 <item>, … (N units).`; the warehouse's pallets down by those units (BizMan warehouse page); the commissary's order form shows them in stock; Restock at the bay loads them. A second night with the stock still at the target: `nothing brought (0 units)` |
| W4 | A target for a food the warehouse doesn't hold, past 02:00 | The line ends `short in the warehouse: N <item>`. Peter: the game's logistics alert ("not enough stock") lists the commissary by name with that food |
| W5 | Give up the vehicle's space at that commissary (no other space there); reopen the plan; run past 02:00 | Peter: the destination reads `<commissary> (no space rented: nothing delivered)`. Log, once per load: `Commissary logistics: <id> has no rented space, so the delivery from <warehouse> brings nothing (said once until it has a space again).` Nothing leaves the warehouse. Rent a space again: the next 02:00 delivers |
| W6 | Save and load | `Commissary logistics: N plan destinations at commissaries: <id> from <warehouse>, K targets.`; the dropdown still names the commissary |
| W7 | The warehouse changes hands (give up its rent), or a business elsewhere is sold | No `ERROR` line; the commissary destination stays in the plan |
| W8 | Uninstall step (Ctrl+Shift+F11 twice) with stock a plan brought | `Uninstall: N commissary destinations removed from logistics plans.`; the commissary refund includes that stock at what the warehouse paid for it (the warehouse's own buying price, not the catalogue's); `… of this tax year's commissary deduction taken back` is the refund less that stock. The plan screen shows no commissary afterwards |
| W9 | Risky, in a throwaway copy of the save: remove the mod without the uninstall step, load, open the plan, run past 02:00 | No game errors in Player.log; the commissary destination shows as unassigned with no products, and nothing leaves the warehouse |

# Truck paint and the 1,600 kg drive (next build)

Two things on `main` after the bay-side merge; no version bump, so the log says 0.9.10. The truck's material no longer lets the game's vehicle shader paint its panels in one tint (art/README.md, "Paint mask"; since replaced by a real mask): until now the cream and teal body rendered dark red, close to black in shade. The drive rows are Peter's, at the wheel; the setup runs with the bridge in **FT bridge 2** (four trucks, so nothing needs spawning).

| # | Do | Look for |
|---|---|---|
| P1 | Before Peter drives: a truck in the Hell's Kitchen bay (`teleport <truck> place=bay:hells-kitchen`), `shoot` `vehicle-side` with `game_view` at noon, then at 18:00 and in the shade of a building | Cream box and cab, teal skirt, white trims, the burger sign on the side, as in `art/truck/renders/14-final.png`, a little darker in shade but never black or dark red; no red rim along the roof edge. Judge the colour, not the grime: the save's dirt shows as brownish patches. A little less gloss at the edges than the game's cars is expected (no paint sheen without the tint) |
| P3 | The same truck in the shade of a building, in the game's own view (`game_view`). 14 October: Peter saw it almost black in the Hell's Kitchen bay on 51f2e81 and "much better" on 9e9f982 (decals off on the truck material). The bridge's own camera still shows the side dark there, so judge colour in the game's view | Cream to khaki, never black. Checked 13 October on 20701b9: at 16:00 in a building's shade on 1st Street (`build/logs/game/paint-drive/hover-test*.png`) the body reads brownish cream in both the bridge's and the game's picture. The black side at 13:00 in the Hell's Kitchen bay, shot right after a paused teleport, is unexplained; the baked occlusion is not the cause (visible painted panels: mask G median 0.98 stored, measured by casting rays at the shipped model). Shoot a truck there again after driving it in |
| P4 | Hover the mouse over any food truck (checked by Peter on a42b4e0, 13 October: works) | The game's white outline, as on its own cars. Seen missing on 13 October (all trucks; the hover card with Click to drive shows). Cause: the SDK's HDRP assets are deferred-only, so the build stripped our vehicle shader's Forward pass, and the game's outline pass (DrawRenderers on the outlined layers, override material `02_Selection_Objects`) only draws shaders with one. The build now keeps Forward passes (`BatchBuild`); the bundle's SH_Vehicle lists Forward. Check: the white edge round body, wheels and hatch, as on a game car; nothing else about the truck looks different; the cart and a placed mod machine still outline as before. Known: a bridge `shoot` with the cursor resting on a truck now shows that edge (the bridge lifts outlines from buildings and items only) |
| P2 | `shoot` `vehicle-overhead` and one from inside the open truck at the hatch | The roof cream with its two vents; no red anywhere inside: walls cream, counters and hood steel grey. Dark but not red inside is the occlusion issue (P3), not the tint |
| D1 | From the Hell's Kitchen bay, pull away up Second Avenue and hold full throttle on the straight | A little slower off the line than before, still about 50 km/h at the top. If the game's delivery truck is at hand (the delivery job's vehicle, or one bought at General US Trucks), it should pull away a little quicker than ours |
| D2 | Brake hard from top speed | Stops straight in about the same distance as before (the brakes out-grip the tyres at either mass, so the stop is the tyres'); the nose dips a little more, the rear wheels stay down, no slide |
| D3 | Take a few junction turns at 20 to 30 km/h, one tight; bump a kerb | Leans a little more than before, never tips or lifts a wheel; climbs or stops at the kerb like the game's trucks |
| D4 | Back into the bay and park; open the window and stand at the hatch | Sits level, no bouncing after it stops; the hatch, register and crew at the same height as before |
| D5 | Overall (Peter, 13 October: heavy enough; turned too tightly at 35 degrees, right at 28) | "Slightly heavier", not a different vehicle. Say heavier, lighter or right; if wrong, say which part (pull-away, braking, turns) |
| D6 | (Checked by Peter on 9e9f982, 14 October: no crash sound, kerbs and walls fine. Log: two zero-speed touches by the player getting in and out, silent at 0 m/s, and one real hit on a fire hydrant at 11 km/h.) With the bridge on, drive the truck and steer hard at junctions, past kerbs and manholes (the crash sound Peter heard about 70% of the time on 13 October, on the 28 degree build) | No crash sound in turns: the collision hull no longer includes the wheels and stays 0.30 m up (art/README.md, the truck's hull row). Any `Collision (test bridge):` line names what the truck hit, its layer, where on the truck (local x, y, z) and which of our colliders; copy them. Startup has `Patch VehicleController.OnCollisionEnter(Collision), test bridge collisions: Active.` Also: the truck still can't be driven into walls or other cars; its wheels climb kerbs rather than sinking into them (the old hull used to ride it up); a truck teleported onto a bench or bollard is refused (`Something stands at x …, z …: …`). The startup footprint line now says about 2.80 m high, centre y 1.70 |
| D7 | Quit the game from the pause menu (checked 14 October: no mod errors in the whole session) | No `ERROR: Commissary, parked cars after a jump` or `visit check` lines after `Quitting game...` (six on 13 October: the player was destroyed first) |
| D8 | (Checked by Peter on e88e30d, 14 October: quiet far away, sounds near the truck.) Stand 90 m from a truck that crew serve, outside any vehicle; then within 15 m; then in a building near it; load a box into a truck by hand | Startup: `Patch VehicleController.OnItemAddedToCargo: Active.` No box sound from 90 m or from inside another building; within 15 m the sound with each sale, as before; loading by hand and selling at the hatch still make it. Seen 14 October: the game's box sound (HandTruckAddBox, played at the player's position on every cargo change) from a crew-served truck about 90 m away, one or two per sale |

# Bays on the door's side and a heavier truck (next build)

Peter's decisions of 13 October 2026, on branch `bay-side`; no version bump, so the log says 0.9.10. A commissary's bay must be on the door's side of the street (Core `BaySide`: the straight walk from the entrance crosses no driving lane), measured bays included, and a vehicle is in the bay only on that side; Hell's Kitchen moved to Second Avenue 8 and Industry City to Twenty-first Street 2, with a nose-in bay in the lot beside it; the truck weighs 1,600 kg with springs and dampers scaled to match; the bridge's `bay_candidates` takes any building's address. Run with the bridge in **FT bridge**. The rule version changed, so every stored bay is searched again on the first load. Log rows in `tests/game/logchecks/0.9.10.txt` (`bayside.*`).

| # | Do | Look for |
|---|---|---|
| B1 | Load `FT bridge` | For each of the five commissaries, `Commissary <site> (…): bay searched from … ; not resolved yet.` (the stored bays were under the old rule), then within seconds `Commissary <site>: bay on legal parking at x …, z …`. Never `ERROR: Commissary <site>: no bay this load`: every commissary ships with a bay on its door's side |
| B2 | Hell's Kitchen, now Second Avenue 8: `teleport player place=commissary:hells-kitchen`; walk to the door; `shoot` `door`, `bay-pavement`, `bay-road` | `Commissary hells-kitchen (ba:street_secondavenue#8): bay searched from x 267.8, z 332.5 (nearest entrance)`, then `bay on legal parking at x 267.8, z 326.1` or within a metre of it (the scouting's pick, 6.6 m walk). Paint on the kerb in front of the door, no road between; the sign behind the kerb (`sign at x …, z …, 0.40 m behind a kerb …`), plate to the road; props against the facade either side of the door, none in front of it. The map's Street food line lights Second Avenue 8 |
| B2a | `teleport <truck> place=bay:hells-kitchen`; check `state`; drive the truck out and park it in the paint by hand | Both times the panel says LEGAL, `state`'s `commissary.in_bay` is true and Ctrl+F3 says `in the bay`; the Commissary button shows; the hatch faces the pavement |
| B3 | Lower Manhattan: the same with `commissary:lower-manhattan` | The bay moved off the far kerb (it was at x -567.1, door at x -551.0): paint on the door's side. The props and sign still stand by the door (M21, M22). If instead `no bay this load`, copy the line: it needs a measured point or a move like B2 |
| B4 | Garment District and Murray Hill: the same for each | Their measured bays (x 305.7, z -311.6 and x 43.8, z -281.8) are still used (`(set in data)`, `bay on legal parking at …`) and the paint is on the door's side. If instead `ERROR: Commissary <site> (…): the measured bay in data/commissaries.json at x …, z … is across the road from the door …, so it isn't used.`, copy the line and shoot the door and the old bay: the door the rule chose may not be the one people use |
| B5 | Industry City, now Twenty-first Street 2: `teleport player place=commissary:industry-city`; walk to the door; `shoot` `door`, `bay-pavement` and a `vehicle-overhead` of a truck sent with `teleport <truck> place=bay:industry-city` | `Commissary industry-city (ba:street_twentyfirststreet#2): bay searched from x -1439.0, z -1073.4 (set in data)`, then `bay on legal parking at x -1439.0, z -1073.4, … nose-in in a lot, facing 180 degrees`. Never `the measured nose-in bay … isn't legal by the wheel test` (if it shows, copy it: it names the nearest legal place). In the overhead the truck stands nose-in in one stall, between the stall lines, nose to the building, rear to the aisle, the paint (2.5 m wide) in that stall only and no parked car in it. The marker line ends with `sign at …, at the stall's head, … behind a kerb` or `… the pavement's edge`, the sign at the stall's head with its plate facing the aisle, or `no sign (…)` with what each spot found; never a sign standing in the next stall or in the aisle. Props by the door as usual |
| B5c | At Industry City: the truck from B5 and one parked by hand nose-in in the same stall | The panel says LEGAL; `in_bay` true; Ctrl+F3 `in the bay`; the Commissary button shows and Restock works. |
| B5a | At any commissary with a bay: park a truck legally on the opposite kerb within 15 m of the bay point; then in the paint; press Ctrl+F3 at each and `state` | Opposite kerb: Ctrl+F3 says `outside the bay: across the road from the door`, `state`'s vehicle `commissary.in_bay` is false, no Commissary button on the panel, and pressing its key says `not here` rather than `park legally`. In the paint: `in the bay`, `in_bay` true |
| B5d | Rent a truck space at Hell's Kitchen, leave the truck in its bay; save; load the save from the main menu shortly before 04:00 and run the clock past it | No fine. If the street side wasn't readable at 04:00 the log says `Overnight check through day N waits: the street side of the bay can't be read yet for … (no driving lanes read round the door); tried again each minute.`, then `Overnight check through day N: … in its bay …; 0 fines` |
| B5b | Watch every load's log | Never `bay on legal parking` for a site whose lines just before say `no driving lanes found round the door`; if that error appears (once per commissary per load, `… so the street's side can't be checked; tried again every 5 s.`), copy it with the door's height: the entrance stands far above or below its street |
| B6 | Spawn a truck (Ctrl+K) beside the game's delivery truck (a van from the delivery job, or a DeliveryTruck bought or spawned with the console); drive each from a standstill to full speed on a straight avenue, then brake hard from 50 km/h; park the food truck and look at it from the pavement | The food truck pulls away a little slower than the delivery truck and than 0.9.10's truck, still reaches about 50 km/h, and stops in about the same distance without the nose hitting the stops. Parked it sits at the same height as before (the hatch and the crew at the window where 0.9.10 had them) and doesn't bounce more. Peter: "slightly heavier", not a different vehicle |
| B7 | `tools/ft call commissary op=bay_candidates address=ba:street_twentyfirststreet#4 radius=60` | A reply with `site` and `status` null, `address` echoed, one origin per entrance (`kind` `entrance`), every spot with `same_side`, the automatic pick (if any) with `same_side` true. Log: `Building ba:street_twentyfirststreet#4: bay candidates within 60 m (test bridge): from entrance x …, z …: N legal, S on the door's side, M the automatic pick; T ms.` Then `address=21st` and an unknown number such as `ba:street_twentyfirststreet#999` are refused with a message, and `site` with `address` together is refused |
| B8 | `tools/ft call commissary op=bay_candidates site=industry-city radius=30` | One origin, `kind` `data`, `same_side` true; every spot facing 180 degrees (`heading` 180 or -180), the automatic pick at x -1439.0, z -1073.4 |

# Mod round after the bridge sessions (next build)

Fixes for the mod findings of the three bridge sessions, on branch `mod-round`; no version bump, so the log says 0.9.10. Run with the bridge (`tools/run-remote.sh start homepc`) in **FT bridge**. The test bridge rows below still hold; row 36 there is still open.

| # | Do | Look for |
|---|---|---|
| M1 | Load `FT bridge` with the player far from Hell's Kitchen | Once per resolved bay: `Commissary <site>: bay swept on load, N parked car(s) kept out.` Never `, K left` |
| M2 | `teleport player place=commissary:hells-kitchen` from far away, then `shoot` the bay (`bay-road`, `bay-pavement`) | `Commissary hells-kitchen: the player jumped N m to within 150 m of the bay; swept before the frame was drawn, K parked cars kept out.` No car anywhere in the paint, ends included |
| M3 | Run the clock past an hour with the player 100 to 150 m from the bay | No car in the paint afterwards; the minute line `kept N parked cars out of the bay (…)` if any were |
| M4 | Load near Hell's Kitchen and walk to the door; `shoot` `door` | `Commissary props hells-kitchen: props by the door at x …, z … (facade facing …): pallet …, …`. Props against the facade, clear of the door, hydrant and walkway. Any `left out:` line in full, with what stands there |
| M5 | Load the save again | Superseded by B1 (Industry City moved; no commissary logs `no bay this load` now). Was: for industry-city only (garment-district and murray-hill have measured bays now, M15): `… no bay in the save; searched again after load.`, then one `ERROR: Commissary industry-city: no bay this load (…)`, once per load |
| M6 | `tools/ft call commissary op=bay_candidates site=<site> radius=80` for those three | A candidate list with walk lengths; log line `Commissary <site>: bay candidates within 80 m (test bridge): …`. Pick a spot with a short walk, teleport a truck there, check `legal` and shoot it |
| M7 | Startup log | One `Patch ParkingSimulator.RunDaily: Active.` and one `Patch ParkingSimulator.RunHourly: Active.` line |
| M8 | Rent a truck space at Hell's Kitchen; keep it legally in its bay for several hourly ticks, then on paid street parking for several ticks, return to its bay at 23:50 and run past midnight; keep a second truck on paid street parking | One `Commissary hells-kitchen: no public parking for … for N h in the rented bay today (observed since load).` summary for the bay truck, counting only waived hourly ticks before midnight. Its "Public parking" debit and Econoview parking row still include every earlier paid street hour; the second truck pays normally. The 00:00 bay hour belongs to the next day's summary. Commissary rent still paid |
| M9 | Leave a truck in a commissary bay where it has no rented space for several hourly ticks and past midnight; repeat with a rented truck that remains in its own bay all day | The unrented truck pays the normal public parking charge and has no exemption summary. The all-day rented truck has no public parking debit; one nightly summary counts its waived hours. M8's earlier street hours remain charged even when that truck is in its rented bay at midnight |
| M10 | Open the truck, stand at the hatch, `shoot` `vehicle-side` and a shot from inside if a pose allows | The cash register is about a third of a metre wide, on the counter just cab-side of the player, screen towards the player, not floating, sunk or through the frame; the middle of the window is clear |
| M11 | Same with crew serving (one, then three) | No body clips the register |
| M12 | Load near Hell's Kitchen; `shoot` `bay-pavement` | The marker line ends with `sign at x …, z …, 0.40 m behind a kerb … m past the paint` or `… behind the pavement's edge (no raised kerb) …`, and the sign stands on the plaza side by the paint, plate to the road. If it says `no sign (…)`, copy the whole bracket: per spot it says whether there was no kerb or pavement, or what the pole would stand in |
| M13 | Leave one truck legally in the Lower Manhattan bay with no space anywhere, and a rented truck out of its own bay, past 04:00 | The first: `Phone: The City of New York: Your … had no commissary space at 04:00 on day N. A food vehicle out overnight needs a space …`. The second: `Your … wasn't in the bay of Hell's Kitchen Commissary, where it has its space, at 04:00 …`. Never `wasn't in a commissary bay` |
| M14 | Spawn a truck twice in a row without moving (Ctrl+K or `debug spawn truck`); then spawn where there is no room (between parked vehicles) | Two `Spawned foodtrucks:vehicletype_foodtruck: id …, at x …, z ….` lines with different places; the second truck stands beside the first, not inside it. With no room: `No room to spawn here: move to an open spot.` and `Not spawned … no room …` in the log, no truck |
| M15 | Load; go to Garment District and Murray Hill | `Commissary garment-district (…): bay searched from x 305.7, z -311.6 (set in data)` and the same for murray-hill at x 43.8, z -281.8, then `bay on legal parking at …`; paint at the kerb across the pavement from each door, no car in it. Industry City still logs `no bay this load` (open for Peter) |
| M16 | Teleport the player to the Hell's Kitchen door (`commissary:hells-kitchen`) on a fresh load, before the props are placed | The props line names the building as the facade (`… layer Buildings`), never `layer Player`; props against the facade as in M4 |
| M17 | Make a bay resolve after the load while you look at it (delete its stored bay in an FT save, or change its `bay` in `data/commissaries.json`, with a lane car in the paint area) | `new bay swept with the view rule, …, K left for now.`, then `Commissary <site>: paint waits for K parked car(s) in view to leave the bay.`; turn away, then `the bay is clear; its paint is drawn.` and paint with no car in it. Bays searched on load log `new bay swept under the loading screen, …` and no second `bay swept on load` line |
| M18 | Load far from Lower Manhattan, run the clock a few hours, then walk to its bay | No car stands in the paint when it appears; if one was in view, the `paint waits …` and `bay is clear …` lines as in M17 |
| M19 | `tools/ft call commissary op=bay_candidates site=hells-kitchen radius=10`, then `radius=80` | The same spot flagged `automatic` in both, and its `distance` at most 30 |
| M20 | Stand at the Industry City door facing the facade and spawn a truck (Ctrl+K or `debug spawn truck`); then against any wall | Never a truck inside a building: it spawns beside you on the street or not at all, with `No room to spawn here: move to an open spot.` and `Not spawned … (in front: <collider> on <root>, layer … is in the way from where you stand)` |
| M21 | Load near Lower Manhattan and walk to its door; `shoot` `door` | `Commissary props lower-manhattan: props by the door at x -551.0, z -123.7 (facade facing … degrees, <collider> on <root>, layer Buildings, … m from the door point): pallet …`; props against the facade, none in front of the door. If none fit, the line names what stood there; `no building wall on a ray …` means the street ray found no wall at all |
| M22 | `shoot` `bay-road` at Hell's Kitchen and Lower Manhattan, and one from the pavement | From the road the plate stands in front of the pole, face to the road, double arrow along the kerb; from the pavement the pole stands in front of the plate. Lower Manhattan's name lines read cleanly, nothing of Hell's Kitchen showing through |
| M23 | Open a truck where about one customer an hour comes, and one at night where nobody comes; watch a truck's queue go | `About 1 customer an hour here right now` (`About N customers …` otherwise), at night `Hardly any customers here right now`, never `About 0 customers`; queue log lines for a truck read `Queue view: truck …`, for a cart `Queue view: cart …` |
| M24 | Crew serve an open truck a whole day; run past midnight; read the bank's transactions | One `Crew: <name> worked HH:00 on day N (open window, …); counted for the game's midnight wage.` per hour served, and at midnight one game "Unassigned Daily Wage" per crew member equal to those hours times their hourly wage, nothing else for them: the hour lines only count hours, the game's wage is the one payment |
| M25 | Open a truck alone where nobody comes (the Lower Manhattan bay at night) | `The truck is open with … Hardly anyone comes by here right now.`; with crew on it, `… Hardly any customers here right now.`; a truck's hourly spot line reads `Truck … spot on day …` |

# Test bridge (next build)

The game side of the test bridge (`docs/test-bridge.md`): HTTP on `127.0.0.1:8330` inside the game, only when it starts with `-foodtrucks-test`, plus a `Notice:` or `Phone:` log line for every notice and text the mod shows. On `main`; no version bump, so the log says 0.9.10. Two live sessions so far (`build/logs/game/live1-end-214811`, `live2-end-082641`); the rows below marked "after live2" are fixes from them. Start the game once without the flag (rows 1 and 2), then with it: `tools/run-remote.sh start` runs `steam.exe -applaunch 1331550 -console -foodtrucks-test` on homepc. From the Mac, `ssh -L 8330:127.0.0.1:8330 homepc` and `curl http://127.0.0.1:8330/health`. Use a throwaway save named **FT bridge**, made from a **new character** of its own (start a new game, save it as FT bridge). Never make an FT save by saving a series city under a new name: the bridge refuses any save whose character also has saves not starting with FT.

| # | Do | Look for |
|---|---|---|
| 1 | Start without the flag, load any FT save, play a minute | `Test bridge: off (the game was started without -foodtrucks-test).` once at startup. `curl http://127.0.0.1:8330/health` on homepc gets no answer. Everything else as in 0.9.10 |
| 2 | Same session: open a cart where it can't open, spawn a truck (Ctrl+K), call a commissary | Every notice on screen also appears in Player.log as `Notice: <the same text>`, and each commissary text as `Phone: <commissary name>: <text>`. Nothing looks different on screen |
| 3 | Start with the flag, stay at the main menu, `curl …/health` | `Test bridge: listening on http://127.0.0.1:8330/ (session <8 hex>).` in the log. The reply has `"scene":"menu"`, `"save":null`, the mod version and a growing `frame` |
| 4 | Alt-tab away from the game for a minute, `curl …/health` twice | `frame` still grows (the game runs in the background) |
| 5 | `load` `{"name":"FT bridge"}` from the menu, poll `/health` | `scene` goes `loading`, then `city` with the save's name, day and time. No question about mods |
| 6 | `state` | Clock, money, player position, the licence, every food vehicle with its position, legality, stock and queue, the commissary. Compare a few values with the screen |
| 7 | `debug` `{"action":"spawn truck"}`, then `state` | A truck appears in front of you; the reply's `notices` has "Spawned …"; the truck is in `state.vehicles` |
| 8 | `time` `{"pause":true}`, then `{"speed":4}`, then `{"run_until":{"day":"+0","time":"<an hour from now>"},"speed":10}` | The game pauses (pause overlay as with the pause key), then runs four times as fast, then runs fast and stops paused at that time, speed back to 1. Hourly things (the crew's hour lines, wages) happen on the way |
| 9 | `money` `{"set":50000}` | The top bar shows $50,000; no transaction in the bank app |
| 10 | `teleport` `{"who":"player","place":"commissary:hells-kitchen"}`, then `{"who":"<truck id>","place":"bay:hells-kitchen"}`, then `{"who":"player","place":"vehicle:<truck id>"}` | You stand by the garage door facing it; the truck stands in the bay paint, hatch to the pavement, legal, on a free part of the paint (row 29); you stand on the pavement by its hatch. Try it once from inside a building too |
| 11 | `vehicle` `enter` on the truck, `rent_bay`, `restock`, `exit`, then `enter` again, drive to a legal spot, `open`, `step_out`, `back_to_hatch`, `close` | Each does what its button does, with the same notices, and the reply's `state` follows. `open` while paused is refused with a message |
| 12 | Open the phone, `ui` `{"op":"list"}`, click an app by text, `ui` `{"op":"escape"}` twice | The list has the phone's buttons with their labels and the phone's text under `texts`; the click opens the app; escape closes it, then the phone. With nothing open escape is refused |
| 13 | At the commissary's Commissary button (list picker open), `ui list` | Its choices are listed as `option` entries; clicking one and then the confirm button runs it |
| 14 | `shoot` `{"name":"bay-day","pose":"bay-pavement","target":"bay:hells-kitchen"}`, `{"name":"truck-side","pose":"vehicle-side","target":"<truck id>"}`, `{"name":"door","pose":"door","target":"commissary:hells-kitchen"}` | PNGs in `…\LocalLow\Hovgaard Games\Big Ambitions\FoodTrucksTest\shots\`. Is the exposure like the game's view (not black, not blown out)? Any UI in them? The game's own view doesn't flicker or move while they're taken |
| 15 | `console` `{"line":"help"}` | The reply's `output` holds the console's command list |
| 16 | `save` `{"name":"FT bridge 2"}`, then `load` `{"name":"FT bridge"}` from the city | The save appears in the Load screen. The load goes to the main menu and on into the city without a question |
| 17 | Load by hand a disposable save whose name doesn't start with FT, never `HART. YT`; then try `money`, `time`, `save` and `ui click` | Each is refused: the save's name doesn't start with FT (`ui works only in a loaded FT city; use load from the main menu.` for the click). `state` still answers |
| 18 | `GET /events?since=1` after a few of the rows above | Log lines, `notice` and `phone` events, and any Unity error; `next` grows. `new_game` returns `ok: false` saying why |
| 20 | Start a `save` and at once `curl …/health` and `GET /events` | Both answer straight away while the save runs. The save's reply has an `op` number; `{"cmd":"op","args":{"id":N}}` says `done` |
| 21 | `time` `{"run_until":{"day":"+0","time":"<the next hour>:59"},"speed":20}` | It stops paused on that :59 exactly, and `state.clock.arrived_at` says so. Then run to the next HH:00: the hour's events have run when it stops |
| 22 | Park a truck legally in a bay, `state` | The truck's `legal` matches the panel's LEGAL; while you drive it `legal_reason` is "panel's live reading: Legal", otherwise it starts "wheel parking test". A cart on the pavement says "pavement rule" |
| 29 | After live2: with a parked car in the Hell's Kitchen paint (or the player's own car parked there), `teleport` the truck to `bay:hells-kitchen` | The truck goes to a free place on the paint if the paint has room, else the reply names what stands there (`a parked car (…)`, the player's car). With `"clear": true` a parked NPC car goes and the truck takes its place; the player's car never goes. Run the clock: the truck stays put |
| 30 | After live2: drive the truck, `teleport` it into the bay with you in it, then `state` and look at the panel | The panel says LEGAL at once (no leaving and re-entering); `legal` true with "panel's live reading: Legal"; `vehicle open` works there |
| 31 | After live2: at the hatch, `vehicle step_out` | The reply's `notices` has the "Nobody's at the truck's window…" notice |
| 32 | After live2: `shoot` `door` and `bay-road` at Hell's Kitchen | `door`: the entrance in view, seen from the street side. `bay-road`: from the driving lane, the paint (LOADING ZONE) in frame |
| 33 | After live2: `tools/ft call console line=help --json` (and `--json` before `console`, and between key=value arguments) | JSON on stdout each way |
| 34 | After round 6: teleport a truck facing 45° to another heading, and read the log | One `Test bridge: footprint of foodtrucks:vehicletype_foodtruck: …` line, about 2.2 m across and 6 m long, whatever the heading |
| 36 | After live3: `shoot` `door` at Hell's Kitchen with the player standing behind the facade's line (e.g. at the door), and `bay-road` | The facade is textured, not a pale ghost, in both; the game's own view still cuts away what stands between its camera and you |
| 35 | After round 6: teleport the empty truck from one district into the Hell's Kitchen bay, sleep past midnight | The "Public parking" charge is priced by Hell's Kitchen (the bay's district), not the district it came from |
| 23 | `commissary` `{"op":"order","site":"hells-kitchen","items":[{"item":"<a food's name>","boxes":2}]}` with a space rented there | The same `Commissary order …` log lines as an order by phone, no money taken; the order is in `state.commissary.orders`. At 08:00 its payment shows in `state.transactions_today` |
| 24 | Hire a worker (Ctrl+Alt+U), `state.employees`, then `crew` `assign` and `unassign` on the truck | The worker is listed, joins the truck (notice as from BizMan, `done: true`) and leaves again |
| 25 | From a non-FT city, `load` `{"name":"FT bridge"}` | Refused, the reply says to go to the main menu or load from an FT save |
| 26 | In FT bridge, open the pause menu (Esc), `ui list`, then click its Save button by id | Refused: `ui click refuses the game's save and load controls; use the save and load commands.` Resume and Options still click |
| 27 | Save the FT bridge city by hand under a name without FT (same character), then `money` | Refused: `This save's character also has saves not starting with FT (…); make FT saves with a character of their own.` Delete that save afterwards |
| 28 | `save` `{"name":"FT bridge"}` | The reply has the file's path and size; the log has `Test bridge: saved 'FT bridge', N bytes.` |
| 19 | `quit` | The reply comes, then the game closes within a second or two |
| 37 | After live4: in FT bridge, `save` `{"name":"FT bridge 2"}`, then `load` `{"name":"FT bridge 2"}` from the city; once in the city, `load` it again at once | Both loads reach the city: `Test bridge: to the main menu, to load 'FT bridge 2'.`, `Test bridge: at the main menu; loading 'FT bridge 2' once its loading screen has gone.`, `Test bridge: loading 'FT bridge 2' (…)`, `Test bridge: load 'FT bridge 2' done: …`. `/health` goes `loading`, `menu`, `loading`, `city`, never stuck on `loading` with the main menu on screen. This is row 16's second half |
| 38 | After live4: only if a load still hangs (main menu on screen, `/health` `loading` for over 10 s) | Either `Test bridge: the bridge's load started a loading screen while another was still up; …` followed by `ERROR: Test bridge: the bridge's load died with its loading screen (another was still up); cleared the game's loading flag, so loads work again. …` and a second `load` that works without a restart; or, with no such line, `load` refuses with "The game's loading flag is on with no loading screen; the bridge can't tell …" and the flag is left alone. Tell me which, if this ever shows |
| 39 | After live4: put a truck at garment-district's kerb (`teleport` x 305.7, z -311.6, heading 180) and one at industry-city x -1522.9, z -1065.7; `shoot` `vehicle-side` of each and `door` at `commissary:garment-district` | No black or inside-a-wall picture. Where the facade is near, the reply's `pose_note` says so (`…, pulled in to N m in front of …` or `from the hatch side was blocked; taken …`) and the shot's log line has the same note in brackets. `camera` in the reply says where it stood. Hell's Kitchen `vehicle-side` and `door` still come out as before, `pose_note` null |
| 40 | After live4: `teleport` `{"who":"player","x":-1490,"z":-1150}`, then the same with `"y":5.06` | First: on the street (y about 0), reply `surface` "the walkable surface nearest the mover's height (…) of 2: 5.06, 0.05" or similar, and the same in a `Test bridge: teleport to …` log line. Second: on the deck if there is walkable ground there, else refused with "No walkable ground within 3 m"; `surface` says "the surface nearest the y asked for" |
| 41 | After live4: `teleport` a truck to x -1532.56, z -1148.9, heading 0, first without and then with `"clear": true`; `shoot` from above (free camera, row 43); run the clock 2 game hours and shoot again | Without `clear`: refused, naming the van (`a parked car (…)` or `the building's delivery-job van (…)`, perhaps "hidden now"). With `clear`: either refused for the delivery-job van, or the parked car goes and the reply's `in_the_way_after` is `[]`. A hidden van left there after a move shows in `in_the_way_after` and as an Info line `… stands where a hidden delivery-job van has its stall (hidden): …`, never as an ERROR. From above, the truck stands in no car, before and after the 2 hours |
| 42 | After live4: `state` `player.needs`; then `time` `{"run_until":{"day":"+2","time":"09:00"},"speed":60}` with the player on foot, and step into and out of a truck afterwards | `needs` has energy, hunger, happiness, `collapse_at` -20 and `kept_up`; `clock.keep_needs` true while it runs. `Test bridge: needs topped up while the clock runs (energy … to 100.0, hunger … to 100.0), day …` lines on the way; no collapse, no `Test bridge: the player collapsed …` line, no hospital bill, no clock jump to 08:00. `keep_needs` is false again after arrival |
| 43 | After live4: `tools/ft shoot top --at <x>,30,<z> --yaw 0 --pitch 89 --fetch build/shots` and `tools/ft shoot across --at <x>,1.7,<z> --look-at <x2>,1.5,<z2> --fov 50 --fetch build/shots`; MCP `ft_shoot` with `x`, `y`, `z`, `look_at` | PNGs fetched; each reply's `camera` matches what was asked (pitch 89, yaw 0; fov 50). A camera put inside a building gets a `pose_note` warning |
| 44 | After live4: first `load` after starting the game, then `GET /events` and `/health` | The `load '…' done` event's day and time match `/health` then. If they differ, a `Test bridge: the clock jumped from day … to day … in one go: <why>.` line names why. `/health` has `time_skip` (false unless the game's time skip runs) |
| 45 | After live5: on a rainy day with the player at a truck's hatch, then on the plaza, `shoot` `door` and `bay-road` at Hell's Kitchen and `vehicle-side` of the truck (row 36 again) | No pale ghost and no white cut line anywhere in the pictures: facades and the truck textured top to bottom. The game's own view still cuts away what stands between its camera and you |
| 46 | After live5, reconciled with row 53 (live sessions 8 and 9: the deck at x -1490, z -1150 is the game's walkable bridge, so it isn't a refusal case): with the player near Hell's Kitchen, `teleport` to x 412, z 200 (inside a block: ground, no NavMesh), then to x 383, z 212 with `"y": 4` | First: refused, `No walkable ground at x 412.0, z 200.0: no walkable surface of 1: 0.01 not walkable; …`. Second: refused, `… near y 4.00: no walkable surface within 1.5 m of the y asked for (4.00); surfaces there: 0.01 walkable; …`. The player doesn't move either time (both seen in live session 10) |
| 47 | After live5: `time` `run_until` to a few minutes ahead at speed 60, five times | `run_until … reached at` names the target minute every time, never the minute before |
| 48 | After live5: `tools/ft --json shoot x --pose door --target commissary:hells-kitchen --fetch build/shots` | `result.local_path` is the fetched PNG |
| 49 | After live5: a long `run_until` with `keep_needs`, then `state` `player.needs` | `happiness_modifiers` lists the game's modifiers with hours left; happiness isn't topped up (by design); energy and hunger are |
| 50 | After live5: drive a truck a little, park it, pause the clock, `teleport` it 30 m away by `x`/`z`; `state` and a `vehicle-overhead` shot; then unpause for a few seconds and read `state` again | While paused, `state`'s position and the shot both show the truck at the target, not partway from its old place; after unpausing it stays there and drives normally (no jerk, no sliding) |
| 51 | After live6: in `FT bridge 2` (day 4) with the player where live6 saw the ghost (at the Hell's Kitchen door, at the truck's hatch, on the plaza), `shoot` `door` and `bay-road` at Hell's Kitchen and `vehicle-side` of the truck, each with `"compare": true, "game_view": true` | The shot (`<name>.png`) shows the facade and the truck textured top to bottom, no pale copy, no white cut line. Whenever `<name>-asis.png` shows the ghost, the reply names its cause: `uncovered` for the game's view fade (`… (CityBuildingController, reveal 0.00, blocking the game's camera)`), or `hovered` for the game's hover or card highlight (live10). `<name>-game.png` is the game's own view. Then take a game screenshot by eye or a second `game_view`: the game's view still fades what stands between its camera and the player, as before. Also look at the shadows in that `game_view`: if a faded building's shadow appears in the game's view right after a bridge shot and stays until the light updates, a light uses cached shadows and the bridge's render refreshed them (review round 10, nit 3) |
| 52 | After live6, extended in live session 10: stand the player at the open truck's hatch in the Hell's Kitchen bay (the game's camera then rests the idle cursor on 22 1st Street, opens its card and draws it pale with a white edge), `shoot` `vehicle-side` with `"compare": true, "game_view": true` | `hovered` names the building (`SM_MB Building 31 (under the mouse, its building card open, 6 objects outlined)`); no pale building or white edge in `<name>.png`; the `-asis` picture has it; `-game.png` still shows the card and the pale building in the game's view |
| 53 | After live6: `teleport` the player to x -1515, z -1150; then to x -1490, z -1150 with `"y": 5.06` | First lands on the street under the bridge (no refusal for `BridgeMiddleRoadColliders`). Second: live session 8 showed the deck at y 5.03 is the game's walkable bridge (NavMesh and a complete walking route from the street, people and benches on it in the game's view), so the player lands there; `surface` lists both heights as walkable and ends "walking routes checked from the player's place". A deck with no route would be refused |
| 54 | After live6: `teleport` the player onto a parked car and onto another pedestrian | Both still refused, naming the car or the person |
| 55 | After live6: a rain shot of a truck with `"game_view": true` | Live session 10: the open truck at the hatch is black in rain in the bridge's shot and in the game's own frame at the same moment (`rain8-side`, `rain-1345-overhead` and their `-game.png`), and 60 frames change nothing, so it's the truck's materials in rain (art track). If a shot is black while `-game.png` shows the same truck lit, tell me |

Log lines:

- Startup: `Test bridge: listening on http://127.0.0.1:8330/ (session <8 hex>).` or `Test bridge: off (…)`; a taken port gives `Test bridge: could not listen on 127.0.0.1:8330 (…)` and the game goes on without the bridge.
- Every notice: `Notice: <text>`; every text: `Phone: <contact>: <text>`, the player's own read-back `Phone: <contact>: [player] <text>`. A raw key (`Notice: foodtrucks:… {…}`) means the locale file lacks the key: tell me.
- Commands: `Test bridge: running the clock at speed N until day D HH:MM.`, `Test bridge: run_until day D HH:MM reached at day D HH:MM; paused, speed 1.`, `Test bridge: money set from … to ….`, `Test bridge: moving the player to x …`, `Test bridge: moved <type> <id> to x …`, `Test bridge: vehicle <id> <op>.`, `Test bridge: clicked "…" (…).`, `Test bridge: escape: ….`, `Test bridge: shot <name> (…): <path>.`, `Test bridge: saving as '…'.`, `Test bridge: loading '…' (…).`, `Test bridge: quitting the game.`
- Any `Test bridge, refresh:` or `Test bridge, every frame:` error line: tell me.
- Every mod error line now starts `ERROR: ` after the mod's prefix.
- After live2: `Test bridge: cleared a parked car (…) at x …, z ….` when `clear` sent one away.
- After round 6: `Test bridge: footprint of <type>: … m across, … m long, … m high, centre …` once per vehicle type.
- After live4: `Test bridge: at the main menu; loading '…' once its loading screen has gone.` on every load from the city; `Test bridge: teleport to x …, z …: y …, <surface>.` on every x/z teleport; `Test bridge: needs topped up while the clock runs (…)` during long runs; `Test bridge: the clock jumped from … to … in one go: …` on any jump of more than 30 game minutes; `Test bridge: the player collapsed at …` on a collapse (only a collapse, not a doctor's visit); `Test bridge: needs top-ups stopped: …` if the save stops being an FT save of an FT-only character; `Patch LoadingScreen.StartLoading, test bridge load: Active.` at startup with the flag. Any `ERROR: Test bridge: … stands in something after the move` or `ERROR: Test bridge: the bridge's load died with its loading screen …`: tell me. A hidden delivery-job van after a move is an Info line with "(hidden)", not an error.
- With the flag only: `Patch GameManager.RunMainGameTick(Single), test bridge run_until: Active.` and three `Patch SaveGameSerializationHelper.…, test bridge save: Active.` lines. Loads end with `Test bridge: load '…' done: day D, the mod's save data attached.` (or `… failed: …`); vehicle ops log `Test bridge: vehicle <id> <op>: done.` or `not done.`

Game calls to watch first (read in the decompile, never run): the HttpListener under the game's Mono; `GameManager.SetMinutesMultiplier` and `GameSpeedController.SetPlayerPause` for `time`; `GameManager.SetPlayerPosition` and `VehicleHelper.TeleportVehicleToGround` for `teleport`; `TransitionToSave` with `LoadScene.LoadTransitionToSave`, and `LoadScene.LoadMainMenuFromCity` run from the bridge's own object, for `load`; `CancelButtonHandler.HandleEscapeClick` for `escape`; the second camera with HDRP's camera data copied by reflection for `shoot`; TextMeshPro read by reflection for `ui` labels and texts.

# Commissary look and payment (0.9.10)

Peter's notes of 11 October 2026: commissary orders are paid on delivery, the purple bay is now white road paint with a loading zone sign and props by the door, and crews' sales are paid in after midnight like the game's shops. Build 0.9.10 on branch `milestone-8`; the log says 0.9.10. Fresh **FT** save, other gameplay mods off. The 0.9.9 rows below still hold, except that the paint is white with LOADING ZONE in it instead of purple.

| # | Do | Look for |
|---|---|---|
| 1 | Rent a space at a commissary, note your money, call it and order 2 boxes of something, read back, confirm | The read-back ends "… $… in all, paid on delivery here at 08:00 tomorrow". The confirmation says "Ordered: … for $…, paid on delivery". Your money doesn't change, and no transaction appears |
| 2 | Wait for 08:00 (or press Ctrl+Alt+F3) | The commissary texts "Your delivery is in: … $… paid. Load it at the bay." Money goes down by goods plus fee in one transaction "Commissary order from …". Econoview shows the fee under delivery fees today. The stock is at the commissary |
| 3 | Order again, then spend money until you have less than the order's total, and wait for 08:00 (or Ctrl+Alt+F3) | The commissary texts "We couldn't deliver … the payment of $… didn't go through. The order is cancelled; order again when you have the money." No money moves, no stock arrives, the call's start says 0 orders on the way, and nothing is taken later when you have money again |
| 4 | Tax statement (Ctrl+X twice) after row 2 | The paid order is deductible under "Commissary rent and deliveries" |
| 5 | With delivered stock at the commissary and one order still open, run the uninstall step (Ctrl+Shift+F11 twice) in a throwaway save | The notice pays back only the stock ("Commissary stock paid back: $… Open orders are cancelled; they were not paid yet."). Nothing comes back for the open order |
| 6 | Walk to the Hell's Kitchen commissary (First Street 22) by day, then by night | On the parking lane across from the garage: a white outline like the city's own road lines, slightly worn, with LOADING ZONE in it reading from the pavement. No purple anywhere. Does the white match the street's lines, or is it brighter or greyer? Is the lettering readable, and does it look like paint rather than a sticker? |
| 7 | Same place, on the pavement beside the paint | A grey pole just behind the kerb with a white sign: COMMERCIAL VEHICLES ONLY, LOADING ZONE, a red double arrow, FOOD VENDORS, Hell's Kitchen Commissary. Read it from the pavement and from the road. Is it the size of the city's other street signs? Does the pole stand on the pavement, not in the kerb or the road? |
| 8 | At the garage's door | A pallet stacked with cardboard boxes, a wire roll cage with cartons, a hand truck with boxes and a green dumpster against the wall on either side of the door. Nothing stands in the doorway or out in the walking line, nor in front of any other entrance (the building's other doors, a neighbour's shop door): each prop keeps 2 m from every entrance. Do they look like the game's own things, with the same soft shading and rounded edges as the game's items (the sign and the mod's props now have the surface look)? Screenshot |
| 9 | Walk into each prop, and watch pedestrians pass | You stop at it rather than walk through. People walk round them and in and out of the door; nobody gets stuck or walks through a prop |
| 10 | Drive a truck into the bay, over the lettering and the sign's side of the paint | Nothing blocks the truck in the bay; it parks LEGAL inside the paint as in 0.9.9. Ctrl+F3 still says "in the bay" |
| 11 | Walk 150 m away from the bay and look back; go into a building next door and out again | The paint, sign and props stay where they are (no popping out at 100 m as the purple did). Indoors nothing shows through |
| 12 | Visit the other four commissaries | Each has its paint, a sign with its own name, and props by its door. Tell me any where the sign or props are missing, stand somewhere odd (in the road, on a doorstep, floating, sunk), block a path or stand in front of any door, the neighbours' included |
| 13 | Call a commissary and open the order form's item list (ADD ITEM) | Food rows show the game's food icons as before. Equipment rows show the small model a cart or truck carries (once the art track's icons are merged; until then the game's icon, and one log line per piece). The furniture stores' own list still shows the game's icons |
| 14 | Give a truck a crew and a route for the day, note your money in the morning and stay away from it (more than 100 m), awake, until evening | No sale notices and no notice for its stops closing on time all day (a sold-out or licence close would still show); your money doesn't move with its sales. Econoview's Street food row for the truck shows today's sales as they happen |
| 15 | Same day, stand by the crewed truck (within 100 m) but not at the hatch, and watch the line for a few minutes | The customers queue and are served, but no sale notices show and money doesn't move. Then step in at the hatch: from then on each sale shows its notice and is paid at once, as before |
| 16 | Let midnight pass (awake), with a crewed truck still selling at 23:59 | At midnight your money goes up once per crewed vehicle, before the game's day summary shows, one transaction "Street food sales by the crew: <vehicle>, day N (… items)" each, dated the day before. The game's day summary and Econoview count the street food for that day as before (compare with row 14's Econoview figures) |
| 17 | With two crewed trucks on routes, sleep through a whole working day (or skip time) | No pile of sale or closing notices when you wake (only a sold-out or similar close shows). One transaction per truck for each midnight slept through. Does the game stutter less while sleeping than in 0.9.9 with the same trucks? |
| 18 | Tax statement (Ctrl+X twice) after row 16, and the uninstall step in a throwaway save on a day with crew sales not yet paid in | The statement counts the crew's street food the same as in-person sales. The uninstall pays the waiting crew sales in at once (one transaction per vehicle and day) |

Log lines:

- Rows 1 to 3: `Commissary order N: 2 boxes, … at <site> for $… plus $… delivery, paid on delivery; due day D at 08:00.` At 08:00 `Commissary delivery: order N, … at <site> (ordered day …, due day D), paid $… on delivery (goods $…, fee $…).` or `Commissary delivery: order N, … not delivered: the payment of $… was refused (not enough money); the order is cancelled.` Ctrl+Alt+F3: `Debug: N commissary orders brought forward and delivered (paid on delivery).`
- At load: `Commissary state: … N orders on the way (paid on delivery), … M rent charges and K fines unpaid; …`.
- Row 5: `Uninstall: commissary refund $… for the stock delivered; N open orders cancelled, unpaid; … spaces given up.`
- Rows 6 to 12, once per bay when first drawn, the 0.9.9 marker line now ends with the furniture: `…; a 5.8 m truck fits wholly inside; LOADING ZONE 0.70 m high, 5.6 m long; sign at x …, z …, 0.40 m behind a kerb … m past the paint`, with `no sign (…)` or `no lettering (…)` when one wasn't placed. The props have their own line once decided: `Commissary props <site>: props by the door at x …, z …: pallet +2.5 m along the facade, roll cage …, hand truck -2.5 m …, dumpster …` with `left out: <prop> (why)` for any that didn't fit; `(next to the entrance at x …, z …)` is a neighbour's or second door. A try that waits for the street (`no props yet (no traffic lanes near the door)`, all props off the NavMesh) or for a vehicle or you to move (`a vehicle or the player stands there`) logs `Commissary props <site>, try N: …; trying again in 10 s.` Tell me every "left out", every try past the first and "no sign". `Commissary bay: the game's DeliveryPallet prefab not loaded …` or `… has no plain meshes to copy` means the mod's own model stood in. `Commissary bay: <name>_Display.prefab missing from the mod's bundle.` or `M_BayPaint missing` is a build error.
- Rows 14, 15, 17: one line per crewed vehicle per game hour, `Crew sales: <id>, day N HH:00 hour: C customers, I items, $…, paid in after midnight; U units left.`, instead of one `Sale:` line per item. In-person sales keep their `Sale:` lines. A crew's window closing on time away from the player: `Truck <id>: closing notice not shown (the crew's window, the player away or asleep).`; a sold-out close logs its `closed (foodtrucks:notification_sold_out)` line and shows its notice.
- Startup: `Patch BusinessHelper.RunDaily: Active.` (the midnight payment) next to `Patch EmployeeHelper.PayDailyWages: Active.`
- Routes, after review round 2: a route from an older test save that fails the rules logs `Route: truck <id> has no plan: its route fails the rules (Overlap); the crew waits until a stop is changed on the BizMan page.` once, and its BizMan page says the crew doesn't run it and why.
- Rows 16 to 18: at midnight, for each truck on a route, `Route: truck <id> caught up to minute M for the day's close.` (M a multiple of 1440), then `Crew sales paid in: <vehicle> (<id>), day N: I items, $….` once per vehicle and day, before the game's day summary lines (or at the uninstall step). The last `Crew sales: <id>, day N 23:00 hour: …` line of a day must say day N, not N+1.
- Row 13: `Commissary order form: no icon Icon_<Name> in the mod's bundle; <item> shows the game's icon.` once per piece until the art track's icons are in.
# Crew and routes in BizMan

The vehicle panel's Hires dialog becomes a page in BizMan (Peter, 11 October 2026). Built on top of `milestone-8` (branch `worktree-agent-abec0297f63eaec2b`); no version bump yet, so the log still says the milestone-8 version. Fresh **FT** save with one truck, one cart and three unassigned employees with customer service; own at least one nightclub or office near where the truck stands. Other gameplay mods off. The page is built from copies of the game's own BizMan parts and nobody has seen it yet, so screenshots of every step help.

| # | Do | Look for |
|---|---|---|
| 1 | BizMan, Street food line, click the truck's card | The truck's page replaces the list: a Back button, the truck's name, where it is, "Crew: 0 of 3", three crew rows, "Route: 0 of 6 stops", Add stop on the map, Partner venue. Screenshot it. Back returns to the fleet section with the other businesses above it |
| 2 | In crew row 1, open the dropdown | Unassigned, then each unassigned employee with customer service, satisfaction and wage. Pick one: the notice says they joined, the row keeps them, and they are gone from rows 2 and 3. Pick Unassigned: they leave. Close the phone at any point and reopen: the change stuck |
| 3 | Drive the truck, press Hires on its panel | BizMan opens on the truck's page, not the old list picker |
| 4 | On the truck's page, Add stop on the map. Click a building on a street with parking | The phone closes, the map opens with "Click a building…". The building's card shows "Add as a stop" in place of Set destination. Click it: a yellow "New stop" marker appears at the kerb within 40 m and a question names the address and distance. Confirm: the map closes, then Opens at and Closes at offer only free half hours. The page reopens with the stop |
| 5 | Repeat row 4 on a building with no parking lane nearby (a park, a pier) and once on a far-away building | "No legal parking space within 40 m…" for the first. For the far one either a spot or "The streets round … aren't loaded yet" |
| 6 | Add a second stop the same way, then on the page change stop 1's opening time to after stop 2's | The list re-sorts, the times shown match, and Closes at keeps the stop's length where it fits. A time that overlaps the other stop isn't offered |
| 7 | Drag stop 2 above stop 1 | The places swap and the time slots stay where they were. Tell me if the drag doesn't start (the log names the drag handle) |
| 8 | Remove a stop with its remove button | A confirm question, then the stop goes. Cancel leaves it |
| 9 | Change Days to Monday to Friday | The fleet card's Route line says so after Back |
| 10 | Put a crew member on the truck, sleep to a stop's opening | The crew drives to the snapped spot, parks with the hatch to the pavement and opens. Screenshot the truck at the stop |
| 11 | Same for the cart: a map stop, a crew member, wait for the opening | The marker and the cart stand on the pavement by the kerb, not in the lane or by a door. The cart opens there |
| 12 | Push the cart, stop on a good pitch, press Add stop here on its panel | The old exact-spot flow: times asked, stop added where it stands |
| 13 | On the truck's page, Partner venue dropdown, with the truck parked within 50 m of your venue | Your venue is offered; pick it, then No partner. The notices say signed and ended |
| 14 | Open the map yourself (M), click a building | Set destination as usual, no Add as a stop |
| 15 | Give a stop 22:00 to 02:00, then try to add a stop from the map in the early morning | Opens at offers nothing before 02:00; Closes at for a stop opening at 00:00 offers no time at all. No stop can run longer than 24 hours. (With every-day or weekday routes; all the Days presets run two days in a row) |
| 16 | Add stop on the map, confirm a spot, then close the Opens at picker without choosing (its X or Esc) | BizMan reopens on the vehicle's page with no new stop |
| 17 | Add stop on the map, click a building so the spot question shows, then press Esc to close the map and answer the question with Confirm | Nothing is added, no time picker opens; the log says `Map pick: the confirm for <id> came after the pick ended (map closed); nothing added.` |

Log lines:

- Startup: `Patch BuildingResume.SetButtons: Active.` and `Map pick: on ("Add as a stop" in the building card).`
- Row 1: `BizMan: vehicle page built from the warehouse driver row (…) and the logistics plan (…; stop rows dragged by …); it sits in … (laid out by its parent | sized by itself, scrolling | no scroll view).` Send me the whole line. Then `BizMan: vehicle page for truck <id>.` An error `BizMan vehicle page: <part> isn't where 1.0 had it. The vehicle panel's Hires button opens the old crew and route dialog instead.` means a copied part is missing; Hires then logs `Hires: the BizMan vehicle page isn't available; the old crew and route dialog opens for <id>.` and opens the old dialog.
- Row 2: `Crew: <name> (…) on truck <id>.` and `Crew: <id> off truck <id> (unassigned on the BizMan page).`
- Row 4: `Map pick: started for truck <id>.`, `Map pick: "Add as a stop" copied from the building card's Set destination button.`, `Map pick: truck stop by <address> at x …, y …, z …, heading …, … m from the entrance; legal parking, Pavement: hatch side …; … parking colliders, … lane segments near (… ms).` Tell me the ms. Then `Map pick: ended for <id> (stop picked).` and `Route: stop … at x …, z …, heading … hh:mm-hh:mm added to truck <id>; 1 stops.`
- Row 5: `Map pick: no truck spot by <address> (entrance x …, z …): no legal place with the hatch to the pavement within 40 m (…)`, or `neither parking lanes nor traffic lanes within reach`.
- Rows 6 to 8: `Route: stop 1 of <id> now hh:mm-hh:mm (BizMan page).`, `Route: stop 2 of <id> moved to 1; the times stay in their slots (BizMan page).`, `Route: stop 1 removed from truck <id>.`
- Row 10: the usual `Route: truck <id> arrived at …: …, N m from the stop.` N under 10 m means the snapped spot was free.
- Row 11: `Map pick: cart stop by <address> …; by a parking lane, pavement on the right … m out, cart … m out` or `by the nearest driving lane (no parking lane fits)`.
- Row 1, after review round 1: the page's build line also names the copied list's layout parts, `the copied list's verticalLayoutGroup at '', contentSizeFitter at ''` (or `none`); an error `the copied list's … inside the copy … isn't where 1.0 had it` means the page fell back to the old dialog.
# Art look (next build)

The surface-look pass of 11 October 2026 (art/README.md, "Surface look"): every model now has the game's kind of material (base colour with baked grime, a mask with metallic, occlusion and smoothness, a normal map with rounded edges) and smoothed normals. Also new: the umbrella furls when the cart isn't selling, a cash register on the cart and in the truck, no street dirt on the mod's vehicles, and icons for every equipment piece. Fresh **FT** save, other gameplay mods off. Screenshots for rows 1 to 4.

| # | Do | Look for |
|---|---|---|
| 1 | By day, put the cart with a hot dog grill and a coffee piece fitted next to the game's own hot dog grill and coffee machine (a shop that sells them, or your own store), and look from the pavement | Our steel reads like theirs: light, slightly metallic, with soft highlights on rounded edges, not a flat bright surface with sharp edges. Crevices under lids and round knobs are darker. Colours aren't louder than the game's |
| 2 | The same by night under a street lamp | Our pieces don't glow brighter than the game's items next to them; steel goes darker, like theirs |
| 3 | Park the truck next to the game's delivery truck or a van, by day and by night | Rounded edges and corners on the cab and box catch the light; panels show faint shading near seams and wheel arches; the paint isn't brighter than the van's. Tell me if any part looks too dark, too shiny or blotchy |
| 4 | Look at the icons of the mod's items (Extra Storage cabinet, griddle, corn grill, dumpling steamer, dumplings, tacos, chopped cheese, grilled corn) in a shop list or the inventory, next to the game's | Icons look like the game's: the item seen from the front left and a little above, the same size in the frame, no background |
| 5 | Park the cart over a dirty stretch of street (litter or stains on the asphalt) and walk round it; then park the truck over one | No dirt or litter shows on the cart's body, counter, wheels, umbrella, equipment or register, nor on the truck's body. Dirt still shows on the street around them |
| 6 | Push the cart, set it down, open it for business, close it, and save and load while it's open | The umbrella is furled round the pole (a striped roll with a strap) while pushed, set down and closed, opens when the cart opens, furls when it closes, and is open again after loading the open cart. While pushing, the furled umbrella doesn't touch the player |
| 7 | Look at the cart's counter from the street, with two pieces fitted | A small dark cash register stands on the front right corner by the umbrella pole, facing the door side, clear of both pieces, the pole and the push bar |
| 8 | Open the truck and stand at the hatch; look in from the pavement | The register stands on the serving counter at the cab end of the window, facing the street; you and the crew don't stand in it |
| 9 | (Equipment icons) Open the commissary's order list once it uses them | Each compact piece shows its own icon (art/equipment/<Name>/Icon_<Name>.png), not the game's icon of the full-size machine |

Log lines:

- Row 6: `Cart umbrella: cart <id> open, selling.` and `Cart umbrella: cart <id> furled, not selling.`, once per cart and state per session. `Cart umbrella: cart <id> has no Cart_Canopy / Cart_Canopy_Furled` is an error: an old prefab got loaded.

# Playtest fixes (0.9.9)

Fixes from the 0.9.8 agent run of 11 October 2026 (log `build/logs/game/agent-098-092245`), with the review round 4 fixes. Build 0.9.9 on branch `milestone-8`; the log says 0.9.9. Fresh **FT** save, other gameplay mods off. Rows 1 to 6 here replace the 0.9.8 rows 8 to 11 below (bay paint by the door).

**In the bay** for a truck means two things at once: the game calls it legally parked (the panel's LEGAL; its own wheel test), and its middle is within 15 m of the commissary's bay point. The paint shows the legal parking inside that circle, so a truck standing wholly inside the paint is in the bay. A legal space outside the paint but within 15 m (past a gap in the paint, say) also counts; ILLEGAL never does. A **cart** is in the bay when its middle is within 15 m of the bay point, on the pavement or in the lane (Peter's decision; the game has no parking test for a pushed cart).

| # | Do | Look for |
|---|---|---|
| 1 | Walk or drive to the Hell's Kitchen commissary (First Street 22), by day and by night | The purple paint lies on the parking lane across the street from the garage door (the west kerb, where trucks park), not by the door. No paint on the road or on the kerb by the door. The paint is empty: no parked car stands in it, now or after a day of traffic |
| 2 | Drive the truck there, park inside the paint with the hatch to the pavement | The panel shows LEGAL. Ctrl+F3 says "in the bay". The Commissary button shows |
| 3 | Rent a space there (Commissary, rent), park inside the paint, and leave it past 04:00. Then do it again, but save inside a building before 04:00, load that save there and sleep past 04:00 | No fine for the truck either time, and the overnight log line says "in its bay". A truck without a rented space there is fined as before |
| 4 | Park on the garage's own kerb by the door (ILLEGAL), then at a legal space on the bay's kerb about 20 m past the end of the paint | Ctrl+F3 says "outside the bay: not legally parked" for the first, "outside the bay" for the second. Pressing Commissary on the first says to park legally inside the purple bay |
| 5 | Visit the other four commissaries (or teleport near them), by night | Each has its paint on a parking lane near the garage, on the garage's own street, and empty of parked cars. Park a truck in it: LEGAL and "in the bay". Tell me any with no paint, paint on another street or far from the garage (more than half a block), or a parked car in it |
| 6 | Keep watching the Hell's Kitchen paint for a minute from the pavement, then turn away and back | No parked car vanishes while you look at it. A car that stood in the paint is gone once you have looked away |
| 7 | Open a truck at a busy spot and watch a line of 5 or more for 15 minutes | The customers stand in one straight line along the pavement from the hatch, about 1 m apart, running towards the truck's back where there is room, all at the same distance from the truck and clear of its side. Nobody circles their place. On a narrow pavement the line may be shorter. Screenshot from the pavement |
| 8 | Step out of the truck and let the line give up (about 20 minutes) | Each customer who gives up shows the game's red angry emoji over their head as they walk off |
| 9 | Truck with a griddle full (160/160), tacos in stock at the commissary, and hot dogs too (no hot dog grill fitted): Restock | "Nothing on the truck holds Hotdog: fit its equipment first. Tacos: the Griddle is full (160/160). … items stay in stock here." With nothing missing a piece: "Nothing more fits in the truck. Tacos: the Griddle is full (160/160). …" |
| 10 | Back a truck against a wall so its rear door is unreachable, open it, step out, stand at its rear corner and choose Back to the hatch | You are put at the hatch, not told the rear door can't be reached |
| 11 | Push a cart to the Hell's Kitchen paint. Still pushing it, stop on the pavement beside the paint, then in the paint, then 20 m along the pavement past the paint's end, and at each look at the panel and press Ctrl+F3. Then rent a space for it in the bay, set it down by the paint and leave it past 04:00 | While pushing, beside and in the paint: the Commissary button shows and Ctrl+F3 says "in the bay". 20 m past the end: no button, "outside the bay". (Set down, the button goes and Ctrl+F3 measures from you, so check while pushing.) No fine for the cart left by the paint |

Not changed: the first Step out's "Nobody's at the truck's window" notice. The 0.9.8 log has its line at the first Step out (line 291), so it most likely showed and the agent missed it; row 2 of "Truck serves only from the hatch (0.9.8)" still checks it.

Log lines:

- Rows 1 to 5, at startup: `Commissary bays: the truck's wheels from its prefab: (+1.80 along, +0.87 across, 0.54 up), …` once (the wheel controllers stand about 0.54 m up, 0.16 m above the hubs) (an error line means TruckAssets' numbers are used). Per commissary, `Commissary <site> (<address>): bay searched from x …, z … (set in data | nearest entrance); not resolved yet. | resolved at x …, z … (from the save). | no legal parking found (kept in the save).` For a new save, soon after load, `Commissary <site>: bay on legal parking at x …, z …, … m from x …, z … (set in data | entrance), the lane along … degrees; resolved after load in … ms and kept in the save.` For Hell's Kitchen the distance should be about 0.1 m. Tell me the ms figures. `Commissary <site>: no legal parking for a truck within 30 m of …` is an error: that commissary has no bay (no paint, no vehicle is ever in it, its rented vehicles aren't fined) and needs a measured bay point. `bay not resolved yet (…): the city's parking lanes aren't loaded` before a success is fine.
- Near each bay, `Commissary marker <site>: bay paint at x …, z …, along … degrees, 1 stretch of … m (…), 2.40 m wide, legal parking at its middle and both edges every half metre; the lane's middle +0.00 m across from the bay point, … m from the driving lane; the paint lies within the 15 m bay (… m either way along); a 5.8 m truck fits wholly inside.`
- Rows 1, 5, 6: `Commissary <site>: kept N parked cars out of the bay (G as the lanes parked them, S already standing there).` at most once a minute while it happens. Startup: `Patch ParkingLaneGenerator.GenerateParkedVehicles(Int32, Boolean): Active.`
- Rows 2 to 4 and 11: Ctrl+F3 also logs `Debug: at x …, z …; nearest commissary <site>, … m (bay radius 15 m): in the bay | outside the bay: not legally parked (the game's wheel test) | outside the bay.` A truck's reason names how legality was decided; `the game's saved parking state (the city's parking lanes aren't loaded)` should not appear while you stand there.
- Startup or the first look: `bay not resolved yet (…): no parking lanes round it yet` before a success is fine, and one commissary waiting doesn't hold up the others; a commissary that never resolves keeps its 04:00 check waiting, so tell me. Two errors ask for a measured bay point in `data/commissaries.json`, each for one entrance (origin): `the door at … has no short walk to the street (…); no bay from this entrance` (a loading dock or a door cut off from the pavement, say) and `the only legal parking within 30 m of … is out of a short walk from the door (N too long a walk, M with no route); no bay from there` (the parking is on the street behind, say). The commissary has no bay only when every one of its entrances fails; then the `no legal parking for a truck` error follows. Tell me about any of them. `bay not resolved yet (…): the traffic system isn't ready to check the door's street` before a success is fine.
- Row 3: `Overnight check through day N: <id> in its bay; …`. After the indoor reload, no `waits` line, or one `Overnight check through day N waits: the bay of <site> isn't resolved yet (parking lanes not loaded).` followed by the check once you go out. Tell me if you see that.
- Row 7: `Queue view: line at cart <id>: 14 of 14 places on the right side towards the back, 1.0 m apart as wanted, 2.0 m out from the middle; as placed: 1.98…2.02 m out (0.88 m clear of the side at the least), 0.97…1.03 m apart, 5 ways in and out.` A line cut short says fewer places. At the line's removal: `0 stopped after circling their place` or few.
- Row 8: once a session, `Queue view: customers' emoji runner added (the character prefab has none).` No `NullReferenceException … ThirdPersonCharacter+<ShowExpression>` in the log.
- Row 9: `Commissary restock: full pieces: Tacos: the Griddle is full (160/160).` after the usual restock line.
- Row 10: `Truck window: back to the hatch of truck <id>: no path to the rear door at …, but the player is beside the truck.`

# Playtest fixes (0.9.8)

Fixes from the 0.9.7 playtest of 10 October 2026, and a painted bay at each commissary. Build 0.9.8 on branch `milestone-8`, with the first review round's fixes; the log says 0.9.8. Fresh **FT** save, other gameplay mods off.

| # | Do | Look for |
|---|---|---|
| 1 | Commissary call, Order stock, ADD ITEM, add a box, then press the form's ORDER while the item list is still open | The list closes and the commissary reads the order back at once. Do it again with HANG UP, and with the phone's own CLOSE [ESCAPE]: each works with the list open and closes it. In 0.9.7 none of the three reacted until the list's X or Esc |
| 2 | Square Appliances' order form (needs an address of your own), ADD ITEM, then click the form's ORDER with its list open | Unchanged from the game: nothing happens until the list is closed. Only the commissary's list lets clicks through |
| 3 | Open a truck at a busy spot with a line of 5 or more and watch the people walk up, move up and leave for 15 minutes | Nobody spins or circles on the pavement. Everyone stands on their own place, about 0.8 m apart, facing the window; nobody stands on the place behind theirs or overlaps the person in front. Anyone still spinning or standing off their place: note where (at the front, in the middle, walking up). A gap in the line can be expected: a customer who waits more than 10 s for a blocked place is taken out of view (log: the blocked-place line) and their place stays empty until the line moves |
| 4 | Truck panel with a hot dog grill and hot dogs loaded; then a cart with the same | Each slot's orange "i" is at the bottom right; the amount ("80/80" on the grill, "80" and "x1" on the hot dogs) is readable at the bottom left. Hovering the "i" shows the storage tooltip as before. Does the "i" sit well there, clear of everything? |
| 5 | In Contacts, look at Street Food Bookings, a commissary and the City of New York | Street Food Bookings and the commissary have no red X; DELETE ALL in their category leaves them. The City of New York keeps its X, as in the game (it is the game's contact; the mod adds it again on the next load). Play through the afternoon of day 1: Street Food Bookings stays |
| 6 | Truck in the bay: Commissary, "Order stock for delivery to …", then HANG UP | The call ends: CALL turns green and "Never mind, I'm no longer interested" appears under the call at once. Same after a call started with CALL, and for the bookings and City calls |
| 7 | Restock a truck with one hot dog grill from 1,500 hot dogs in stock | "Loaded into the truck: fitted Hotdog Grill; 80 Hotdog (the Hotdog Grill holds 80). 1420 items left in stock here." Restock again: "Nothing more fits in the truck: the Hotdog Grill holds 80. 1420 items stay in stock here." |
| 8 | Walk towards the Hell's Kitchen commissary (First Street 22) | Within about 100 m a purple painted bay shows on the road by its door: an outline along the parking lane with COMMISSARY written in it. Walk away: it goes past 100 m. Is it easy to see in daylight and at night? Does it look like road paint? |
| 9 | Drive the truck there (Ctrl+K, then drive) and park wholly inside the paint, both ends | The Commissary button shows on the panel and Ctrl+F3 says "in the bay" anywhere inside the paint. With the truck's middle just past one end of the paint it says "outside the bay" (the bay counts the vehicle's middle, so a nose or tail past the paint still counts). Does a truck fit inside the paint at a legal parking space? |
| 10 | Drive and park on the painted bay | Nothing blocks the truck; cars and people pass over the paint. The paint shows from farther away while you drive or push a food vehicle |
| 11 | Visit the other four commissaries (or teleport near them) | Each has its paint by its door, on the side of the door where the parking lane is (a stretch that stops at a junction or a gap stops there too). Tell me any where it is missing, on the wrong street, past a junction, or sits on the pavement or across the driving lane |
| 12 | On a sloped or cambered street (a bridge approach, the Hamptons), look along a long bay's outline | The outline lies on the road all along: no part sinks under it or floats above it |

Log lines:

- Row 1: `Commissary orders: the item list's backdrop (<names>) lets clicks through to the phone while the commissary's list is open.` once a session, then `Commissary order at <site>: its item list is closed (read back | hung up | the call ended).` A `no full-screen backdrop under the item list` line means the click blocker is elsewhere; tell me if ORDER still does nothing.
- Row 3: at the first customer, `Queue view: customer agent speed 1.20 m/s, stopping distance 0.10 m (now 0.30), radius 0.30 m, acceleration …, angular speed …, avoidance …; arrives within 0.50 m.` If anyone circled: `Queue view: cart <id>: a customer circled their place 0.62 m off without getting closer; put there.` once per line (put there only once nobody else stands on the place; `stopped where they were (their place is off the NavMesh)` otherwise), and the line's removal says `…; N stopped after circling their place (K put there, worst … m off), at most M walks by one customer`. `customer <id> has walked to 17 targets` means the line itself keeps sending someone to new places. `customer <id> waited 10 s for someone to clear their place; taken out of view.` means two people blocked each other's places; one goes from view and their sale is paid.
- Row 4: `Storage panel: the cargo slots' info button moves to the bottom right on food vehicles (… px from the edge, … px, slot … px), clear of the amount.`
- Startup: `Patch ContactScrollerController.LoadContact: Active.`, `Patch ContactsHelper.GetPermanentIds: Active.` and `Patch VehicleOverlay.UpdateOverlay(EntityController): Active.` A morning with the bookings contact missing logs `Catering: the bookings contact was missing from the phone; added again.`
- Row 6: `Commissary call: foodtrucks-commissary-<site> hung up (started from the bay | started with CALL); the call is over, CALL is ready.`
- Rows 8 to 11: one `Commissary marker <site>: bay paint at x …, z …, along … degrees, 1 stretch of 19.2 m (-9.6…+9.6 m) on the parking lane … m from the driving lane, … m wide; the door stands 11.5 m from the lane's middle, so the 15 m bay reaches 9.6 m either way along the kerb; a 5.8 m truck fits wholly inside.` per commissary when first drawn. A `doesn't reach the street` error means no parked vehicle there is ever in the bay; a `has no stretch of 2 m or more` error means the game has parking there but none within the bay, so nothing is drawn. A site with two separate bay points logs one line each. From the 0.9.7 log, Hell's Kitchen's door is about 11.5 m from where the truck parked, so the bay covers about 19 m of kerb, centred on the door: the truck at z 221.1 was in (13.7 m), at z 214.0 out (18.4 m), and the player's first spot 16 m out.

# Truck interior (0.9.8)

The truck's kitchen is rebuilt: the cooking line now runs along the back wall, opposite the hatch, and all four fitted pieces stand there full size, facing you at the hatch. The hatch side has only a clear serving counter, the sliding windows park past the opening's rear end, and the 0.9.7 ledge with its two small pieces is gone. Renders: `art/truck/renders/interior-*.png`. Fresh **FT** save, other gameplay mods off; the log says 0.9.8.

| # | Do | Look for |
|---|---|---|
| 1 | Ctrl+K truck. Park legally with the hatch to the pavement, "Open here", then stand on the pavement 2 to 3 m from the hatch and look in (walk out with Step out, or look with the camera from outside) | The window is clear from end to end: no unit, shelf or glass pane in it. Through it: the serving counter at the sill, behind it the aisle, and along the back wall a steel counter under a hood, with a fridge at the rear end. Screenshot |
| 2 | Close the window and look from the pavement | The hatch shuts flush; nothing pokes through the closed hatch or the walls |
| 3 | Open again and stay at the hatch; look from the game camera | You stand at the middle of the window, seen from the chest up, nothing in front of you. Screenshot |
| 4 | Put three workers on the truck (staff 3, Ctrl+Alt+U) and open with you at the hatch; then step out | With you: two crew beside you in the window and the third waiting further back by the fridge; nobody stands inside the counter, the sink or the fridge. After you step out: three crew across the window. Faces visible from the pavement |
| 5 | Fit four pieces, Extra Storage among them (Ctrl+U fills the slots; the first piece is Extra Storage). Look through the window with the hatch open, and from the cab end and the rear end | All four stand on the back counter in a row, full size, each facing the hatch (controls, doors and glass towards you), the first two right behind the middle of the window. None pokes into the wall, the hood, the fridge or a neighbour. How do they read from the street? Too small, too hidden? |
| 6 | Take one piece out of the cargo, then save and load | The piece leaves the counter at once; the others keep their places. After loading the pieces fill the counter from the middle out |

Log lines: `Vehicle displays: truck … line 2 spot, …`, `… line 3 spot, …`, `… line 1 spot, …`, `… line 4 spot, …`, once each. No `lacks equipment spots` error.

# Truck serves only from the hatch (0.9.8)

A truck now sells only while you stand at its serving hatch or a crew member serves there. Stepping out stops it, however close you stay; a cart is unchanged (standing by it serves). Same **FT** save, other gameplay mods off.

| # | Do | Look for |
|---|---|---|
| 1 | Truck with stock and no crew: "Open here", wait for a couple of sales | You stand at the hatch and sales come in; no "Nobody's at the truck's window" right after opening |
| 2 | Press Escape (Step out) and stay right by the truck for a few in-game minutes | At once "Nobody's at the truck's window. Customers wait a while, then walk on. Click the truck and choose Back to the hatch to serve again." The hatch stays open; nobody is served; after about 20 minutes the waiting customers give up and walk off (angry emoji). No sales |
| 3 | Click the truck on foot | Its small panel shows "Back to the hatch" next to "Enter vehicle" and "Manage storage". Click it: you walk to the rear door and stand at the hatch again. The window stays open, the people still waiting stay in line, and sales resume. Does the button look like the game's own? Click the truck a few times: Back to the hatch stays in the same place each time. While you walk there, clicking the truck again doesn't offer it a second time |
| 4 | With one worker on the truck (Ctrl+Alt+U, no route): open, stand at the hatch a while, then step out | The worker serves beside you, then on alone after you step out; no "Nobody's at …" message. The log has a `paid for` line for each hour the worker served |
| 5 | Cart: open it and walk 5 m away, then 15 m | Still serving at 5 m; at 15 m "Nobody's at the cart's window", as before. Clicking the cart shows no "Back to the hatch" |
| 6 | At the hatch, skip time with any of the game's time skips that works from there (if none does, note that and skip the row) | You are put outside by the rear door after the skip. Back to the hatch puts you back |
| 7 | At the hatch, save; load that save | You stand outside by the truck, the window still open, nobody serving. Back to the hatch puts you back |
| 8 | Close the window, then click the truck | No "Back to the hatch" on a closed truck; get in and "Open here" as usual |
| 9 | Hold a box and click the open truck; then drive another vehicle next to it and click it from the seat | No "Back to the hatch" in either case |
| 10 | Park so the rear door is against a wall or a barrier, open, step out, walk 10 m away and choose Back to the hatch | "You can't reach the truck's rear door from here." and you stay where you are. Right beside the truck it puts you at the hatch anyway |
| 11 | A truck on a crew route, open at a stop with two crew: click it and choose Back to the hatch | You stand in the middle of the window, the crew either side; a crew member who doesn't fit waits inside. When the route closes and drives on, you are put outside before it moves |

Log lines: `Truck window: player stepped out of truck … (stepped out)`, then `Window <id>: nobody serving the truck; the player left the hatch: stepped out.` No sale messages for that truck until you are back at the hatch. Row 10: `Truck window: back to the hatch of truck <id>: no path to the rear door at …; refused.` Row 3: `Truck window: back to the hatch of truck <id>: walking to …` and `Truck window: player placed at the hatch of truck <id> (back to the hatch) at …`. Each step-out names its cause: `stepped out`, `a time skip ended the activity`, `Unstuck or another game move took the player off the hatch`, `out of energy`, `the activity panel closed`, `the game ended the activity`; after a load the nobody-serving line says `the player hasn't been at the hatch since the save was loaded`.

# Truck driving (0.9.8)

The truck now drives like the game's delivery truck instead of the example sports car: the delivery truck's weight, suspension, power curve and brakes, 40 kW and a 50 km/h top speed (art/README.md, "Driving"). Same 0.9.8 build, same **FT** save.

| # | Do | Look for |
|---|---|---|
| 1 | Ctrl+K truck. On a long straight, beside the game's delivery truck (the 40-slot box truck from a truck dealership; the Honza Mimic too if one is at hand), floor it from standstill to top speed, then brake hard to a stop. Do it twice on the flat and once uphill | The food truck pulls a little slower than the Delivery Truck and clearly slower than the Honza, tops out at about 50 km/h on the speedometer (the Honza about 64), and stops in about the Delivery Truck's distance without locking or nosing over. Corners at 40 km/h and a kerb at speed don't tip it. Does it now feel like a loaded step van? Too slow anywhere (hills, merging)? |

Log lines: none new. In the game's console (`-console`), `GetEnginePower` in the truck reads 40, `GetBrakeForce` 2500 and `GetMaxSpeed` 50.

# Storage per piece (0.9.7)

Each fitted piece of equipment now holds its own food: 40 units a piece on a cart, 80 on a truck (a coffee counts a quarter, a pastry half). A piece that serves two foods (the grill: burgers and kabobs; the concessions case: popcorn and cotton candy) shares its room between them. The new **Extra Storage** cabinet takes one equipment place, serves nothing and doubles every piece (cart 80, truck 160); a second one is refused. Food with no fitted piece to hold it doesn't load. The commissary still sells whole boxes; Restock fills each piece from the commissary's stock and leaves the rest there. Also in this build: the bay's "Order stock for delivery to …" starts the call properly (it showed the commissary's first line with no buttons). Build `0.9.7` on branch `milestone-8`, with the first review round's fixes (rows 14 to 18); the log says 0.9.7. Fresh **FT** save, other gameplay mods off.

| # | Do | Look for |
|---|---|---|
| 1 | Ctrl+J cart. Carry a box of hot dogs to it before fitting anything | Refused: "Nothing on this cart holds Hotdog. Fit a Hotdog Grill first." The box stays in your hands |
| 2 | Fit a hot dog grill, then load the box of 500 | 40 hot dogs go in, the rest stays in your hands, with "The Hotdog Grill is full: it holds 40 units …". The cart's panel: the grill's slot reads 40/40; hover it for "Hotdog Grill: 40/40 units" and "Hotdog: 40". Does the slot and its tooltip look like the game's own? |
| 3 | Ctrl+U by the cart (Extra Storage comes first), then load hot dogs again | Extra Storage goes in and shows on the counter as a steel cabinet. Hot dogs top up to 80; the grill's slot reads 80/80. Hover the Extra Storage slot: "Extra Storage: doubles every piece's storage (80 units a piece)". How does the cabinet look on the cart? |
| 4 | Carry a second Extra Storage to a truck that has one (Ctrl+K, Ctrl+U twice: the first press fills all four slots) | "One Extra Storage is enough: a second adds nothing …", even though the slots are full too; the second stays out. On a cart with a grill and a cabinet, the same message |
| 5 | Truck with a grill: load burgers, then kabobs | Both share the grill's 80 (160 with Extra Storage); the tooltip lists both foods |
| 6 | At a commissary bay, order hot dogs, a griddle and Extra Storage; read the order form | Foods read "Hotdog, box of 500 (… here)"; the griddle and Extra Storage read "Griddle (0 here)", "Extra Storage (0 here)" at their price per piece, no "box of". The title counts "boxes and pieces". The read-back says "Griddle, $2,000.00", not "1 box of Griddle" |
| 7 | Deliver (Ctrl+Alt+F3), then Restock a truck with a hot dog grill, a griddle in stock and tacos and chopped cheese in stock | Restock fits the griddle first, then fills the grill with hot dogs and the griddle half tacos, half chopped cheese. The message starts "Loaded into the truck: fitted Griddle; 80 Hotdog, …". Foods with no piece on board stay: "… Nothing on it holds …, so that stays here." Order a second Extra Storage for a truck that has one and Restock again: no "One Extra Storage is enough" message; the cabinet stays in the commissary's stock |
| 8 | Sell the truck's tacos down, take the griddle out (carry it off), then Restock again | The leftover tacos stay in the truck and aren't on the menu; Restock doesn't load more tacos and says nothing holds them. Hover the tacos' slot: "No equipment on board holds Tacos …" |
| 9 | A save from 0.9.6 with a cart holding 80 hot dogs | The 80 stay; the grill's slot reads 80/40 and its tooltip says nothing more loads until some is sold. Nothing is deleted or refunded |
| 10 | Truck in a commissary bay: "Commissary", then "Order stock for delivery to … (opens its texts)". Try it on foot by the truck and from the driver's seat. Then try it once and close the phone straight away | The phone opens on the conversation, the commissary's line appears with Hang up and Order stock under it, and the call goes on as from the CALL button. Hang up works. Closing the phone early shows no "Couldn't open the texts" warning |
| 11 | In the order form, open the item list (add button), then press the phone's own CLOSE [ESCAPE] button; then press Esc. Do the same in Square Appliances' furniture order form | Tell me whether the phone's CLOSE button does nothing in both while the list is open (the game's own behaviour, left alone) or only in ours. Esc should close the list first in both |
| 12 | BizMan, the fleet section | Each vehicle's card lists its pieces, e.g. "Hotdog Grill 62/80 units", and "Extra Storage: Doubles storage" |
| 13 | Play a cart with one piece at a busy spot for a couple of hours | Does selling out in about 3 to 4 hours (without Extra Storage) feel too fast? |
| 14 | Ctrl+Alt+J hand truck. Put a hot dog grill on it, then a box of hot dogs; Ctrl+J a new cart and unload the hand truck into it | The grill goes in first, then 40 hot dogs; no "Fit a Hotdog Grill first" warning. The rest of the box stays on the hand truck |
| 15 | On the cart's item panel, press sell on the grill's slot (it reads "40/40") | The question reads "Sell Hotdog Grill for $…?", without "(40/40)". Cancel: the slot still reads 40/40 |
| 16 | A cart with a coffee machine: load coffee one cup at a time near the top (or a box, then sell one cup) | At 159 cups the slot reads 39/40, not 40/40; at 160 it reads 40/40 |
| 17 | BizMan, the fleet section, with a piece full | The piece's row has no red text |
| 18 | A truck with a catering permit and three fast-food pieces (hot dog grill, grill, griddle) and a cooler, as the only caterer, in a save whose vehicles have sold a few hundred items (offers come at most once every two days or so, and fewer before 500 items sold). Sleep to the next day several times and read the Street Food Bookings offers (Ctrl+Alt+V books a fixed 40-guest test gig and skips the offers, so it checks nothing here) | No offer asks for cold drinks: cold drinks are only wanted at events of 100 guests and up, and the cooler holds 80. Fast-food offers go up to 200 guests. The log's `Catering offer N: …` lines show the same |

Log lines:

- At startup: `Patch CargoItemUi.SetUp(CargoItem, ICargoHolder, Boolean): Active.`, `Patch CargoItemUi.OnSellClick(CargoItem, CargoInstance): Active.`, `Patch ICargoHolder.TryToMergeAndMoveCargoBetweenHolders(ICargoHolder, ICargoHolder): Active.` and `Item foodtrucks:itemname_storagecabinet: registered (machine, price 900, …)`.
- Rows 1 to 5: `Cargo limit: <item> into <vehicle type> <id> capped at N (no fitted <piece> holds it | <piece> holds 40 units | 4 equipment slots | one Extra Storage counts).`
- Row 7: `Commissary restock: truck <id> at <site> loaded …; [no piece for …;] K units refused by the cargo and kept; L units left in stock; storage hotdoggrill 80/80; griddle 80/80.` "refused by the cargo" should be 0: Restock only asks for what fits.
- Row 10: `Commissary <id>: the bay's Order stock opens its conversation.` then `Commissary <id>: the bay's call started once the conversation was up (N frames).` A `the bay's call didn't start` error means the conversation never showed with Contacts open. Closing the phone early logs Info: `the bay's call was dropped: Contacts isn't showing …`.
- Ctrl+L: each vehicle's stock now ends with its pieces, e.g. `stock 62 (hotdoggrill 62/80; storage x2)`.
- Catering offers: each offer's guests are capped by its own category: the most food items one truck holds of it (80 a piece on a truck, 160 with Extra Storage, a coffee machine 320 cups, a concessions case 320 popcorn and cotton candy).

# Hatch side and debug keys (0.9.6)

Build `0.9.6`: 0.9.5 plus two playtest fixes. "Open here" on a truck now checks that the hatch faces the pavement: it reads the traffic system's driving lanes beside the truck, so a hatch to the road is refused even though the NavMesh lets people walk on the street. A crew's route parks a truck that stands the wrong way at a stop again, turned round. The debug keys moved off F4 to F10 and F12 (Camera Tools' Hide UI, the game's `-console` keys, quick save and menu) onto letters: **Ctrl+K** truck, **Ctrl+Alt+K** licence and permits, **Ctrl+J** cart, **Ctrl+Alt+J** hand truck, **Ctrl+U** new items, **Ctrl+Alt+U** hire, **Ctrl+L** status, **Ctrl+Alt+L** demand here, **Ctrl+G** grip, **Ctrl+Alt+G** zone point, **Ctrl+V** city event, **Ctrl+Alt+V** catering gig, **Ctrl+X** tax statement, **Ctrl+Alt+X** tax fixture (these four only on a second press within 10 seconds), **Ctrl+F3** / **Ctrl+Alt+F3** / **Ctrl+Shift+F3** commissary bays, deliveries and overnight check, **Ctrl+Shift+F11** twice for the uninstall step. Fresh **FT** save, other gameplay mods off; Camera Tools may stay on for rows 8 and 9.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh` builds; installed after the mod was parked for any recording.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 20 minutes)

| # | Do | Look for |
|---|---|---|
| 1 | Ctrl+K truck, Ctrl+Alt+K, fit a grill, load hot dogs. Park in a parking space at the kerb with the hatch (right side) to the pavement, close to the kerb. "Open here" | It opens as before; the line forms on the pavement by the hatch |
| 2 | Same street, park with the hatch to the pavement but well away from the kerb (as far out as the space lets you), or at a slant. "Open here" | It still opens. If it's refused, tell me how far from the kerb the truck stood |
| 3 | Drive off, turn round and park in a space with the hatch to the road (pavement on the driver's side). "Open here" | Refused: "The hatch faces the road. Park with the serving side to the pavement." You stay in the driver's seat; nothing opens |
| 4 | Try row 3 on a one-way street with parking on the left, and on a wide avenue (wide lanes, a wide parking strip) | Same refusal with the hatch to the road; opens with the hatch to the pavement. On the avenue, also park as far from the kerb as the space allows with the hatch to the pavement: it should still open |
| 5 | If you find parking in the middle of a street (a median), park there and "Open here" | Either "There's only road by the hatch…" or it opens. Tell me which, and whether the line ends up on the road |
| 6 | Open properly (row 1) and watch the people walking up, moving up and leaving | They come and go along the pavement, not down the street, and stand on the hatch side. Nobody walks across the street to or from the line |
| 7 | Put a worker on the truck (Ctrl+Alt+U), add a stop where it stands (hatch to the pavement), with hours starting soon. Then park it at the same stop with the hatch to the road, walk more than 15 m away and wait an in-game hour | The crew moves the truck and opens at a space nearby with the hatch to the pavement; no "The crew can't open" message |
| 7b | After row 7 (the crew parked it), get in, turn it round in the same space so the hatch faces the road, get out, walk away and wait an hour. Again with the worker taken off the truck before you get in and put back after | The crew moves it again, as in row 7, rather than trying to open there |
| 8 | Press each debug key from the list above once, Camera Tools on, the game started without `-console` | Each does its action, the UI stays visible, no quick save and no menu pops up. Ctrl+V, Ctrl+Alt+V, Ctrl+X and Ctrl+Alt+X only show "Press … again within 10 seconds" the first time; a second press runs them |
| 9 | Ctrl+Shift+F11 once in a save without Food Trucks vehicles | The "Press again within 10 seconds" question; nothing else |

## 3. Log lines (me)

- `Debug actions ready: Ctrl+K food truck, Ctrl+Alt+K licence and permits, …, Ctrl+V twice city event here, …, Ctrl+Shift+F11 uninstall step.`
- One `Pitch: truck <id> at (…), heading …: parking Legal; hatch <verdict>: hatch side pavement … m, road … m; other side pavement … m, road … m; N lane segments near; first pavement +0.xx m on the hatch side, … on the other; may open|refused.` per "Open here". Row 1: `Pavement` with the hatch side's pavement at about 1.5 to 3 m and no road before it. Row 3: `WrongWay`, road at 1.5 to 3.5 m on the hatch side and no pavement before it. If any row reads `traffic system not running` or `no traffic lanes within 25 m`, the check fell back to the NavMesh alone: tell me where.
- The first-pavement heights say whether the game's kerbs are raised (a positive few centimetres) or flat; for a later, second signal.
- `Route: truck <id> at … stands with its hatch to the road for stop N (…); the crew parks it again …` once in row 7, then `Route: the space at … would put the hatch to the road (…); the truck takes it the other way round.` if the space it picks needs turning. A space refused both ways round logs `… has road by the hatch either way round …; trying the next lane.`
- Row 7b: `Route: the player took truck <id>; where the crew parked it no longer counts as at its stop.` when you get in.
- `Queue view: line at cart <id>: … places on the right side …, N ways in and out` for the truck, as before; `(K crossing a driving lane left out)` after it when some ways were dropped. `Queue view: cart <id>: a customer's walk would cross a driving lane; taken out of view.` should be rare; tell me if it repeats at one place.
- No new Harmony patches for the hatch check (the commissary's six are listed in its section below).
# Commissary orders by text (0.9.6)

Ordering now stays in the commissary's conversation on the phone. "Order stock" shows the game's own phone order form, the one Square Appliances shows in its conversation, filled with the commissary's foods and machines: the address is the commissary, the time is tomorrow at 08:00, amounts are boxes (at most 10 an order), prices are box prices. "Order" makes the commissary read the order back with its total; "Confirm order" books and pays it. The bay's "Order stock for delivery to …" opens the same conversation. The Hell's Kitchen commissary moved from First Street 24 (a retail unit, with a random shop in it each game) to the apartment building at First Street 22, by the wholesaler. Use a disposable save with a vehicle that has a commissary space.

| # | Do | Look for |
|---|---|---|
| 1 | Call a commissary where a vehicle has a space; press "Order stock" | The phone stays open. The conversation shows the order form: delivery address = the commissary's name, delivery time = tomorrow 08:00, both fixed. Delivery fee $0.00 and total $0.00. Any label on the form that still reads like a furniture store? Name it |
| 2 | Press the form's add button | The item list beside the phone: foods first, then machines, each as "Hot dog, box of … (0 here)" with the box price. Its title reads "<Commissary>: 0/10 boxes". Search works |
| 3 | Add hot dogs, raise them to 3; add a griddle | Each row shows its box price; the amount counts boxes. The fee is 10% of the goods, the total goods + fee. You can't go past 10 boxes in all (the list title counts them) |
| 4 | Press "Order" | The commissary answers: "So that's: 3 boxes of Hot dog (… items, $… a box), 1 box of Griddle …, $… plus $… delivery, $… in all, here at 08:00 tomorrow." Buttons: Hang up, Change, Confirm order. No money has moved |
| 5 | Press "Change" | A fresh order form. Order 1 box of something and press "Order" again |
| 6 | Press "Confirm order" | The money goes (a transaction "Commissary order from …" per item), the commissary's answer lists one "Ordered: …" line per item. "Order more" opens the form again. Hang up; reopen the conversation: the order and the commissary's texts are in its history |
| 7 | Press "Order" on an empty form | A notice "Add at least one box to the order"; the form stays |
| 8 | Drive a vehicle into a commissary bay, press "Commissary", then "Order stock for delivery to … (opens its texts)" | The phone opens on that commissary's conversation and the call starts; same flow as above |
| 9 | Spend down to less than an order's total, then order and confirm | The answer says the payment didn't go through and the order is held; it is paid and delivered later, as before |
| 10 | Call a furniture store (Square Appliances or another) and open its order form | It is the game's usual form: your buildings, its delivery times, its prices, $250 delivery. Nothing of the commissary's |
| 11 | City map, Street food line, "Commissaries"; drive to Hell's Kitchen's | It lights First Street 22, next to the wholesaler at First Street 18. Is there a kerb by its door where the truck parks legally? Ctrl+F3 at the kerb should say "in the bay" |
| 12 | Load a save that rented a space at the Hell's Kitchen commissary on 0.9.5 | The space is still there (state line in the log), the rent keeps coming, and the vehicle in the new bay isn't fined at 04:00. A vehicle still standing at First Street 24 (the old bay) is fined once at 04:00; move it to First Street 22. That is expected, since 0.9.5 never shipped |
| 13 | Open the order form, press its add button so the item list shows, add an item, then press "Order" | The item list beside the phone closes when the commissary reads the order back. Again with the list open: hang up; and again: close the phone with its button. Each time the list closes and nothing errors when you click where it was |

Log lines:

- At startup: `Patch DeliveryContractSettingsBase.Start: Active.`, the same for `SetUpInventoryList`, `AddItemToList(String)`, `UpdateTotalPrice`, `FurnitureDeliveryContractSettings.get_MaxContractAmount` and `UpdateItemsListTitle`, then `Commissary orders: the order form in the commissary's conversation is on.`
- At load: `Commissary hells-kitchen (ba:street_firststreet#22): bay at x …, z … (nearest entrance).` A line `Commissary <id> (<address>): the building has a business in this game (…)` only where a rival's factory landed in a garage.
- Rows 1 to 6: `Commissary order at <id>: the order form lists N items.`, `Commissary order at <id>: read back N boxes (…), $… plus $… delivery.`, one `Commissary order N: B boxes, U <item> at <id> for $… plus $… delivery, paid; due day D at 08:00.` per item.
- Row 8: `Commissary <id>: the bay's Order stock opens its conversation.`
- No `Commissary order form, …` errors.
- Row 13, phone closed: `Commissary order at <id>: the call ended with the order form open; its item list is closed.`

# Commissary checks (0.9.4)

Build `0.9.4`: 0.9.3 plus the commissary and the new items. Five commissary garages (`data/commissaries.json`): rent a space per vehicle at the bay, order stock by phone, delivery at 08:00 the next day, Restock at the bay, daily rent, and a $250 fine from the City for a food vehicle out of its bay at 04:00. The bay positions are guesses from the map outlines and are the main thing to check. Fresh **FT** save, other gameplay mods off. Debug keys (as of 0.9.6): **Ctrl+F3** where every bay is from you, **Ctrl+Alt+F3** deliver paid orders now, **Ctrl+Shift+F3** what the 04:00 check would say now (no fines). Console: `foodtrucks_commissary_where`, `foodtrucks_commissary_deliver`, `foodtrucks_commissary_night`.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; installed after the mod was parked for any recording.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 20 minutes)

| # | Do | Look for |
|---|---|---|
| 1 | Load a save from an earlier build that has a food vehicle, after 04:00 | A City of New York text about the new overnight rule, and no fine for the night that passed before this build |
| 2 | City map, Street food line, "Commissaries" | Five buildings lit in purple: Industry City, Garment District, Hell's Kitchen's edge, Lower Manhattan, Murray Hill. Do they look like garages a food vendor would use? Name any that don't |
| 3 | Ctrl+K truck, Ctrl+Alt+K, drive to the nearest lit commissary and park at the kerb by its door. Ctrl+F3 | A new contact ("… Commissary") with a welcome text. The notice says "in the bay". If not, the distance tells me how far off the bay is: Ctrl+Alt+G where a vehicle could legally park, and I put that point in the data |
| 4 | In the truck at the bay, press "Commissary" on the panel | The button shows only in the bay. The list offers "Rent a space at …: $1,250.00 a month". Rent it: a message, and a rent transaction of about $41 today |
| 5 | Order 3 boxes of hot dogs and a hot dog grill from the commissary | Superseded in 0.9.6: ordering is in the commissary's conversation; follow rows 1 to 9 of "Commissary orders by text (0.9.6)" above. The list-picker ordering these rows used to check is gone |
| 6 | Ctrl+Alt+F3 (or sleep to after 08:00) | A text per order: "Your delivery is in" |
| 7 | At the bay, "Commissary", "Restock" | The grill and the food go into the truck's cargo (the item panel counts them). A box bigger than the stock limit loads in part and the rest stays ("… items left in stock here") |
| 8 | Leave the truck in the bay, push a cart elsewhere and walk away; sleep past 04:00 | The truck isn't fined; the cart is: a City text "Your Food cart wasn't in a commissary bay at 04:00", and a fine transaction of $250 |
| 8b | If the save rents or owns a building: leave a cart at its door (within 15 m), and a truck inside a warehouse you rent, if you have one; sleep past 04:00 | Neither is fined (Ctrl+Shift+F3 before 04:00 says "at your own building") |
| 9 | Next day, Econoview's income statement | Under the truck: "Commissary rent"; under the cart: "Overnight fines"; in the Street food group: "Delivery fees". The statement's Total still equals the day summary's profit |
| 10 | Rent a space for the cart at a second commissary, then move the truck's space there from the panel at that bay; give one up | Messages for each; the rent keeps coming daily for the spaces left |
| 11 | With stock left at a commissary, run the uninstall step (Ctrl+Shift+F11 twice) | "Commissary stock and open orders paid back: $…", the commissary contacts are gone. Drive past a commissary afterwards: no contact or welcome text comes back. Econoview's tax page: the commissary deduction is lower by the refund |
| 11b | In a save with no Food Trucks vehicles, press Ctrl+Shift+F11 once | A question first ("… Press again within 10 seconds"); nothing happens until the second press |
| 12 | If you have a business: buy a new machine (griddle, say) at a commissary, restock it into the cart, push the cart into your business and try to place the machine | No "Place" button for it: the machines are street food only. It stays in the cart's cargo. Tell me if the game places it anyway |
| 13 | Give a truck a route whose last stop runs past 04:00, with little stock, and sleep through | If it sells out and closes before 04:00, it is fined like any vehicle out; if still open at 04:00, it isn't |

## 3. Log lines (me)

- `Commissaries loaded: 5 garages; rent cart $300, truck $1250 a month, overnight fine $250, bay radius 15 m.`
- `Commissary: 5 of 5 garages found in the city.` and one `Commissary <id> (<address>): bay at x …, z … (nearest entrance).` per garage; the state line after it.
- `Map filters: Street food line with 8 filters.`; `Cart buttons added to the item panel.`
- `Overnight check: starts on day …`, then one `Overnight check through day N: <vehicle> in its bay / out …; K fines.` per night and an `Overnight fine: …` line per fine.
- `Commissary <id>: unlocked on day … (visited)`; `… rented a space …`, `moved its space …`, `gave up its space …`.
- `Commissary rent: <vehicle> at <id> for day N, $… paid.` once per space per day.
- `Commissary order N: …` (the ordering lines are now those of the 0.9.6 section), `Commissary delivery: order N, …`, `Commissary restock: … loaded …; … units didn't fit and stay; … left in stock.`
- `Uninstall: commissary refund …` after row 11. Also `Uninstall: $… of this tax year's commissary deduction taken back for the refund.` and `Uninstall: N market entries and M market events for the mod's foods removed.` (both 0 normally).
- No new Harmony patches; the earlier `Patch …: Active.` lines are unchanged. No `Commissary:` error lines.

# Playtest fixes and truck equipment (0.9.3)

Build `0.9.3`. The fixes from both 0.9.0 playtest sessions: the Food Safety course at the business school, the queue in arrival order with the money at the end of the wait, the street food breakdown in Econoview, crew serving a window the player opened, the cart's final model with its equipment on the counter and parked cars kept off it, the player inside the truck at the hatch, and the Street food fleet page in BizMan. New in 0.9.3: popcorn, cotton candy, slushies and kabobs (the game's own items, on its concessions case, slushi machine and industrial grill; the grill and fryer are now separate pieces), fitted equipment on show in the truck, and a taller truck: the roof is 0.20 m higher and the serving window sits 0.60 m higher, so the player and crew stand on the truck's floor with their faces in the window. Also new: the mod's own items, dumplings, tacos, chopped cheese and grilled corn and the dumpling steamer, griddle and corn grill (rows 46 to 50); nothing sells or buys them yet, so **Ctrl+U** puts a set into the food vehicle next to you. Fresh **FT** save, other gameplay mods off. The Milestone 7 list below still applies.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; installed after the mod was parked for any recording.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 45 minutes)

| # | Do | Look for |
|---|---|---|
| 1 | Go to the business school, use the Market Demands door | A question: which course? Buttons "Food Safety course" and "Market Demands". Does it read well? |
| 2 | Press Escape; use the door again and open the phone; use it again and press "Market Demands" | Escape and the phone close the question with nothing else. "Market Demands" opens its usual study panel; cancel it |
| 3 | Use the door again, press "Food Safety course" | The study panel says "Food Safety course" and $25 an hour, with up to 6 hours on the slider. Cancel it: no money gone |
| 4 | Phone, your profile's education page | "Food Safety course" listed with the others, 6 hours to go |
| 5 | Study it through (Start, then skip time) and use the door once more | Tuition paid, the course completed. The door goes straight to Market Demands without asking |
| 6 | Phone the City of New York, buy the licence | The licence is sold. (Before row 5, the refusal names the business school's Market Demands classroom) |
| 7 | Open a cart at a busy spot and watch the line form | People walk up and take their place in the order they arrive. Nobody stands at the back while someone ahead of them is still walking up, and nobody is served before reaching the window |
| 8 | Watch a few sales | Each customer waits at the window for the order. The money and the sale message come as they walk away, not as they step up |
| 9 | After a day with sales, open Econoview's income statement | "Street food" is a group. It opens into one row per vehicle (and "Vendor licence" or "Event places" if you paid for them); a vehicle opens into Sales, Stock sold, Crew wages and so on |
| 10 | Same day, with a hired crew or paid parking | "Unassigned staff wages" and "Parking fees" are lower by the vehicles' share, which shows under each vehicle instead. The statement's Total equals the day summary's profit for that day |
| 11 | Put a worker on the truck with no route (Ctrl+Alt+U hires one), open it yourself and walk away | The open message says "<name> serves at the window". The worker stands behind the hatch and customers are served; no "Nobody's at the truck's window" |
| 12 | Leave it open over a full hour and past midnight, then check the next morning's transactions and Econoview | An unassigned wage for the worker covering the hours the window was open (one hour for each hour the window was open at some minute before half past it; none for an hour it sold out or closed before). The 23:00 hour is in the closing day's wage, not the next day's. In Econoview it is under the truck's Crew wages |
| 13 | Give the worker a route, open the truck yourself during a stop's hours and keep it open past the stop's end | The route leaves the truck where you opened it; the worker keeps serving. The day's wage counts each hour once, not twice for the overlap |
| 14 | Open the truck with the worker off sick, or with no crew | "Nobody's at the truck's window" once you step out of the hatch (since 0.9.8; before, once you walked away) |
| 15 | Put three workers on the truck (staff 3), one weaker than you at customer service, with no route (or outside its hours); open it and stand at the window from before an hour until after half past it (say 9:50 to 10:40), then step out | While you stand there the weakest worker waits inside the truck and the others are at the hatch beside you; after you step out all three are at the hatch. The log has no `worked 10:00` line for the weakest worker, and one for each of the other two |
| 16 | Sleep through the night with the truck open and little stock | The wage the next morning stops at the hour the truck sold out, not at the hour you woke. Sleeping past midnight, the hours before midnight are in the closing day's wage |
| 17 | With a worker serving a truck you opened (no route) since before 12:00, at 12:10 assign them to one of your shops (and, in another try, a warehouse as a delivery driver; or set them to be replaced by a headhunter; or fire them) | At once an unassigned wage for that worker in the transactions, covering today's hours on the truck including 12:00 (log: `Crew: <name> paid $… for N hours on leaving the crew's pay …`). Next day's Econoview: that wage under the truck's Crew wages. No second payment for the same hours at midnight |
| 18 | Serve with a worker from 12:00, run the uninstall step (Ctrl+Shift+F11 twice) at about 12:20, keep playing to the next morning | The worker's wage includes 12:00 (log: `worked 12:00` before the uninstall lines) |
| 19 | Look at a cart standing on the street, from the side and from above | The new cart: grey box, red and orange umbrella, two spoked wheels. The texture is on (not plain white or pink). Both wheels touch the ground, the feet at the front too |
| 20 | Grab the cart and push it around, forwards and back | Both hands on the push bar, not inside it or in the air. The wheels turn the right way as it rolls and stop when you stop. Hover and click on the cart still work; walking into it, you stop at the wheels |
| 21 | Load stock into the cart (food and an industrial coffee machine) and look at the counter | No cardboard boxes anywhere on or around the cart; the item panel still counts the stock. The coffee machine stands on the front spot behind the umbrella pole, on the counter (not sunk in or floating), facing the cart's right side |
| 22 | Fit a drinks fridge as the second piece | The drinks cooler stands on the rear spot, in front of the push bar. Grab the cart: neither piece touches your hands or the bar; walk around it: nothing blocks you that didn't before |
| 23 | Open the cart where the line forms on its left, then on its right (turn the cart round) | Once the line appears, both pieces turn to face it. Close: they turn back to the right. The steam-well lid stays shut throughout |
| 24 | Fit a hot dog grill, then in turn a burger station, ice cream counter, pizza oven and display case | Each shows its compact model on the spot, close to the game's own item in look. Do they sit right and read from the street? |
| 25 | Take the front piece out of the cargo, then save and load | The piece goes from the counter at once; the rear piece stays where it is. After loading, the fitted pieces are back on the counter, filling the spots in cargo order (a lone rear piece moves to the front then). If you can, leave the equipped cart on one floor of a multi-floor building and go to another floor: the pieces hide with the cart, none float |
| 26 | Leave a cart in a parking lane by the road, walk more than 80 m away and wait for the next hour, or sleep a night; come back | No parked car stands on or in the cart. Nobody's car parks into it with auto-park either |
| 27 | Ctrl+K truck, Ctrl+Alt+K, fit a grill, load hot dogs. Park legally with the hatch to the pavement, "Open here" | You stand inside at the hatch, facing the pavement, not outside. A panel "At the serving hatch" with Step out [Esc], Close the window and Drive (no key shown on the last two; F and Enter do nothing). Can you see yourself through the open hatch, or does the roof hide you? Screenshot |
| 28 | Press WASD, click the pavement, scroll the camera | You don't move or slide through the truck. The camera turns as usual |
| 29 | Wait for a few customers | They are served (sales come in) with you inside; the "Nobody's at the window" message doesn't show |
| 30 | Press Escape | You step out behind the truck, on the pavement or the parking lane, not inside the body. The window stays open. Since 0.9.8 it stops serving as soon as you step out, however close you stay ("Nobody's at the truck's window"); before, it served while you were within 10 m |
| 31 | "Open here" again (drive a metre and park), then Drive in the panel | You step out by the driver's door and get in; the window closes as when driving off |
| 32 | Open again, then Close the window | The usual closing message; you step out |
| 33 | Open again, save from the menu while at the hatch, load that save | Saving works. After loading you stand outside behind the truck, the window still open; since 0.9.8 it no longer serves until you are back at the hatch (drive and open again), unless a crew serves. Accepted: while you stand at the hatch the game counts the save as changed (its saved position is the step-out point), so its recovery autosave runs at every interval and quitting may ask about unsaved changes |
| 34 | With a hire on shift at the truck (Ctrl+Alt+U, put them on the truck with a stop here), open | The hire stands beside you at the hatch, not in your place, and inside the hatch's opening. With three hires, two stand either side of you and the third waits further back in the truck. Step out: they spread across the opening |
| 35 | Open, then sleep or skip time from the phone, if anything lets you | After the skip you stand outside by the truck |
| 36 | Drive until your energy runs out (or run it down first), then park and "Open here" | The window opens, but you stay outside with "You're too tired to work the hatch." At the hatch, if energy runs out you step out with the same message, and the game's usual exhaustion follows |
| 37 | With a cart or truck that sold something today, open BizMan | The business list ends with "Street food", "Food carts and trucks", the week's average profit and "N of M open". No cart or truck owned: no such line. Its location button is hidden and satisfaction reads "-" |
| 38 | Try BizMan's filters: a neighbourhood, a business type, "Businesses", "Real estate", and a search for "street" | Neighbourhood, building and business type filters and "Real estate" hide the line; "Businesses" and the search keep it. The list keeps working |
| 39 | Click "Street food" | A "Street food" section opens below the warehouses and the list scrolls to it: a card per vehicle, built like a warehouse card. Does it look right, or does anything of the warehouse card show (its buttons, column headings saying "Driver" or "Days left")? |
| 40 | Read a card | Name; "Open at …" or "Closed, at …" (or on the way to a stop, or with the private driver); Today and Last 7 days with sales and profit; Health; Crew; Route; each item with "N sold" today and "N left". Do the sales match Econoview's? With the vehicle out on the street, the location button sets the GPS to where it stands, with a vehicle marker on the map; with it in a garage, to the garage's address |
| 41 | Open a business from the list, go back, close and reopen BizMan | The section is closed again; your own businesses and warehouses open and look as before |
| 42 | *Superseded in 0.9.8 by "Truck interior (0.9.8)" row 5: the ledge is gone and all four pieces stand on the back counter.* Fit four pieces in the truck (say a grill, a coffee machine, a cooler and a display case), park and look in through the hatch from the pavement, open and closed | The first two stand at the hatch's two ends on a steel ledge, smaller than on the cart, facing the street; the other two on the back counter behind the crew. Nothing pokes through the walls, the closed hatch or the window panes. Do they read from the street? Is the ledge's grey close enough to the truck's steel? |
| 43 | Open at the hatch, with a hire on shift; look from the pavement, then from the camera at the hatch | From the pavement: your and the hire's heads and shoulders fill the window, nobody stands inside the griddle, the worktop or an equipment piece. The taller truck looks right from the street (window at about eye height, the step from the cab to the higher box roof). At the hatch, the camera doesn't clip through the roof |
| 44 | Buy a slushi machine and a concessions display case at Red Carpet Decor (6 Broadway Street), slushies, popcorn and cotton candy at a wholesaler; fit both on a cart in Hell's Kitchen or Midtown on a summer afternoon near a park or theatre, open | The cart sells all three; the counter shows the popcorn machine and the slushie machine. Do the prices and the pace feel right for snacks? |
| 45 | Fit an industrial grill on a cart, load burgers and kabobs; then fit only a fryer with fries | With the grill, burgers and kabobs both sell; with the fryer alone, only fries. A cart that had the old burger station (grill and fryer) keeps selling all three |
| 46 | Spawn a truck (Ctrl+K), stand next to it and press **Ctrl+U** | A message lists what went in: all three new machines (dumpling steamer, griddle, corn grill), then the foods until each machine's room is full (80 units a piece, 160 with the Extra Storage that Ctrl+U fits first; dumplings count half a unit). Open the truck's item panel: each new item has its own picture, not a cardboard box, and its name. Do the pictures look like the game's own item icons (angle, light, size)? Screenshot |
| 47 | Look into the truck through the hatch | Three of the new machines on show (hatch ends and back counter), each its compact model. Do they sit right? |
| 48 | Ctrl+Alt+K, park legally, open at a weekday lunch spot in Midtown or Lower Manhattan; watch half an hour | Dumplings, tacos and chopped cheese sell, at prices like the hot dog's. Do the pace and prices feel right? |
| 49 | Spawn a cart (Ctrl+J), push it, press Ctrl+U; look at the counter; open at a park on a summer afternoon if you can | Since 0.9.7 Extra Storage and the dumpling steamer fit (the griddle and corn grill are refused with the equipment-full warning), shown on the counter's spots facing the queue. Dumplings sell. Pick up a machine box from the cargo: is the box in your hands the game's usual cardboard box? |
| 50 | With new items in a cart's cargo, run the uninstall step (Ctrl+Shift+F11 twice) | The refund includes the new items at their prices (machines $2,000 / $2,000 / $1,200, foods at wholesale per unit) |

## 3. Log lines (me)

- `Patch EducationDoorController.StartLearning: Active.`; `Food Safety course: added at the Market Demands door, 360 minutes at $25 an hour.`
- No `Food Safety course:` error lines; no `Queue view:` NullReferenceException on quitting.
- `Queue view: line at cart …` and `… line removed …` lines, and the session total on unload.
- `Patch …LoadSalaryRows: Active.`, `Patch …LoadOngoingExpenseRows: Active.`, `…LoadFeeRows: Active.` and `Patch EmployeeInstance.PayWage(BuildingRegistration): Active.` (with the parking patches); `Econoview: crew wages and parking move from the game's rows to the vehicles.`
- `Sale: …` lines arrive as customers leave the window.
- `Crew: <name> worked HH:00 on day N (open window, <vehicle id>); counted for the game's midnight wage.` (`paid for HH:00 …` before the mod round), logged as the hour ends, once per hour the crew served a window you opened; none for route hours, none after a sell-out, and never two for the same hour and worker.
- `Patch EmployeeHelper.RunHourly: Active.` (a prefix and a postfix) and `Patch EmployeeHelper.PayDailyWages: Active.`; `Patch EmployeeHelper.UnassignEmployeeFromAllWorkshifts(EmployeeInstance): Active.`, `Patch EmployeeInstance.ResignBeforeReplacement: Active.`, `Patch EmployeeInstance.Resign: Active.`, `Patch EmployeeInstance.RemoveEmployee: Active.`, `Patch GameManager.RunMainGameTick(Single): Active.`
- `Patch VehicleHelper.DestroyBlockingVehicles(GameObject, VehicleType, Transform, Boolean): Active.`; at load, `Parking lanes: cart … seen; body collider 0.96 x 1.50 m …` for each cart (or `in hand now …` for one being pushed); after row 26, any `Parking lanes: cleared … parked … cars from cart …`.
- `Vehicle displays: cart … front spot, coffeemachine: shows CoffeeStation.` and `… rear spot, cooler: shows DrinksCooler.`, once each; after row 42, `Vehicle displays: truck … hatch rear spot, …`, `… hatch front spot, …`, `… back rear spot, …` and `… back front spot, …`. No `display … not found in the bundle` and no `lacks equipment spots` error.
- `Patch PlayerHelper.SaveCurrentPosition: Active.`
- One `Truck window: player placed at the hatch of truck …` line per open (or `… player left outside …: <reason>`), and `Truck window: player stepped out of truck … (<reason>)` for each way out; no `the NavMesh warp failed`. Row 33: `Truck window: saved at the hatch …`. If the game puts you back in the seat after Park: `Truck window: <id>: the player is back in the driver's seat; not placed at the hatch.` Row 36: `… player left outside …: out of energy` or `stepped out … (out of energy)`.
- No `Tried to set navigation blocker` warnings or NullReferenceExceptions around the rows above.
- `Patch BusinessScrollerController.PopulateAllModels: Active.`, `Patch BizManFilterToggle.PassesFilter: Active.`, `Patch BusinessCellView.SetData: Active.`, `Patch BizManList.OpenBusiness: Active.`, `Patch BizManList.OnDisable: Active.`; `BizMan: the Street food line and fleet section are on.`
- At startup, one line per new item: `Item foodtrucks:itemname_dumplings: registered (food, price 13.5, wholesale 5.1, box 80, icon yes).`, likewise tacos, choppedcheese, grilledcorn, and `… dumplingsteamer: registered (machine, price 2000, …)`, griddle, corngrill. No `Item …: not registered` line.
- `Item icons (mod load): 7 of 7 registered.`, and at each save load `Item icons (city load): 7 of 7 registered.` and `Item icons (city ready): 7 of 7 registered.`. No `ItemsGetter.RegisterModItem` warning, no `Prefab wasn't found` for griddle, corngrill or dumplingsteamer, no Addressables `InvalidKeyException` for `ItemIcons/…png`.
- After row 46: `Debug: New items into foodtrucks:vehicletype_foodtruck …: dumplingsteamer 1/1, griddle 1/1, corngrill 1/1, dumplings 80/80, …` and `Vehicle displays: truck … dumplingsteamer: shows DumplingSteamer.` (and griddle, corngrill). After row 49, `Cargo limit: foodtrucks:itemname_corngrill into … capped (2 equipment slots).`
- After row 39: `BizMan: fleet section copied from the warehouse section (card at …).` and the `BizMan: fleet section label …` / `fleet card label …` lines (they name the copied headings, to fix any that still say warehouse). No `BizMan Street food (…), line taken out` error.

# Milestone 7 checks: catering and Bakery

Build `0.9.0`. Catering gigs for trucks, offered by a new "Street Food Bookings" contact, and the Bakery mod's goods when that mod is installed. On top of 0.8.0 (its list is below). Fresh **FT** save, other gameplay mods off; the Bakery mod on for row 5 only. Debug: **Ctrl+Alt+V** books a gig at the nearest place from in 30 minutes.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; installed after the mod was parked for any recording.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 15 minutes)

| # | Do | Look for |
|---|---|---|
| 1 | Ctrl+K truck, Ctrl+Alt+K, fit a grill, load 50 hot dogs. Ctrl+Alt+V near an office | "Debug: catering gig at …". Is "Street Food Bookings" in the contacts? |
| 2 | Drive to the building named, park in front within the half hour | "Catering: the truck serves the guests until …". Open the window too: it sells only the hot dogs beyond the gig's 40 |
| 3 | Wait the hour by the truck (or sleep) | A text "Thanks for the catering … paid" and a catering transaction; 40 hot dogs fewer in the cargo |
| 4 | Play or sleep a few in-game days with the truck having sold some food | Offer texts from Street Food Bookings. Call: take one. Does the dialog read well? |
| 5 | With the Bakery mod on: fit its bread counter, load bread | The truck opens and sells bread |

## 3. Log lines (me)

- `Version 0.9.0 loading.`; `Catering: bookings by phone …`; `Bakery mod: found …` or `not installed`.
- `Catering: day …: N offers from …`; `Catering offer … taken`; `Catering gig … started`; `… completed … paid`; failures with their reason.
- `Map filters: Street food line with 7 filters.`

# Milestone 6 checks: depth (second half)

Build `0.8.0`. Premium spots, the city's events and partner deals with your own venues, on top of 0.7.0 (whose list is below and comes first if time is short). Fresh **FT** save, other gameplay mods off. Debug keys as before, plus **Ctrl+V**: a city event at the nearest event place from now for three hours, with one place booked.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; installed after the mod was parked for any recording.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 15 minutes, after the 0.7.0 list)

| # | Do | Look for |
|---|---|---|
| 1 | Open the city map, Street food line, "Premium spots and events" | About 17 buildings lit in gold, two or three per neighbourhood. Do any look like silly places for a food cart? Name them and I drop them |
| 2 | Push a cart to a lit building and open within about 40 m of its door | A second message names the spot ("Fifth Street office row: the cart has the spot …"). Open a second vehicle next to it: "your cart already has the spot" |
| 3 | Wait or sleep a few days | A text from the City of New York about a weekend market or street festival. Call the city: "Event places", book one. Does the dialog read well? |
| 4 | Ctrl+V near a lit event place, then open a cart within about 60 m | "The cart has its place at the …"; more customers than before. A second vehicle there is refused ("Your booked places at this event are taken by your other vehicles"); after an unbooked event (a real one you didn't book) the message is "The street is taken by a city event" |
| 5 | If the save owns a nightclub, theatre, cinema or office: park within 50 m, Hires | "Sign a street food partner deal with …". Sign it; open there in the evening |
| 6 | Sleep to the next morning after the partner sold an hour outside a nightclub, theatre or cinema | The venue's facility satisfaction a few points up in BizMan (5 points; the log line gives before and after) |

## 3. Log lines (me)

- `Version 0.8.0 loading.`; `Pitches loaded: 17 premium spots, 7 event places, 2 event kinds.`; `Pitches: 17 of 17 premium spots and 7 of 7 event places found in the city.` (any missing addresses are listed).
- `Patch BusinessHelper.UpdateSatisfaction(BuildingRegistration): Active.`
- `Premium spot …: held by …` / `taken by …`; open lines end with `premium spot …` and `partner …` where they apply.
- `City event …: market at … on day …`; `City of New York: place 1 of 2 booked at event …`; `Event …: place held by …`; `Open: … refused at the …`.
- `Partnership: … signed with …`, `worked day …`, `facility satisfaction … -> …`.
- `Cart … spot on day …` lines show live venues from your own clubs and theatres with their guest counts behind the weights.

# Milestone 6 checks: depth (first half)

Build `0.7.0`. Health grades, regulars, competition between your own vehicles, and live crowds from nearby venues. It includes Milestones 3 to 5, also unplayed; their lists are below and come first if time is short. Fresh **FT** save, other gameplay mods off.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; installed after the mod was parked for any recording.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 10 minutes, after the Milestone 5 list)

| # | Do | Look for |
|---|---|---|
| 1 | Open a cart or truck | The message names the grade on the window ("grade pending" on a new vehicle) |
| 2 | Hold the cart (or sit in the truck) and press Clean | The game's time-skip overlay runs 20 minutes with "Cleaning the window, the counter and the equipment", then "clean for today" |
| 3 | Open two of your vehicles with the same food within a block of each other | Each sells less of that food than alone (the open log line shows "neighbours" above 1 and "customers left by neighbours") |
| 4 | Open next to a theatre or cinema in the evening, or a nightclub as it closes | More customers from 22:30 for about 45 minutes (theatre), at 17:00, 19:30, 22:00 and 00:30 (cinema), or for 30 minutes after the club closes. A closed nightclub brings nobody |
| 5 | If you have time: sleep a few days with a cart open each day at the same spot, never cleaning | A health inspector's message with a grade below A, eventually; the next open message shows it |

## 3. Log lines (me)

- `Version 0.7.0 loading.`; `Reputation through day …: N vehicle days counted` after 05:00 each day with an open vehicle (the night belongs to the evening before).
- Open lines end with `Reputation: grade … x regulars … x neighbours … = …; pitch …`.
- `Cart … spot on day …, HH:00: …; N live venues, now nightlife x… ` hourly while open near venues.
- `Cleaning: … cleaned on day …`; `Health inspection: …` when the inspector comes; `stale … sold` after three days with the same stock.

# Milestone 5 checks: hires

Build `0.6.0`. The game's own employees can run a cart or truck: the "Hires" button on the vehicle's panel puts an unassigned employee on it and builds a route of stops with opening hours. The crew drives (or pushes) to each stop, opens, closes at the stop's time and moves on, and the game pays them at midnight. The player still restocks. 0.6.0 includes Milestone 4, which hasn't been played yet either: its list is below and can share the session.

Use a fresh save whose name starts with **FT**, never the series save. Other gameplay mods off. Debug keys: **Ctrl+K** free truck, **Ctrl+Alt+K** free licence and permits, **Ctrl+J** free cart, **Ctrl+Alt+U** hire a customer service worker (skill 60).

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds.
- [ ] Installed on homepc with the game closed, after the mod was parked for any recording and restored for the session.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 20 minutes, one launch)

| # | Do | Look for |
|---|---|---|
| 1 | Ctrl+K for a truck, Ctrl+Alt+K, load a grill and hot dogs. Ctrl+Alt+U twice | Two new employees in My Employees, both "Unassigned". Is there an "unassigned employee" task for each? |
| 2 | Sit in the truck, press Hires | The list shows both employees with skill, satisfaction and wage. Put one on the truck. Does their "unassigned" task go away? |
| 3 | Stop in a legal parking space at a lunch spot. Hires, "Add this spot to the route", open 11:00, close 14:00. Drive about 500 m to another legal space, add a second stop 15:00 to 18:00 | Do the hour lists read well? The messages say "Stop added" with the hours |
| 4 | Drive to a third place, step out and walk 30 m away. Wait (or sleep) until about 10:30 (at least an in-game hour after you last drove it: the crew leaves a vehicle the player has just used where it is) | Before 11:00 the truck disappears ("sets off … arriving about 11:00"), then appears in a parking space near stop 1 with its hatch to the pavement, and the crew opens it. Did it land in a sensible spot? |
| 5 | Walk to stop 1 while it's open | The worker stands inside behind the hatch, dressed as an employee; customers queue and pay. Does the worker look right (height in the window, facing out)? |
| 6 | Sleep through to the next morning | The truck sold through the afternoon at stop 2 (the summary message for each stop), then was off duty. Is there an unassigned wage for the employee in Econoview or the transactions, dated the day they worked? |
| 6b | Next day during stop 1's hours: drive the truck (it closes as you take it) to a wholesaler, step out and go in to buy boxes | The truck waits outside while you shop, then you load it. Walk away: within about an in-game hour of your last drive the crew takes it back to stop 1 and opens |
| 7 | If you have time: Ctrl+J for a cart, load it, put the second employee on it, add a stop where it stands with hours starting in a few minutes, then push it 30 m away and let go | The cart goes back to its stop (a few minutes' walk) and opens there with the worker standing behind it |
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

Not checked this time, on purpose: a late stop past midnight (Core tests cover it), three crew in one truck, crew satisfaction over weeks (the route's hours feed the game's job demands; watch for "no task" complaints later), the uninstall step with a crew (run Ctrl+Shift+F11 at the end if you like and I check the log).

# Still open: Milestone 4 checks (truck, licence and city systems)

Build `0.5.0`. The food truck is in, with the final model: buy it at General US Trucks, drive it, stop in a parking space and open from the driving panel. Selling now needs a vendor licence and a permit for each vehicle, both bought by phone from the City of New York (the contact that sends parking tickets). The licence needs the Food Safety course. The city map has a "Street food" filter line. You look at how it looks and handles; I read the rest from `Player.log`.

This build changes save data (licence, permits, vehicle types) and replaces the test truck, so use a fresh save. Don't reopen an older FT save that still has a Ctrl+F6 test truck (from 0.4.0 or earlier): that vehicle type is gone, and a save with an unknown vehicle breaks. A save from 0.4.0 or earlier gets no help: its cart needs a licence and a permit like any other.

### 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; the bundle has `FoodTruck.prefab` and the truck's textures.
- [ ] Installed on homepc with the game closed, after your recording is done and the mod was parked.
- [ ] After the session: the log lines in section 3.

### 2. In game (about 15 to 20 minutes, one launch)

A new save whose name starts with **FT**, never the series save. Other gameplay mods off. Debug keys: **Ctrl+K** free food truck, **Ctrl+Alt+K** free licence and permits (skips the course), **Ctrl+J** free food cart, **Ctrl+Alt+L** demand here.

| # | Do | Look for |
|---|---|---|
| 1 | Without the course: phone the City of New York and try to buy a licence | Does the city show up in the contacts with a call button? The call offers a licence, and refuses it without the Food Safety course. Do the texts read well? |
| 2 | Ctrl+K for a truck, then drive it a few blocks, park, reverse, take a corner fast | How does it look on the road (wheels on the ground, size, lights, mirrors) and how does it handle? Too floaty, too slow, tips over? |
| 3 | Ctrl+Alt+K, then load a grill and hot dogs into the truck (box in hand, click the truck). Stop in a parking space at a lunch spot and press Open here | You step out, the hatch on the right swings up, and people line up on the pavement beside the hatch, not in the road or behind the truck. Do they face the hatch? |
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

Not checked this time, on purpose: delivery of a truck to a warehouse (refused, as for the cart), the licence reminder texts (they need 50 in-game days; Core tests cover the days), the uninstall step with a truck (same code path as the cart; run Ctrl+Shift+F11 at the end if you like and I check the log).
