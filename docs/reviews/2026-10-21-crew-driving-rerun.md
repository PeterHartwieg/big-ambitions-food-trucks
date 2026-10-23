# Crew driving rerun review — 21 October 2026

The requested panel ran parallel review rounds 5–7: Opus 5.5, GPT 6 Astra and GPT 6.1 Sol. All three concluded **APPROVE FOR CONTROLLED DIAGNOSTIC TESTING** for the completed source. This is approval of the diagnostic rerun, not powered driving, traffic cooperation or Gate A acceptance.

## Corrections and validation

- Native Gley tests same-root layers first, then building masks, then obstacle/player masks. Visibility admission now respects that precedence and requires a rigidbody for every actual pooled model root layer. Tests cover overlapping masks, later mask changes and roots outside the setup's traffic mask.
- An admission failure before `Begin` now disposes only probe resources. It does not hold the native body, save pose/damage, mark the save changed or stop the engine. The `beganNative` guard is set before engine acquisition, preserving cleanup after a partial start. Late-damage registration begins there too. Tests cover both traffic and placement refusal, partial engine failure and later callbacks.
- Added a bounded, read-only collider inspection command. It reports stable hierarchy paths, exact native body/additional/parking roles, exact NWH top/bottom mesh references separately from wheel ancestry, disabled colliders, shape-local bounds transformed into root-local enclosures, and grouped initialized Gley rules, triggers and collision-matrix results. It never acquires probe ownership or changes the vehicle.

Twenty adapter, traffic and inspection tests passed, including six inspector cases. The source compiled against native game/Unity assemblies with zero errors and nine existing warnings. The official SDK built the test package against the updated rig's `45b4699` Food Trucks 0.10.2 baseline; its integrated Core suite passed 2,126 tests with one existing skip. That source integration was isolated under ignored `build/crew-drive-rerun/source`; it did not merge the rig branch into the working checkout. Artifact checks verified both the newer baseline and the probe/inspection changes in the package.

## Limits and next decision

All three source reviews finished before the detailed native visibility capture. Their approval must not be represented as independent validation of that capture. The parent verified the installed artifact and native results recorded in [the implementation checkpoint](../crew-driving-implementation.md).

The panel agreed that a future exception must distinguish the real body from known native wheel shapes, refuse an invisible body and preserve physical collision coverage. It has **not** agreed on the final containment or vertical-observation strategy: Astra favored an explicitly identified wheel exception with conservative physical geometry retained; Opus requested body-outline containment and a separate body-height observation band. Neither proposal is enabled. Do not discard low obstacles or weaken physical geometry merely to obtain a passing drive.

Opus also identified additional evidence needed before changing that policy: per-collider and rigidbody include/exclude masks, override priority, collision-enabled/kinematic state and comparable world heights. The current report's matrix result is only a matrix result, not a complete contact condition. These reporting additions belong with that next contract. Its suggested outside-city guard already exists in the command dispatcher (`RequireFtCity`), so no duplicate guard was added.

Local briefs, model outputs, tests, source/installed hashes and native evidence remain under ignored `build/crew-drive-rerun/`. No game binaries, decompiled sources or saves are committed.
