# Stock panel at quit, 0.10.9

Issue #19: quitting the game while serving at the window logged `[Mod:FoodTrucks] ERROR: Serving work, stock panel: System.NullReferenceException` from `UI.ItemPanel.ItemPanelUI.Toggle` called by `FoodTrucks.ServingWork.End`. The error was caught and teardown carried on, but log checks fail on it.

## Cause

Closing the runtime during an unload or quit (`FoodTrucksSession.Detach` -> `CartSelling.OnDisable` -> `ServingWork.Reset` -> `End`) hid the work stock panel with the game's `ItemPanelUI.Toggle(false)`. That method sets `InstanceBehavior<GameManager>.Instance.selectedVehicle = null` without a null check. Once the game quits, the singleton lookup returns null (HGExtensions `InstanceBehavior<T>.GetInstance` while `SingletonLifecycle.IsApplicationQuitting`), and the manager may already be destroyed. The panel itself can still be alive, so the existing `panel != null` test did not catch this.

## Change

Only the stock-panel block in `ServingWork.End` (`unity/FoodTrucks/Scripts/ServingWork.cs`): `Toggle(false)` now also requires `!GameManager.isCitySceneBeingUnloaded` and a live `InstanceBehavior<GameManager>.Instance`, as well as the existing live panel and no entered vehicle. During an unload the scene removes the panel anyway. Booking held orders, removing the cargo listener and the work energy, resetting fast-forward and cancelling the time machine run as before, and the catch stays. Normal stop work, walking away, closing and stepping out of the hatch still hide the panel. No Core or save schema change.

Game members were checked with ILSpy against the cached `BigAmbitions.dll` (SHA-256 `9427561B81AAAB2ADA09C22A3C98DD9A4017A65225B389513885BF336852E911`): `public static bool GameManager.isCitySceneBeingUnloaded`, `GameManager : InstanceBehavior<GameManager>`, and `ItemPanelUI.Toggle(bool isEnabled)` writing `selectedVehicle` on the unchecked instance.

## Evidence

- **Checked (automated):** `tools/tests/test_serving_adapter.py` compiles the production `ServingWork.cs`. Its `ItemPanelUI.Toggle` stand-in now dereferences the `GameManager` singleton on `Toggle(false)` as the game does. New cases: with a live panel and the manager missing (unload flag on and off) or present with the flag on, `Reset` for a cart and a truck makes no Toggle call and logs no error, while held orders, listener, energy, speed and time machine are still cleaned up and all fields are cleared; a following session starts and closes its panel. With a live manager, stop work, walking away, step-out and `Reset` call `Toggle(false)` exactly once. The quit cases fail against the 0.10.9 source before this fix and pass with it.
- **Pending (in game):** the checks in `CHECKS.md` (SP1 to SP3). Not compiled against the full game and Unity references or run in the game.
