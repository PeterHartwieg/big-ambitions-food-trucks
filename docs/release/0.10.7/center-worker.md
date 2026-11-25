# Street Food 0.10.7: centred cart worker

Employees, the player's stance and the first customer share the midpoint of the two fitted worktop spots. The Unity adapter reads the fitted display anchors and Core computes their midpoint. With fewer than two fitted worktop pieces, service uses counter-local z=0. The dedicated below-counter fridge is excluded. The current anchors at z ±0.30 give z=0. The server stands 0.85 m from the cart centre on the side away from customers, facing across the counter.

The register stays at (±0.29, 0.95, 0.615) on its corner mount. Operator-facing equipment yaw and worker-side fridge mounting are unchanged from the earlier 0.10.7 grill-facing fix. Food truck paths and geometry are unchanged. No version or save-schema change.

Crew placement caches follow changed equipment centres. A changed centre releases a held player stance and rebuilds the cart queue. Customer and server pavement checks, road, parking and crosswalk exclusions, player reachability and bounded counter-side fallbacks all remain. There is no end-of-cart service.

## Native results

Windows rig agent1, game build 3682, session `596be1fd`, 1920x1080 windowed, 24 October 2026. Steam disconnected and Workshop disabled, confirmed by live health. Disposable fixture only, with no observer placement or yaw override.

| Case | Queue side | Employee local (x,z) m | Player local z m | First customer local z m | Places | Result |
| --- | --- | --- | --- | --- | --- | --- |
| kerb-edge90 | right | (-0.849999, -0.000004) | 0.000000 | -0.000000 | 6 | PASS |
| CW1-right180 | right | (-0.849995, -0.000000) | 0.000000 | -0.000000 | 10 | PASS |
| CW2-left37 | left | (0.849998, -0.000002) | 0.001930 | -0.002384 | 8 | PASS |

The bridge rounds player and customer coordinates to 0.01 m. Employee coordinates come from the live transform. Every employee and player position is on safe pedestrian ground with no street rejection and the correct customer-facing yaw. Both sides are covered: right at 180° with two occupied queue places, left at 37° with one, and both include a real first customer. The first queue place lines up with the working centre, not the register. Native close-ups show the HotDogRoller and BurgerStation controls and the dedicated fridge on the worker side, with the register facing customers.

At the 90° kerb edge the new centre passes the pavement checks and allows six queue places. The old register-aligned stance at z=0.615 fell in the parking lane there. The target changed. The safety rule did not get looser.

The log checker found 111 active patches and no inactive patches, mod errors or FoodTrucks exceptions. The fixture logged one GameManager NullReference during the preparation midnight, right after `internetcafe:itemname_pcsession` was missing from ItemsDictionary. Its stack has GameManager and no placement adapter. That cause was not diagnosed. The placement checks ran later in daylight without further exceptions. The existing native NavMesh spawn warnings remain. No broader overnight or foreign-mod compatibility pass is claimed.

## Offline coverage

Zero, one and two fitted items are covered offline only. They were not photographed separately in game, and neither were models other than the HotDogRoller and BurgerStation.

- `dotnet test FoodTrucks.sln`: 2,251 passed, none failed, one opt-in 200-seed soak skipped.
- `FT_TEST_TRANSPORT=inprocess python3 -m unittest discover -s tools/tests`: 254 passed. The crew, serving and queue adapters run asymmetric fitted spots, zero, one and two fitted items, dedicated-fridge exclusion, both sides and headings, equipment removal, pavement exclusions and unchanged trucks.
- Core and the real VehicleDisplays cover all 18 prefab orientations x both sides x 360 headings (12,960 combinations).
- Official Unity SDK build, Windows and Mac bundles, bundle validation and the release physical-probe exclusion gate: PASS. Archive CRC, the 19-entry inventory and every byte against the built package: PASS.

## Evidence identifiers

- Implementation commit `b19c36aa9224672cf4dad31cd90b399847c346db` on `codex/cart-worker-position`, pushed before the native checks.
- Tested archive `FoodTrucks-0.10.7-rc.zip`, SHA-256 `6808acb91a3686a76f431f0cc5dbe40657739c44cd87833ae6263868e297fde6`. Release DLL SHA-256 `a382d3fbfc5ebb97b7001095d1b7943ef8aab94be0ee3f16f2b14ddeb6cbe472`. Rig-adapted DLL (bridge port only) `35907f01fcf9ced258d24d9e53ba98ccded715c2427851ad9f0b517b27622c56`. The release bytes were unchanged by testing.
- 30 unaltered native PNGs at 1920x1080, with matching SHA-256 on the PC and locally. On the rig they are in `HART-Diagnostics\Performance\streetfood-107-center-20261024\screenshots`, with an `INDEX.md` naming the elevated, worker-side and customer-side views for employee and player in all three layouts. The same rig folder holds the profile backup and the tested profile.
- Rig lease acquired at 2026-10-24T18:00:08.0482368Z with no game process running. All 4,667 profile files, preferences and the install receipt were restored and verified by SHA-256 at 2026-10-24T18:11:29.8833461Z. Final status at 18:12:34.7161926Z: no game process, lease released, no pending operations.

## Final-build limitation

This evidence is for the approved branch's archive above. The final 0.10.7 rebuild was not launched in game. Its gameplay code matches the approved package by normalized compiled comparison, as recorded in `release-verification.txt`, `build-equivalence.json` and `package-verification.json` in this folder.
