# Milestone 2 checks, second build

Build `0.3.0`: the balance study's tuning, prices like a shop, subway entrances and park zones as pitches, the carts' profit in the day summary and Econoview, plus the fixes from the 0.2.0 session (decals, in-world messages, grip measurement). You play and look; I read the rest from `Player.log` afterwards. Balance itself was set by calculation (`docs/balance/cart-economics.md`), not by this session.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; the package lists the three `data/*.json` files.
- [ ] Installed on homepc with the game closed.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 20 minutes, one launch)

New game, saved under a name starting with **FT**; other gameplay mods off. Debug keys, holding Ctrl: **F7** free food cart, **Shift+F7** vanilla hand truck, **F8** mark a zone point, **F9** demand here, **F11** grip log.

| # | Do | Look for |
|---|---|---|
| 1 | Shift+F7, grab the vanilla hand truck. F11 once standing, once while walking. Then the same with a cart (F7) | Just do it; I read the numbers from the log and set the cart's grip from them |
| 2 | Push the cart along a dirty stretch of pavement | No street dirt drawn on the cart |
| 3 | Fit a hot dog grill, load hot dogs. While pushing, **Prices** in the item panel | A list of the cart's products with their price and what customers here pay at most. Set one price, then clear it again |
| 4 | Open at an office spot near a subway entrance around 08:00 to 10:00, then at lunch somewhere else | The messages read like the world, not instructions. Sales come (Ctrl+F9 shows the reading) |
| 5 | Sleep to the next day | The day summary's profit includes the cart. Econoview's income statement has a "Food carts" row |
| 6 | Walk the outline of one or two parks (Central Park's edge in Midtown, or any park or waterfront you know), pressing F8 at each corner. Write down which park | I turn the points into zones; coffee and ice cream do best there |

Anything that looks or feels off, write it down. When done, quit and tell me; I copy the log and park the mod.

## 3. Log lines (me)

- Startup: `Version 0.3.0 loading.`, `Demand data loaded ...`, `Active` for all patches, including `FinancialSummaryHelper.CreateFinancialSummary` and `EconoViewIncomeStatementScrollerController.LoadSalaryRows`.
- `Grip (...)` lines for both vehicles; `Zone point N: ...` lines; `Price: ...` lines for the price changes; `Cart ... open ...` with the spot showing `transit` near a subway; `Sale: ...`; no exceptions from the mod.

Not checked this time, on purpose: uninstall and loading without the mod (the save only gained additive fields), the pricing manager on carts (needs a headquarters and a hired pricing manager; the formula is the game's own), loading by hand truck or with a stocked fridge.
