# Milestone 2 checks

Build `0.2.0`, demand and menu. This session is the first balance check. You play and judge; I read the rest from `Player.log` afterwards.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; the package lists `data/demand.json`, `data/economy.json` and `data/zones.json`.
- [ ] Installed on homepc with the game closed.
- [ ] After the session: the log lines in section 3, read from `Player.log`.

## 2. In game (about 15 minutes, one launch)

Use a new game saved under a name starting with **FT**. Turn off the other gameplay mods. Debug keys, holding Ctrl: **F7** free food cart, **F9** demand here.

| # | Do | Look for |
|---|---|---|
| 1 | Buy the cart at General US Trucks, or Ctrl+F7 for a free one. Grab it and push it around | Do the hands hold the bar? Does it turn naturally? |
| 2 | Fit a hot dog grill (carry it to the cart, click). Load a box of hot dogs the same way | The cart takes 80; the rest stays in your box |
| 3 | Put the box down. Try **Open here** right in front of a door, then next to a crosswalk, then on clear pavement | The two refusals make sense; the third opens |
| 4 | At lunch (11:00 to 14:30), at three different kinds of spot (offices, homes, nightlife or warehouses): Ctrl+F9, then sell for 20 to 30 in-game minutes | Screenshot each Ctrl+F9 readout. Do the sales feel right for the place? Too many, too few, wrong places busy, prices off? |

Write down anything that looks or feels wrong along the way. Your verdict in step 4 is what the tables get tuned on.

When done, quit the game and tell me; I copy the log and park the mod.

## 3. Log lines (me)

- Startup: `Version 0.2.0 loading.`, `Demand data loaded from ...`, both vehicle registrations, and `Active` for all 14 patches, including `VehicleInstance.MergeIntoCargo` and `VehicleInstance.TryToAddToCargo`.
- `Cargo limit: ...` when the box was capped; `Open: cart ... road layer below: yes|no` for each Open that reached the spot check (whether the game's road layer tells pavement from road); `Cart ... open on day ...` with the spot and plan; `Sale: ...` per item; `Demand here: ...` per Ctrl+F9.
- No exceptions from the mod.

Not checked this time, on purpose: the uninstall step and loading without the mod (save data and vehicle types haven't changed since Milestone 1), loading by hand truck, at shelves or with a stocked fridge, and carts from 0.1.0 saves. The reviews covered them; they get checked when save data or vehicles change, and once before release.
