# Crew obstacle evidence and practical clearance

21 October 2026. Developer probe only. Peter clarified that driving should avoid excessive caution and keep gameplay moving. This follows the [common-height native check](../evidence/2026-10-21-crew-trigger-native.md), whose longer route stopped for an unidentified street prop.

## Read-only evidence

The probe now captures the exact decisive collider reference, cached identity and overlap-box geometry with value/reference assignments in the observer. The dedicated `obstacle_evidence` bridge operation formats bounded diagnostics on demand: captured query data, current collider hierarchy, authored shape, rigidbody, transform and truck pose. Captured and live fields are explicitly separate. It does not scan the scene, resolve replacement objects by ID, run extra physics queries or alter ownership.

Opus 5.5, GPT 6 Astra and GPT 6.1 Sol reviewed the diagnostic independently. Sol found that reassigning an already-parented Newtonsoft object could clone it and lose later error writes. The fix mutates an existing section directly and assigns only new sections. A regression using the actual helper and real Newtonsoft failed before the fix and passed afterward. All three then approved controlled native testing.

## Native diagnosis and correction

The diagnostic build identified `MailBox/SM_MailBox (76)`, a static BoxCollider at world bounds center approximately `(227.709, 0.587398, 23.617418)`, size `(0.547821, 1.074796, 0.592724)`. The truck's physical body cleared its nearest side by about 0.85 m. The padded sweep had a 2.305578 m half-width and reached into the mailbox; the truck stopped without contact. A rendered screenshot confirmed the mailbox stood on the pavement beside the lane.

The first correction reduced extra sweep clearance from 0.35 m to 0.10 m. Its native rerun passed the mailbox but stopped for `Trash Bag/SM_Trash Bag (137)`, also on the pavement, with about 0.75 m physical side clearance. The second correction therefore uses the already combined physical, tilt, tracking and heading/chord envelope directly for lateral queries, without extra side padding. Forward/piece-length and corner padding stay at 0.10 m, as does immediate-body padding. Signal and junction margins retain 0.35 m. All obstacle layers, ground filters, trigger checks, braking and ownership remain unchanged; there are no per-prop exceptions.

The final regression uses both recorded props and actual adapter-generated query geometry. Mailbox clearance after the combined allowances is approximately 0.15 m, and the conservative trash-bag bounds clear by more than 0.05 m. Moving a collider inside that footprint still blocks. Existing allocation checks remain intact. All three reviewers independently approved each correction for controlled native testing. Opus requested measuring physical front clearance in the controlled obstruction stop because the initial change also shortened each query piece's longitudinal reach.

All 47 probe tests pass. The first broader run found stale visibility-harness interfaces for the new bridge operation; the test-only stubs were updated and the complete suite rerun successfully. Native-reference compilation has zero errors and nine unrelated warnings. The official SDK package builds successfully. Native evidence, including a separate startup cancellation after a test-only teleport, must be kept distinct from these stub and compilation results.

The [native comparison](../evidence/2026-10-21-crew-clearance-native.md) passed after the final correction: the longer route completed past both pavement props, and a fresh-save cart obstruction test braked, held, resumed automatically after clearing, and finished. Both had zero contacts/damage and clean handback. The separate teleport/restart cancellation and unattributed timing outliers remain follow-ups. The original rig files were restored and its reservation released.

Review transcripts and build/test evidence are ignored under `build/crew-drive-obstacles/`. Gate A remains open for moving traffic, signals, turns, the wider lifecycle campaign and matched performance measurements.
