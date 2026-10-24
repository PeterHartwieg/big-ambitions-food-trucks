# Street Food 0.10.7 — centred cart worker

Employees, player stance and first customer share the midpoint of the two fitted worktop spots. The Unity adapter reads the actual fitted display anchors; Core computes their midpoint. With fewer than two fitted worktop pieces, service uses counter-local z=0. The dedicated below-counter fridge is excluded. Current anchors at z ±0.30 give z=0. The server remains 0.85 m across from the cart centre on the side away from customers, facing across the counter.

The register remains at (±0.29, 0.95, 0.615). Its existing corner mount leaves the working gap between appliances open. Operator-facing equipment yaw and worker-side fridge mounting remain as in the grill-facing fix. Food truck paths and geometry are unchanged. No version or save-schema change.

Crew placement caches follow changed equipment centres. A changed centre releases a held player stance and rebuilds the cart queue. Both customer and server pavement checks, road/parking/crosswalk exclusions, player reachability and bounded counter-side fallbacks remain. No end-of-cart service.

## Validation

- `dotnet test FoodTrucks.sln`: **2,251 passed**, zero failed; one pre-existing opt-in 200-seed soak skipped.
- `FT_TEST_TRANSPORT=inprocess python3 -m unittest discover -s tools/tests`: **254 passed**, final run 140.934 s; prior full run also passed. Actual crew, serving and queue adapters exercise asymmetric fitted spots, zero/one/two fitted items, dedicated-fridge exclusion, both sides/headings, equipment removal, pavement exclusions and unchanged trucks. Core/spec plus actual VehicleDisplays still cover all 18 prefab orientations × both sides × 360 headings (12,960 combinations).
- Official Unity SDK build, Windows/Mac bundles, bundle validation and ordinary-release physical-probe exclusion gate: **PASS**. Archive CRC, exact 19-entry inventory and every byte against built package: **PASS**.
- Native existing Windows agent1, build 3682, session **596be1fd**, 1920×1080 windowed. Steam disconnected and Workshop disabled, confirmed by live health. Disposable FT fixture only; no observer placement or yaw override.

| Case | Queue side | Employee local (x,z) m | Player local z m¹ | First customer local z m¹ | Places | Result |
| --- | --- | --- | --- | --- | --- | --- |
| kerb-edge90 | right | (-0.849999, -0.000004) | 0.000000 | -0.000000 | 6 | PASS |
| CW1-right180 | right | (-0.849995, -0.000000) | 0.000000 | -0.000000 | 10 | PASS |
| CW2-left37 | left | (0.849998, -0.000002) | 0.001930 | -0.002384 | 8 | PASS |

¹ Player/customer coordinates are rounded to 0.01 m by bridge state; employee coordinates come from the live transform. All employee and player checks have safe pedestrian ground, no street rejection and correct customer-facing yaw. Right/180° has two occupied queue places and left/37° one; both include the real first customer. The first place is aligned with the working centre, independently of the register. Native machine close-ups verify HotDogRoller/BurgerStation controls and the dedicated fridge on the worker side, with mirrored register facing customers. Other models and zero/one-item fallbacks are covered offline, not claimed as separately photographed.

The new centre at the 90° kerb edge itself passes pavement checks and allows six queue places. The former register-aligned stance at z=0.615 fell in the parking lane there. This is a changed target, not a relaxed safety rule.

Log checker: 111 active patches, zero inactive patches/mod errors/FoodTrucks exceptions (always-on checks; no version-specific 0.10.7 rules). The retained fixture logs one GameManager NullReference during the **preparation midnight**, immediately following `internetcafe:itemname_pcsession` missing from ItemsDictionary; the stack contains GameManager, not a placement adapter. The preparation error’s cause was not diagnosed in this placement task. Native placement checks occur later in daylight without further exceptions. Existing native NavMesh spawn warnings remain. No broader overnight/foreign-mod compatibility pass is claimed.

## Package and evidence

Implementation commit: `b19c36aa9224672cf4dad31cd90b399847c346db`, pushed normally on `codex/cart-worker-position` before native checks.

ZIP: `/Users/peterhartwieg/Coding_Projects/big-ambitions/big-ambitions-food-trucks-worktrees/cart-worker-position/build/FoodTrucks-0.10.7-rc.zip`

SHA-256: `6808acb91a3686a76f431f0cc5dbe40657739c44cd87833ae6263868e297fde6` (also in `.zip.sha256`). This exact ZIP was native-tested before the established bridge-port adaptation; all release bytes remain unchanged after testing. Release DLL SHA-256 `a382d3fbfc5ebb97b7001095d1b7943ef8aab94be0ee3f16f2b14ddeb6cbe472`; rig-adapted DLL `35907f01fcf9ced258d24d9e53ba98ccded715c2427851ad9f0b517b27622c56`.

All **30 unaltered native PNGs** have matching PC/local SHA-256 and 1920×1080 dimensions. Local screenshots: `/Users/peterhartwieg/Coding_Projects/big-ambitions/big-ambitions-food-trucks-worktrees/cart-worker-position/build/center-worker-0.10.7/screenshots`. Stable PC screenshots: `C:\Users\Peter\HART-Diagnostics\Performance\streetfood-107-center-20261024\screenshots`. `INDEX.md` identifies the elevated, worker-side and customer-side triplets for employee and player in all three layouts, and real-customer captures. Some player views include a foreground customer; the primary employee close-ups are clear.

Lease acquired only after no Big Ambitions processes at **2026-10-24T18:00:08.0482368Z**; all launches/check groups and subsequent test mutations rechecked normal-game absence. Fresh isolated-profile backup: 4,667 files, preferences and install receipt. Only AgentOverlay, test observer and candidate installed; other isolated mods parked and restored. Disposable fixture clock advanced to day 3 daylight and its crew scheduled through bridge; closed fixture truck moved 14 m to clear the camera. Normal game/profile/ModsLocal/saves/preferences/subscriptions and power policy untouched; no wake, main changes, force-push or Workshop upload.

Restored at **2026-10-24T18:11:29.8833461Z**: all **4667** original profile files, preferences, install receipt and **107** referenced files verified by SHA-256. Final status **2026-10-24T18:12:34.7161926Z**: no game process, lease unheld, zero pending operations. Early installation was refused by the lifecycle guard while backup was still pending (no profile mutation); the daylight helper’s crew lookup was corrected from crew-name list to route.shifts before checks. All test helpers and original receipts/logs are retained.

Full evidence/receipts: `/Users/peterhartwieg/Coding_Projects/big-ambitions/big-ambitions-food-trucks-worktrees/cart-worker-position/build/center-worker-0.10.7`. PC backup and retained tested profile: `C:\Users\Peter\HART-Diagnostics\Performance\streetfood-107-center-20261024`.
