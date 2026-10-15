# Autonomous audit and release handoff — 14 October 2026

The current review candidate is **`021fd34`**, built through Unity 2022.3.62f2 and the official SDK for Windows and Mac. Rendered fleet/detail/dropdown checks, actual crew changes, logistics persistence, short selling and safe uninstall passed. Full live validation and Workshop readiness remain **pending**. No release, merge or Workshop publication is recorded here.

## Candidate and review scope

The review base is `4535c83`, published as `codex/release-review-base-2026-10-14`. Local `main` was 383 commits ahead of its remote and was left untouched. Twelve focused PRs remain available for review. PR 10 stacks on PR 4; PR 12 stacks on PR 6.

| PR | Change | Evidence |
|---|---|---|
| [1](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/1) | Complete city purchases after a booked payment listener throws | Five isolated scenarios using actual Charge/Payments sources passed |
| [2](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/2) | Restore warehouse stock after interrupted logistics withdrawal | Four isolated scenarios using the actual withdrawal method passed |
| [3](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/3) | Preserve pending bridge operation recovery handles | Regression passed; live recovery check pending |
| [4](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/4) | Windows/Mac bundles and verified local Mac installation | Both bundles built; local installer and game load checked |
| [5](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/5) | Two park premium pitches | Legal truck parking/hatch rule checked at both; claims/queues/presentation pending; Industry City addition removed after failed placements |
| [6](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/6) | Dedicated scrolling Street food page in BizMan | Latest Windows/Mac build and 1280×720 fleet typography checked; native interaction/resize checks pending |
| [7](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/7) | Gate developer cheats behind the test launch flag | Compiled/reviewed; normal-launch check pending |
| [8](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/8) | Reject browser-origin test bridge mutations | HTTP-handler tests passed; live requests returned 403 / 403 / 415 as intended |
| [9](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/9) | Current README, player starting instructions and this handoff | Documentation review; historical evidence retained |
| [10](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/10) | Deterministic verified release-candidate packaging, stacked on PR 4 | Archive produced and independently verified; independent code review found no concrete defect |
| [11](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/11) | Read native TMP and Unity UI Text labels through the bridge | Actual-helper harness and independent review passed; labels checked in rendered game |
| [12](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/12) | Compact readable vehicle details and scoped dropdown option templates | Rendered layout/popup text and actual UI crew transfer checked; native drag/resize pending |

**Checked latest packaging:** `build/FoodTrucks-0.9.10-rc-021fd34.zip` is 47,696,407 bytes. All 17 entries were independently checked against their SHA-256 values. Archive SHA-256: `715b838385651820c75b6cfba1dea885b24f644fbca77ad90779ae06a19506aa`. Superseded archives were removed to recover disk space; this candidate is retained. It is a local release candidate, not a Workshop upload.

## Automated and build results

- **Checked:** 1,812 ordinary Core tests passed. The separate full Debug long soak also passed: 200 seeds × 365 days (73,000 simulated days), zero failures.
- **Checked:** 112 integrated Python tool tests passed in 33.422 seconds, including release packaging tests (the earlier pre-packaging run passed 106).
- **Checked:** the integrated candidate built through Unity 2022.3.62f2 and the official SDK, producing Windows and Mac asset bundles.
- **Limit:** these results do not establish rendering, player-facing navigation or clean Workshop installation.

## Live Mac evidence

Game build 3682, Steam build 25482473. The final rendered session was `034369fd`, using `021fd34` and the new audit save. Earlier fresh-process session `70663076` loaded `FT audit logistics 20261014` at day 4, 13:01, paused, and verified persistence.

