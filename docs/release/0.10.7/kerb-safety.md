# Street Food 0.10.7 cart kerb safety — 24 October 2026

Implementation: `a936e9083942b96498217594ffed8113c860fae7`, on `codex/cart-worker-position`. Normal push; no main changes or Workshop upload.

The employee adapter previously forced its nominal cart-relative point without navigation or street checks. `CartGroundPlacement` now shares the player's pedestrian-mesh, height, parking-lane, crosswalk, road and traffic-strip exclusions and bounded fallback search. Clear serving/waiting positions are retained. Visible crew avoid the serving player and each other; no safe point means an unseen employee. Parked fallback points are cached but their safety is rechecked before placement. Player reachability checks and truck places remain unchanged.

Queue creation now refreshes register/equipment displays immediately. An opening retains its selected side without a visible line. The first cart customer accepts at most a 0.1 m horizontal NavMesh snap; other cart places and truck tolerances retain their previous limits.

## Automated checks

- Core: **2,223 passed**, one existing opt-in soak skipped.
- Python: **254 passed**, including actual employee/player adapters, display notification, queue placement and side-retention harnesses.
- Unity SDK build, Windows/Mac bundle validation and physical-probe exclusion gate: **passed**.
- Final ZIP: `build/FoodTrucks-0.10.7-rc.zip`; SHA-256 **`5095d8ef4ff2a3500c33b4a9da7e87401d9fa3b3365ca1381d531e59fcdf9565`**. CRC, exact 19-file inventory and all packaged bytes verified.

## Windows native checks

Existing modified agent1 rig, build 3682, session `e2b454a4`; Steam disconnected and Workshop disabled. Established bridge-only adaptation and temporary observer; production placement/queue logic unchanged. Used a disposable copy of the prior final FT fixture. Its original shift ended at 20:00; extended only this disposable shift to midnight for supplementary captures.

Screenshot filenames below are relative to `build/kerb-fix-0.10.7/screenshots/`.

| Check | Result | Evidence |
| --- | --- | --- |
| Kerb-perpendicular, (42, 82), 90° | **PASS** | Cart legal; nominal (42.615, 0.01, 82.85) still rejects as parking lane with RoadBelow false. Visible employee falls back to (42.2023, 0.0857, 82.8491), on pedestrian ground with no street rejection. Player and waiting crew also safe. `kerb-edge90-employee-pavement.png`, `kerb-edge90-employee.png`, `kerb-edge90-player.png`. |
| 0.9 m inland, (41.1, 82), 90° | **PASS** | Employee keeps local (-0.85, 0.615), facing across; no street rejection. Player safe. `kerb-inland90-employee.png`, `kerb-inland90-player.png`. |
| CW1, clear right/180° | **PASS** | Register x=+0.29; employee local (-0.85, 0.615), facing +X. First queue place along≈0.620 (rounded bridge coordinates). `CW1-right180-employee.png`, `CW1-right180-player.png`, `CW1-right180-crew-and-customers.png`. |
| CW2, clear left/37° | **PASS** | Register x=-0.29; employee local (+0.85, 0.615), facing -X. First queue place along≈0.612. `CW2-left37-employee.png`, `CW2-left37-player.png`, `CW2-left37-customers.png`. The customer capture was after the original shift; employee evidence is the earlier on-shift capture. |

At the obstructed kerb edge no visible customer line is laid out: the former ≈0.415 m anchor snap now exceeds tolerance. This is the intended anchor guard, not a claim of exact-register service where pavement is obstructed. All visible crew/player positions passed the actual native street exclusions and shared pedestrian-ground check. Night lighting/bus headlights obscure some screenshots; precise positions and checks accompany them. The screenshot index explicitly labels supplemental captures, including the final left-side image whose harness filename mentions customers but whose queue was empty.

Always-on log checker: **111 active patches, zero inactive patches, mod errors or FoodTrucks exceptions**. Existing Unity NavMesh spawn warnings also occur in the previous run; they remain. CW3–CW6 were not rerun in this focused native pass; clear stance/heading/waiting and truck behavior remain covered by adapter/Core regressions and the previous native run.

## Isolation and restoration

Acquired the free lease at **16:18:53 UTC** with no game process. Full verified backup before installation; every launch checked ownership and absence of all game processes. An initial local wrapper attempted a nested lifecycle reservation and was safely refused; corrected the wrapper before launch. No normal install/profile/preferences/saves/subscriptions or power settings were changed.

Restored at **16:29:45 UTC**: all **4,667 original profile files**, preferences, original install receipt and **107 referenced files** match SHA-256. Released by **16:29:55 UTC**: no game processes, lease unheld, zero pending operations. All **17 native 1920×1080 PNGs** match their PC hashes. Raw replies, logs, manifests, backup/restore/release receipts and the screenshot index are under `build/kerb-fix-0.10.7/`.

The native-tested ZIP hash was `e7c0402a49a706ace49877194e7b4487e9524f570cd413476be8e4a749ef03ec`. Final ZIP differs only in README's updated evidence status; all runtime files are byte-identical to that tested candidate before the established private bridge adaptation.
