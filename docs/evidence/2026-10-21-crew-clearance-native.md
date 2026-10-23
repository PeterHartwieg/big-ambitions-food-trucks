# Practical crew clearance: native comparison

21 October 2026. Developer-only straight probe. Peter's clarified priority is to avoid excessive caution and keep gameplay moving. Production crew journeys remain timer-based.

## Scope and provenance

This follows the [trigger-certificate session](2026-10-21-crew-trigger-native.md). The same reserved native Apple Silicon `agentmac` rig ran in background graphics mode with Persistent City and Driven Rides present. Each accepted drive waited for the requested FT save to finish loading and for that load's physical-population and citywide-traffic activation messages. Commanded speed remained 1 m/s.

The isolated source combines baseline `e32834a` with the recorded crew-probe sources. All 32 provenance entries were checked against the working source and staged source. Only two assemblies were temporarily installed; 261 other manifest entries were preserved. The final native DLL SHA-256 is `40abf39fbfbbba2a67835cc093320297a7bb7ca5b861c37bdb589a7287bf492c`, and Core is `0f277119d750f2b1ec1ab4a84a9ae7a488348c2dababfba9eb4e8970fd655d52`. The previous diagnostic and first tuning packages and their provenance are retained separately in ignored build evidence.

The [review record](../reviews/2026-10-21-crew-obstacle-clearance.md) covers the read-only obstacle diagnostic and clearance changes. Opus 5.5, GPT 6 Astra and GPT 6.1 Sol approved the final correction for native testing. All 47 probe tests pass, including the recorded mailbox/trash geometry, inside-footprint detection, zero-allocation observation, lifecycle guards and diagnostic error serialization.

## Native comparison

| Package/run | Observed result |
| --- | --- |
| Diagnostic, original extra 0.35 m clearance | Held at 14.5280 m for `MailBox/SM_MailBox (76)`, which stood about 0.85 m clear of the physical truck; zero contacts/damage, explicit cancellation with clean handback |
| First tuning, extra 0.10 m clearance | Passed the mailbox, then held at 18.1106 m for `Trash Bag/SM_Trash Bag (137)`, about 0.75 m clear of the physical truck; zero contacts/damage, explicit cancellation with clean handback |
| Final lateral envelope | Passed both props and completed 30.9675 m of rear-axle travel along the 36.7785 m prepared route, ending by physical braking; zero contacts/damage, no forced hold and clean handback |
| Final envelope, fresh-save cart obstruction | Braked for a test cart in the lane, held, resumed automatically after the cart was moved away, and completed 8.1129 m by physical endpoint braking; zero contacts/damage, no forced hold and clean handback |

The mailbox is a static BoxCollider with bounds center `(227.709, 0.587398, 23.617418)` and size `(0.547821, 1.074796, 0.592724)`. The trash bag is a rotated, scaled static BoxCollider with world bounds center `(227.754044, 0.3597825, 26.18614)` and size `(0.830414, 0.901470, 0.738964)`.

The original sweep's half-width was 2.305578 m against a physical truck body about 2.491 m wide. The final half-width is 1.955578 m: the measured, tilt-padded body plus permitted tracking and heading/chord allowances, with no additional side margin. The combined envelope still extends about 0.70 m beyond the physical side. Forward/piece-length and corner clearance remain 0.10 m, as does immediate-body padding. Signal/junction margins remain 0.35 m. No prop, collider, collision layer or ground filter was bypassed or moved to complete the route.

The final long drive took 2,257 control steps. Native fuel decreased from 77.6000061 to 77.42793 L (0.17208 L); damage stayed zero. Owners, traffic records, junction registrations and retained body records were zero at handback, with no wheel-guard fault. Trigger coverage passed again after completion. Screenshots were inspected for the mailbox location and completed long-drive pose; movement, contacts and braking claims come from native telemetry.

## Controlled obstruction and lifecycle follow-up

A first cart test reused the truck after an explicit stop and test-only teleport back to the start. It cancelled after seven control steps with `WrongDirection`, before the obstacle encounter. It reported zero contacts/damage and clean handback. This is not a successful stop/resume test and is not proof of a general startup defect: the exact signed velocity/guard branch was not captured. Fresh-save fixtures isolate the clearance/braking check; the repeat-after-reposition case remains an explicit lifecycle follow-up. No direction guard was relaxed.

The final controlled run freshly loaded `FT Crew Routing Handback 20261021` and waited for Persistent City's physical traffic to activate. The probe truck was not teleported. A disposable test cart was spawned and placed at `(225.32, 0.01, 14.5)` before acquisition, with obstacle clearing disabled. The truck's decisive query identified `GameManager/ItemsContainer/FoodCart(Clone)` and physically braked for it.

After at least two wall-clock seconds of obstacle holding at measured speed below 0.01 m/s, read-only evidence recorded about **2.2037 m** between the truck's transformed physical envelope and the cart's near bounds along the straight road. The controller's conservative gap was 1.19936 m. The cart was moved back to its recorded spawn location, away from the lane. The first subsequent sample above 0.1 m/s arrived 1.122 wall-clock seconds after the clear operation; this is a sampled observation, not an exact reaction-time measurement. No restart or new drive command was issued.

The same probe completed the route in 779 control steps, travelling 8.1129 m and ending with 1.19106 m conservative endpoint clearance. Fuel decreased from 77.5987854 to 77.539856 L (about 0.05893 L). Contacts, damage, boundary violations, remaining ownership/traffic/junction records and wheel-guard faults were zero. There was no forced interruption hold. Post-run trigger coverage passed. This verifies a controlled solid-obstacle stop/resume; it does not establish moving-Gley traffic cooperation or signal behavior.

## Performance and cleanup

The final long drive's instrumented control timer averaged 1.140 ms and peaked at 5.836 ms. These are adapter timings from an instrumented, low-speed single-truck run, without a matched baseline or separately attributed native-physics cost. No fleet or frame-rate claim follows.

The controlled cart run recorded a 1.543 ms control mean and a 306.028 ms maximum; the wheel-guard maximum was 89.704 ms. These outliers remain unattributed. Profile them with a matched baseline before claiming acceptable fleet performance; successful movement is not performance acceptance.

The rig stopped normally. Both original assemblies and the original manifest were restored, all 263 file hashes matched, and the reservation was released with zero pending work. No fixture save was overwritten. The temporary test cart was not saved. Windows and the normal game installation were untouched.

Local artifacts are ignored under `build/crew-drive-obstacles/`: source provenance, installed hashes, diagnostic/first-tuning/final overlays, review transcripts, test/build logs, `native-summary.json`, `diagnostic-long/`, `tuned-long/`, `cart-stop-resume-1/`, `final-long/`, `cart-stop-resume-final/physical-stop-clearance.json`, `completed-native-run.log`, `completed-restore.txt` and `completed-release.json`.
