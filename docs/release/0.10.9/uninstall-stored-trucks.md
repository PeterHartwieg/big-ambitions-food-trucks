# Uninstall and the private driver's garage, 0.10.9

GitHub issue #13: safe uninstall left Food Trucks vehicles in the private driver's garage and could still say it was safe to remove the mod.

## Cause

A vehicle dropped off with the private driver moves from the game's `VehicleInstances` list to its separate, saved `privateDriverVehicleInstances` list. `Uninstall.Request` looked only at `VehicleInstances`. With only a stored truck, the save took the "no vehicles" path and ended with `notification_uninstall_none`; in a mixed fleet the stored truck was neither counted nor removed nor refunded, and `notification_uninstall_done` still showed. The game's `VehicleHelper.Delete` removes from `VehicleInstances` only, so the garage entry needs its own removal.

## Change

In `unity/FoodTrucks/Scripts/Uninstall.cs` only:

- The fleet is read from both lists (every `foodtrucks:vehicletype_` type, old ones included), once per vehicle ID. A stored truck is counted in the confirmation and never takes the "no vehicles" path. A vehicle without an ID is kept as its own entry and is never taken for gone.
- For each vehicle ID, the game's own deletion runs for each copy on the street, as before. Then the private driver's entries with that ID go. If a street copy can't be deleted, the garage entry stays too, for a retry. The refund (purchase price plus paid stock, nested stock included) is paid once per ID, and only after both lists are checked again and no vehicle with that ID is left.
- Both success notices (`uninstall_none` and `uninstall_done`) now require a fresh look at both lists and the pending deliveries to find nothing, besides the existing sales, payment and campaign checks. Otherwise the partial notice shows what is left.

Unchanged: the held-vehicle guard, the newer-save gate, refund amounts and payments, the mod-item deduction reversal, other vehicles in both lists, and the private driver's contract and capacity. No save schema change. As before, a refund that the game refuses after removal is reported and logged but not kept for a later retry.

## Evidence

- **Checked (automated):** `tools/tests/test_uninstall.py` compiles the production `Uninstall.cs` against stand-ins in `tools/tests/uninstall/` whose native delete, like the game's, removes from `VehicleInstances` only. It covers stored-only, active-only, mixed (with an old type and vanilla and other-mod vehicles in both lists), empty, no garage list, the same object in both lists, separate copies with one ID, a stored truck with a pending delivery, a native delete that throws or keeps the vehicle (no refund, partial notice, one payment on retry), a refused refund, the mod-item deduction, the held vehicle, the newer-save gate, a vehicle without an ID and a repeated uninstall that pays nothing more. The same test fails on the previous code (21 failed checks; stored-only gets `confirm_none` then `none`, the truck stays and nothing is paid).
- **Pending (in game):** UN1 to UN4 in `CHECKS.md`. Whether the private driver accepts a Food Trucks vehicle type unchanged is also unverified; if not, a stored entry must be prepared as a labelled rig fixture.
