# Common trigger-height certificate: native Mac test

21 October 2026. Developer probe only; production crews still use timer-based travel. This tests the package approved in the [certificate review](../reviews/2026-10-21-crew-trigger-certificate.md).

## Installed build and conditions

The stopped, unreserved `agentmac` rig was acquired as `foodtrucks-crew-trigger-20261021`. All 32 recorded probe source entries matched both the working source and isolated build. The two installed assemblies were hash-verified:

- FoodTrucks: `c262efe5a4bc29cb2706f4613c78449aa1d31948aef608e7c8ea8543be9be0cc`.
- Core: `b22e5901fb69f3fe65a3ef9784d1d95b5f68fba5d09718d5b89601015b928677`.

The other 261 manifest entries were preserved, including Persistent City and Driven Rides. All 15 crew-probe hooks logged Active. The native Apple Silicon game ran in background graphics mode, bridge session `8b6c1224`, with a 1 m/s commanded speed cap and unchanged collision settings. Only disposable FT fixtures were loaded; no fixture save was overwritten.

## Observations

| Run | Conditions | Result |
| --- | --- | --- |
| `short-1` | Formerly refused Handback checkpoint; Persistent City loaded but its physical traffic activated after completion | First-attempt certificate; 8.1287 m rear-axle travel; endpoint reached by physical braking; zero contacts/damage; no forced hold or retained ownership |
| `long-2` | Ready checkpoint; physical population and citywide traffic activation confirmed before acquisition | Drove 14.5324 m, then physically braked and held for a street-prop collider with about 1.188 m conservative clearance; zero contacts/damage; explicitly stopped after observation; **not a route completion** |
| `short-2` | Formerly refused Handback checkpoint; physical population and citywide traffic activation confirmed before acquisition | 8.1162 m rear-axle travel; endpoint reached by physical braking; zero contacts/damage, no forced hold, no wheel/traffic fault and no retained ownership |

The initial long-run runner attempt, `long-1`, read the previous save's cached `done` health response immediately after acknowledging a new load. The subsequent body inspection refused during loading; no probe started. The corrected ignored runner checks the exact save and load name, `done`, zero pending work, and this load's physical-population/citywide-traffic log messages before acquisition. `long-2` resumed the acknowledged load rather than sending it twice. The initial short result is retained as evidence but is not evidence of driving after physical Persistent City activation.

### Certificate and short completion

`short-2` captured 30 initialized trigger profiles. Their common local interval was approximately 0.360224–0.999645 m. The first candidate selected the 0.629934–0.729934 m band using 64 cooked containment queries. Seven active models were nearby before acquisition, including the raised-floor `MersaidiDash(Clone)12`: its coverage passed with a root-height difference of −0.010668 m. Before/after diagnostics completed without a certificate failure. This verifies the raised-floor case in this run; it does not retrospectively identify the exact trigger responsible for the earlier generic refusal.

The completed run took 601 control steps and 2,404 wheel-guard calls. Endpoint clearance was 1.18778 m, with zero boundary violation. Fuel decreased from 77.5987854 to 77.55342 L (about 0.04537 L); damage stayed zero. Controller enablement, ignition, starter and kinematic state returned to their captured parked values. The known native cached `IsRunning` value remained true after engine shutdown, as covered by the reviewed handback behavior. No save/reload durability claim is added by this session.

A rendered end-position screenshot was inspected. Movement and braking evidence comes from the native telemetry, not the still image. Observed maximum sampled speed was 1.00933 m/s; the 1 m/s setting is the controller target, not an exact measured-speed clamp.

### Longer route obstruction

The 36.7785 m prepared path stopped for collider `4868606`, layer 26 (`StreetProps`), reported at approximately `(227.709, 0.587398, 23.617418)`, from the `piece` obstacle query. Seventy-six holding samples covered 44.92 simulated seconds; the truck kept approximately 1.188 m clearance. No obstacle was moved, disabled or excluded to obtain a pass.

The exact prop and its collision shape were not identified by the current diagnostics. One inspected screenshot showed the lane and truck; the reverse view was occluded. Whether this is a genuine obstruction or excessive conservatism in the sweep remains unresolved. This is the next debugging target before claiming longer-route functionality. A stationary prop does not establish traffic stop-and-resume behavior.

Explicit stop was issued only after verifying stationary holding, ownership and zero contacts. It produced `Cancelled / Explicit stop`, correctly recorded a forced interruption hold, and left zero owners, traffic records, junction registrations, subscriptions or retained body records. Fuel decreased by about 0.25676 L across driving and idling; damage stayed zero.

## Performance limits and cleanup

In `short-2`, the instrumented control timer averaged 0.996 ms and peaked at 6.323 ms; the wheel guard averaged 0.0525 ms and peaked at 0.7245 ms. In the longer drive/hold, control averaged 1.030 ms with a 38.465 ms maximum. These runs include bridge diagnostics and a large modpack; there is no matched baseline or separately measured native-physics cost. Fleet scalability and frame-rate performance remain unverified.

The rig stopped normally. Both original assemblies and the original installation manifest were restored; all 263 file hashes matched. The reservation was released with zero pending work. No production code or runtime safety checks changed during this session.

Ignored local evidence: `build/crew-drive-trigger/native-summary.json`, `installed-hashes.json`, `source-provenance.json`, `short-1/`, `long-1/`, `long-2/`, `short-2/`, `native-run-v2.py`, `completed-native-run.log`, `completed-restore.txt` and `completed-release.json`. The 20-drive/50-lifecycle campaign, moving traffic cooperation, signals, turns, Persistent City absent cases and matched performance measurements remain open.
