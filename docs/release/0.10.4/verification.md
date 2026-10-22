# 0.10.4 verification — 22 October 2026

Unlimited employee rosters were verified in the native Mac game on the owned `agentmac` rig, using disposable FT saves. Windows and Mac bundles were built with the official SDK. Windows gameplay was not repeated for this change.

## Review and automated checks

Two review rounds by Opus 5.5, GPT-6 Astra and GPT-6.1 Sol concluded SHIP. Round-one suggestions led to stale-dropdown protection and load-time repair of legacy cart shifts on a second serving row; all three reviewers approved these in round two. The actual production assignment and UI methods were exercised with 100 employees per vehicle.

Final validation: 2,016 Core tests passed, one opt-in soak skipped; 31 roster and bridge-parity Python tests passed; seven packaging tests passed. Official SDK builds succeeded for Windows and Mac. The reviewed gameplay source is commit `1ea6cdd`; subsequent changes contain release documentation/evidence only.

## Native results

- Added 12 employees to one truck and three to one cart through native BizMan dropdown callbacks. Both headers reported the assigned count separately from their two/one simultaneous limit. Add employee remained available. Replaced and removed the truck's twelfth employee, then added them again.
- Native Schedule displayed two truck serving rows and one cart row. The truck's native Auto-fill confirmation generated a week rotating six employees across two rows. Cart auto scheduling rotated two employees across its single row. No hourly staffing count exceeded capacity.
- Manually configured Monday as three successive two-hour shifts, 11–13, 13–15 and 15–17: six truck staff and three cart staff. The week's other schedules included different weekday/weekend employees. The live scheduling handler refused a third overlapping truck worker, an overlap on an occupied truck row, a second overlapping cart worker and an explicit cart serving2 shift. All refusals preserved the existing schedule.
- Saved and reloaded the entire world. Both rosters and every shift were identical. Removing a later employee after reload removed only that employee and their shifts; all others retained their hours. Reassigned that employee and restored their shifts afterwards.
- At 10:55 neither vehicle had anyone serving. Truck crew opened and sold during the first shift. The cart was opened through its native vehicle action on Monday; its scheduled crew then served while the player was away. Both vehicles served at 13:05 and 15:05. Native window actors changed from Angeline/Araceli to Cynthia/Della on the truck and from Keith to Patrick on the cart. At 17:05 both had nobody serving; the crew-opened truck closed, while the player-opened cart remained open with no worker, as designed.
- Midnight paid exactly nine scheduled employees once, $38.89 each for two hours, with no wage for the seven other hired employees. The underlying native hourly wage has more precision than the bridge's rounded $19.45 display. Vehicle books matched: truck $233.34, cart $116.67. Sales were $606.00 and $337.50 respectively.
- On Tuesday both vehicles automatically opened with their new scheduled staff: Christopher/Cynthia on the truck and Michael on the cart. Runtime window summaries reported two truck workers and one cart worker.
- No FoodTrucks exception/failure was found in the session log. Other installed mods emitted existing diagnostics, including missing Bakery bundles; this was not a globally clean game log.

The rig's first Monday observation at 11:05 was too early to confirm service: the truck opened at 11:09 after load settlement, and the nearby cart was opened manually at 11:35. Tuesday's independent automatic opening at 11:01 was confirmed for both. These observations are retained rather than treating the first attempted check as a pass.

## Evidence and limits

`native-verification.json` retains build identities, both review rounds, native rosters/shifts, reload comparison, service snapshots and payroll. `native-crew-log.txt` retains the native auto-fill, window-capacity and crew-actor lines. Full transcripts, raw replies, SDK/test logs and screenshots remain in the local `build/crew-roster-review/` folder.

The background rig supports actual native UI callbacks/tree inspection and world-camera screenshots, but full game-frame capture returned a null-texture error. The 12-row crew panel fit the rig's large viewport and retained its ScrollRect/content layout. Manual mouse scrolling and visual layout at other resolutions remain unverified. This is not a claim of interactive visual UI acceptance. The stale-dropdown race and legacy cart save repair have automated production-method/Core coverage; they were not forced in the native session.

The private rig used its established port/profile adaptation of FoodTrucks.dll and retained its installed shared bridge-control assembly. Core and gameplay source matched the release; game assemblies were unchanged. The original private package and complete manifest are restored after testing. No ordinary player save was loaded or modified, and no Workshop publication was performed.

## Installation and release preparation

Installed the 19-file release package into the normal Windows ModsLocal/FoodTrucks folder on 22 October 2026 while the game was closed. Independently compared every installed SHA-256 and the exact file set with the package manifest. The previous package was retained in FoodTrucksPackageBackups/FoodTrucks-20261022-183557. The enabled Workshop manifest still excludes Food Trucks item 3822355169. Normal-game ownership was released afterwards. No Windows save was opened.

Prepared `FoodTrucks-0.10.4-rc.zip`; verified the archive CRCs, exact entries and every decompressed file hash. `installation.json`, `package.sha256` and `archive.sha256` retain the receipt. Only the packaged README changed after native testing; runtime, Core, data and bundles are unchanged. Release notes are prepared in `release-notes.bbcode.txt`. This candidate has not been published.