- **Rendered UI checked:** readable 16px fleet body text, 24px title, proportionate controls, vertically overflowing cards, compact vehicle details and readable long employee options in the actual popup. Fleet → detail → Back navigation passed. Actual UI crew transfer passed: removing Rhonda Walker from a three-person crew reduced it to two; assigning her to another truck produced a one-person crew. Those crew tests were not saved. Screenshots: `build/shots/final-fleet/final-fleet-game.png`, `build/shots/polished-detail/polished-detail-game.png`, `build/shots/final-popup/final-popup-game.png`.
- **Logistics conservation and persistence checked:** warehouse 200 / commissary 40 became 160 / 80, and repeating delivery changed neither. Fresh-process reload retained warehouse 160, commissary 80, target 80 and logistics manager Ann Oliva (`a9uvEjNSkicCUMKJhKtGw==`), skill 60, maximum three destinations.
- **Short selling checked:** 13:01 → 13:11 reduced Chopped Cheese 69 → 68 and Corn 79 → 78. Ledger and crew takings each rose $417 → $436; cash stayed $11,278.70 because crew revenue waits for payout.
- **Safe uninstall checked:** the first confirmation kept all four vehicles and cash unchanged. The second removed all four, paid $436 crew takings + $188.80 commissary stock + $67,299.50 vehicles/cargo = $67,924.30, producing cash $79,203.00. Repeating the double-confirmed uninstall paid nothing extra. Spaces, orders and commissary stock were empty; one logistics destination and four contacts were removed; unpaid refunds were zero. New save `FT audit uninstall 20261014` compressed successfully (551,722 bytes). Loading it without the mod remains pending.
- **Park scope checked in part:** legal truck parking and the hatch rule passed at Eleventh Street 23 (Lower Manhattan) and Second Avenue 5 (Hell's Kitchen). The proposed Industry City addition failed the hatch rule at every tested legal position; other candidates were blocked. It was removed, leaving two new pitches and 19 total. Automatic Industry City park demand and all original pitches remain. Premium claims, queues, approach/presentation and first-come checks remain pending.
- **Bridge HTTP guards checked live:** expected 403 / 403 / 415 responses are recorded in `headless-http-guards.json`.

## Limits and restoration

An initial rendered run stopped responding after a BizMan click before any dedicated-page build line. Its sample showed a native Unity wait amid less than 400 MiB free disk and about 19 GiB swap; an older BigAmbitionsMCP mod also logged `MissingFieldException` for `GameInstance.NetWorth`. Cause was not proven. Later clean rendered runs passed the UI checks above. A headless city-to-city reload also remained pending before the mod's city-load callback; it was stopped before scenario fixture/save mutation. No `WaitForEndOfFrame` cause was established. Native wheel/scrollbar/drag and resize checks were blocked by the locked Mac.

**Final cleanup checked (`final-cleanup.json`):** the game process is gone. BigAmbitionsMCP and BigCopilotLink were restored to `ModsLocal`; the Food Trucks candidate is parked outside it at `persistent/FoodTrucks-test-isolation-20261014/FoodTrucks-candidate`. All 37 original save files are unchanged; eight new files belong to the two separate audit saves. Backed-up PlayerSettings and Steam manifest are byte-identical. SDK symlinks point back to `main`, Unity build HDRP settings are restored, and the SDK's preexisting deleted example metadata remains as it was. The main checkout retains only the user's unchanged untracked `docs/announcement-video.md`. Steam shutdown is confirmed; no Steam, game or Unity process remains. Only the current `021fd34` release archive is retained.

Local, untracked evidence is under `build/logs/game/mac-release-2026-10-14/`, including first-run log/sample/state, `headless-http-guards.json`, final uninstall/save JSON, selling before/after state and `final-cleanup.json`. Build/test logs and screenshots remain under `build/`. Raw logs, saves and game data are not included in this tracked handoff.

## Follow-up review rounds — 15 October 2026

**Checked:** the combined source candidate `809d75b` (base `4535c83`) completed three CLI review rounds with GPT 6.1 Sol and GPT 6 Astra, and two rounds in one persistent Claude Opus 5.5 session at medium effort. All three final verdicts report **no actionable findings**. This is source review, not completion of the live or distribution gates below. The packaged runtime remains `021fd34`; later changes affect evidence and build/package tools only.

The first passes missed two actionable P2 comments already present on PR 10. The coordinator reproduced/adjudicated them and assigned a separate Sol CLI worker to each:

- **Harmony notice recovery:** keeping the cached DLL while cleaning the notice could produce a build without its mandatory notice. The build now recovers both from the checksum-pinned package when either is missing, and the final notice copy must succeed.
- **Archive/source consistency:** replacing source bytes and their manifest together after archive streaming could pass final verification against different hashes. Packaging now requires the final mapping to equal the initial one before retaining the archive.

Both regression checks failed before their fixes. Six offline shell-harness cases cover notice recovery, complete-cache reuse, failed download, checksum rejection and required final copy; another regression replaces both source and manifest and checks that no archive is retained. **115 Python tool tests passed on PR 10; 119 passed on the combined candidate in 38.245 seconds.** The updated packager reproduced the retained 47,696,407-byte ZIP byte for byte, with the same SHA-256 recorded above. No Unity rebuild or new live-game result is claimed for this tooling-only follow-up.

Opus's optional concerns about hypothetical shared dropdown ownership, failure-path stock visuals, custom installer roots, backup retention and minor wording/checklist nits were reviewed in the next round. No concrete serious defect was established; all three reviewers accepted leaving them outside this fix scope. Local review prompts, transcripts and regression logs are retained under `build/review-rounds-2026-10-15/`; no credentials, game source or saves are included in this handoff.

## Remaining release gates

1. **Street food UI:** native wheel/scrollbar and stop dragging, resizing/wrapping, UI state after save/reload and operation without a warehouse. Rendered typography, popup readability, navigation and actual crew selection are checked; CHECKS B1–B8 is the detailed guide.
2. **Park pitches:** premium claiming, first-come behavior, gate approach, queue/hatch presentation and map markers for the two retained additions; broader legacy park/queue presentation remains pending.
3. **Normal launch:** developer shortcuts/commands must be absent without `-foodtrucks-test`; safe uninstall must remain available. Test-mode tools have run, but flag-off behavior is not claimed checked.
4. **Current-package smoke:** short sales, logistics persistence and double-confirmed uninstall passed. Mod-free loading of the uninstall save, remaining licence/equipment/crew-route-restock paths and any unverified money/stock behavior remain. Artificial throwing-listener cases already have focused harness coverage.
5. **Distribution:** local packaging and independent review are complete. The implementation plan's private Workshop upload and clean-install check remain pending before public release. Version selection and publication remain separate actions.

Older checklist sections are historical snapshots. Later session evidence supersedes many bay, collision, shading and sale-sound rows; they do not all need replaying. Preserve checked/pending/unverified labels. Older behavior without newer evidence, including colour persistence, standing-order/restock presentation and catering/queue appearance, remains a validation gap to cover in representative play rather than a known defect. Peter explicitly waived another live check for the two-person staff/hatch change; unaccepted balance proposals are not new release requirements.
