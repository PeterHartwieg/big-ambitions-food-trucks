# Dedicated customer-facing drinks fridge — 22 October 2026

Implemented in the working tree; not published. Carts retain two general equipment places and trucks four, with one additional place restricted to a drinks fridge. Either vanilla fridge qualifies. Further fridges use general places, preserving valid older multi-fridge layouts. No inventory item or save schema was added. Soda prices, demand, serving time, storage capacity and Extra Storage multiplication are unchanged.

## Artwork and placement

The original portable fridge remains available for general places. Two Blender-derived displays provide the reserved position:

- Cart: a 0.392 × 0.0704 × 0.3836 m cabinet-front insert below the rear worktop, mirrored and turned to the queue side.
- Truck: a 0.392 × 0.1584 × 0.548 m shallow fridge on the rear end of the serving counter, facing customers.

`art/tools/build_customer_coolers.py` reproduces the sources/exports; `review_customer_coolers.py` checks the exported geometry and produces review views. Both cart sides and the truck passed the geometry checks, including support on the counter, cooking equipment/register clearance and truck hatch positions from 0 to 80 degrees. Both surface-look scores passed. BizMan has a dedicated place, layer and empty-place tooltip; its generated artwork was visually reviewed.

Local artwork evidence: `build/cooler-slot/geometry-checks.json`, `geometry.log`, `surface-look.log`, `cart-left.png`, `cart-right.png`, `truck-counter.png` and `diagram-check.png`.

## Native smoke check

Ran the final built package on the isolated Mac rig with its test-bridge patch, session `555b2c88`, using a new Save As copy of the existing test lab. The original lab save was not overwritten.

| Check | Observed result |
| --- | --- |
| Full cart plus fridge | Storage cabinet and dumpling steamer occupied both general places; a small drinks fridge loaded successfully into the dedicated display |
| Full truck plus fridge | Storage cabinet, dumpling steamer, griddle and corn grill occupied four general places; a large drinks fridge loaded successfully into the dedicated display |
| Customer-facing artwork | Native screenshots show the cart insert below the worktop and the truck fridge beside the open serving window, with the register clear |
| Capacity with Extra Storage | Cart accepted 160 of 200 cans, leaving 40 in the player's hands; truck accepted 320 cans |
| Open and sell | Both opened under player service. Cart sold one soda for $3; truck sold six orders including two sodas |
| General equipment limit | An additional hot dog grill was refused on the full truck, with the new four-general-plus-fridge explanation |
| Current-build save/reload | Both vehicle identities, menu pools, stock, closed state and daily sales ledger matched before/after. The truck's to-do list reordered; its entries were unchanged |

Local evidence is in `build/cooler-slot/native-*.json`, `native-player.log`, `native-reload-check.txt`, `native-cart-close.png` and `native-truck.png`. The general-piece and cooler model assignments are logged by `VehicleDisplays`; storage cabinets do not appear as saleable food pools in the bridge state.

After testing, the rig was stopped, its original mod snapshot and manifest restored, all 263 original file hashes verified, and the reservation released. Existing missing-bundle errors from other optional mods mean this run is not evidence of a globally clean game log.

## Automated checks and build

- Full Core suite: 2,099 passed, one existing soak test skipped. After the final optional-tuning/physical-slot adjustment, all 25 focused cooler/menu tests passed.
- Dedicated mock commissary-transfer test: passed all four cart/truck × small/large fridge cases.
- Broader Python tools suite: 240 passed, two failed. The remaining failures concern the old driving-mod name (`test_drive_refusals_and_reply_match_the_bridge`) and CLI manual expectations (`test_the_manual_lists_the_commands_and_nothing_else_is_there`); neither asserts cooler behavior.
- Windows and Mac Unity bundles built and validated. `git diff --check` passed.

Logs: `build/cooler-slot/core-tests-final.log`, `final-focused-tests.log`, `mock-cooler-tests.log`, `tools-tests.log`, `build-verified.log`.

Final runtime SHA-256 values:

| File | SHA-256 |
| --- | --- |
| `build/FoodTrucks/FoodTrucks.dll` | `cc494cf40affc2e17dfbb0caaa1d53fc7035a61be4ef174b43a78b048a901f85` |
| Mac bundle | `4274756ba75cedf6d030d038e3787ed2a6520d7a5b62b6348114b56fcbe8a605` |
| Windows bundle | `20b6596d1a8315cacb60e3d50487ef00eef320a9647220154440b8222197ad9e` |
| Temporary Mac test-bridge DLL | `2b43e93ed6b8b7d41b9e7ad6644ecbd5110c9749f94fa3e86f2b4214b67ecf56` |

## Still pending

CHECKS CSF1–CSF5 retain the full acceptance scope: historical multi-fridge saves, native commissary transfer of stocked fridges, fitting/removal order, both cart queue sides during movement, hide/show/respawn, native BizMan hover/fill interactions, and matched economic comparisons at quiet/busy/saturated pitches. The new slot removes the equipment opportunity cost; this smoke run does not establish how much extra profit drinks produce or whether drink orders displace food at busy pitches. Windows-native playtesting and publication are also pending.
