# Crew routing: body/wheel contract review

Requested next implementation chunk, 21 October 2026. Reviewers were Opus 5.5, GPT 6 Astra and GPT 6.1 Sol; bounded implementation assignments used GPT 6.1 Sol. The working tree is based on `a09a814`; the isolated rig package uses the newer `e32834a` baseline plus these probe sources. No other task's release/art work was included.

## Architecture and round 8

The panel agreed that the authoritative body must remain visible and that only exact native NWH wheel-mesh references could receive an exception. Both enabled tops and disabled bottoms must remain in physical geometry. Astra recommended continuous steering/suspension containment and a required native wheel-step prefix. Opus proposed actual-pose closest-point checks and separate body/wheel obstacle bands. The implementation chose the bounded straight profile, continuous certification and native step boundary; it retains one full-height physical envelope and conservative road handling.

Review found and corrected world-coordinate precision loss, repeated full validation from wheel/ground calls, missing wheel-guard timing, and a steering cap too small for ordinary placement error. Core and traffic now share the narrower tracking allowances. A lost-session wheel callback no longer writes to a body outside its token's authority. Cleanup retries are bounded.

Sol identified two further blockers: source vertices do not prove the actual cooked collider, and projection across all heights can hide a narrow lower body beneath a wide roof. These remained blockers despite the other reviewers approving the initial straight-test direction.

## Rounds 9–10

Round 9 verified the lifecycle, precision, performance and tracking fixes. The remaining geometry blockers were resolved with per-wheel prisms certified inside the actual cooked convex body and a common height band covered by upright native box triggers. Root tilt is bounded and included in the prism padding. Cooking settings, mesh vertices and triangle indices are guarded; unknown geometry refuses rather than falling back to an optimistic bounding box.

Regressions cover different source/cooked shapes, tapered bodies, short or rotated triggers, cooking/index mutations, translated/tilted roots, disabled bottom activation and allocation-free repeated checks. The installed asset has 196 vertices after export; the final bounded inventory allows 256 while retaining the cooked-shape proof.

Round 10 Sol and Opus approved controlled native testing. Astra found a remaining forward-sweep gap for permitted root pitch: an upper-front obstacle could evade the upright lookahead. The fix adds planar and vertical tilt bounds about the rear axle, including the projected-heading change under combined pitch and roll. A native-adapter regression reproduces the formerly missed overhead obstacle; independent corner checks exercise pitch, roll and combined poses.

In round 11, all three reviewers found no remaining blockers and approved the controlled native sequence. The source-vertex cap correction was also reviewed. These approvals concern controlled testing only, not Gate A or production deployment.

## Native lifecycle correction, rounds 12–13

The first powered acquisition returned its native body and released all records, but a second start refused the truck. Native `IsRunning` remains cached after shutdown because the controller is disabled before another engine step. Admission and restoration must not use that cache as the commanded engine state.

Round 12 identified another native default: the prefab retains ignition on while parked. A direct read after a fresh save load confirmed ignition true, cached-running false, controller disabled, starter inactive and a kinematic body. The final adapter therefore requires the stationary callback, player/controller/starter inactivity, a kinematic body and near-zero motion. It preserves the captured ignition flag on restoration without calling `StartEngine` again. Tests compile the actual adapter and cover both parked ignition defaults across repeated cycles, partial-start cleanup and refusal of active owners. Forty-five combined adapter tests pass. All three reviewers approved the corrected lifecycle in round 13 for controlled repeated acquisition and the short 1 m/s native run. No review claims Gate A or production acceptance.

## Evidence boundaries

The main Core suite passed 2,085 tests with one existing skip; the isolated updated baseline passed 2,141 with one skip. Forty-five adapter and geometry harness tests passed. Native-reference compilation reported zero errors and nine existing warnings; official SDK packages built successfully. The final source snapshot and patched package were hash-checked before installation; only the two Food Trucks assemblies changed in the private rig.

On the older diagnostic package, all 15 hooks activated, collision overrides were default and four native wheel contacts established level support. One stationary traffic-cooperation observation recorded the same Gley car slowing to zero with the truck body as its sole native obstacle, then clearing that membership and resuming after the truck was removed. This is native telemetry; an occluded screenshot is explicitly excluded as visual evidence. The final cooked certificate subsequently passed natively. An acquisition/explicit-stop check ran 23 control steps with no contacts and returned all owner/traffic counts to zero. A second start exposed native `IsRunning` caching after shutdown; the corrected final package subsequently passed repeated acquisition and the short drive.

Review briefs, model outputs, test logs, installation receipts and runtime samples are local under ignored `build/crew-drive-routing/`. Production crew dispatch, automatic parking, fuel purchases and save schemas remain outside this chunk.

## Final native result

Final package native SHA-256 `492fd122ec0c824cdd4febc4c123a8f824e9c0b651242b08364816ba9eeef835`, Core `6ba63f17d611579665c4737acd1ec0e6ff298107b9fc71c6f2afbcd3a0695c54`, source baseline `e32834a` plus the recorded probe overlay. One final-package acquisition refused nearby trigger coverage before engine acquisition. After ordinary traffic cleared, an explicit-stop cycle handed back cleanly and the next acquisition completed the short straight route: about 8.1 m rear-axle travel, 648 control steps, zero contacts/boundary violations, no forced interruption hold, zero leftover records. Fuel was 77.55202 L after the run and remained so after save/reload; pose and zero damage also persisted. Native diagnostic readings confirmed parked ignition=true and cached-running=true with controller/starter off and body kinematic after hand-back.

The trigger-coverage refusal remains an explicit limitation, not a relaxed check or a pass. The next scope is precise rejecting-trigger diagnostics and a cooked-body/common-height certificate that accommodates the initialized native trigger inventory. One completed drive does not satisfy the 20-drive/50-lifecycle campaign, turns/signals, collision-damage or fleet-performance gates.

The original rig modpack was restored with all 263 manifest hashes matching; final status was stopped, unlocked, zero pending. Detailed native measurements and evidence filenames are in [the implementation record](../crew-driving-implementation.md).
