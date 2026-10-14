# Autonomous audit and release handoff — 14 October 2026

The candidate builds and is ready for review. Workshop readiness and full live validation are **pending**. No release, merge or Workshop publication is recorded here.

## Candidate and review scope

The review base is `4535c83`, published as `codex/release-review-base-2026-10-14`. Local `main` was 383 commits ahead of its remote; it was left untouched. Focused branches and PRs preserve that separation. The compiled runtime candidate is `ee54b96`; later documentation integration was `2ecffc5`. Source integration `e0226fa` includes the release packager; the compiled runtime is unchanged.

| PR | Change | Evidence |
|---|---|---|
| [1](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/1) | Complete city purchases after a booked payment listener throws | Five isolated scenarios using actual Charge/Payments sources passed |
| [2](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/2) | Restore warehouse stock after interrupted logistics withdrawal | Four isolated scenarios using the actual withdrawal method passed |
| [3](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/3) | Preserve pending bridge operation recovery handles | Regression passed; live recovery check pending |
| [4](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/4) | Windows/Mac bundles and verified local Mac installation | Both bundles built; local installer and game load checked |
| [5](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/5) | Three park premium pitches | Data/balance checks passed; legal positions and presentation pending |
| [6](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/6) | Dedicated scrolling Street food page in BizMan | Compiled, independently reviewed; live UI checks pending |
| [7](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/7) | Gate developer cheats behind the test launch flag | Compiled/reviewed; normal-launch check pending |
| [8](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/8) | Reject browser-origin test bridge mutations | HTTP-handler tests passed; live check pending |
| [9](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/9) | Current README, player starting instructions and this handoff | Documentation review; historical evidence retained |
| [10](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/10) | Deterministic verified release-candidate packaging, stacked on PR 4 | Archive produced and independently verified; independent code review found no concrete defect |

**Checked packaging:** `build/FoodTrucks-0.9.10-rc-ee54b96.zip` is 47,695,364 bytes. All 17 entries were independently streamed and checked against their SHA-256 values. The `.zip.sha256` sidecar records archive SHA-256 `88660a8037f8b20ab93d88f34803f0c2ec8b8f56904521f9d1da1141ded9996e`. This is a local release candidate, not a Workshop upload.

## Automated and build results

- **Checked:** 1,812 Core tests passed. A separate long soak is still running; its final result is pending.
- **Checked:** 112 integrated Python tool tests passed in 33.422 seconds, including release packaging tests (the earlier pre-packaging run passed 106).
- **Checked:** the integrated candidate built through Unity 2022.3.62f2 and the official SDK, producing Windows and Mac asset bundles.
- **Limit:** these results do not establish rendering, player-facing navigation or clean Workshop installation.

## Local Mac session

**Checked in game:** game build 3682, Steam build 25482473, bridge session `1bcb7702`; `FT logistics ready` loaded at day 4, 13:01 with four trucks. The runtime resolved 20/20 premium pitches and 5/5 commissaries. Resolution means the runtime found the referenced locations, not that every pitch was visually or legally verified.

**Unknown:** the outcome of clicking BizMan. The bridge stopped responding, including `/health`; no dedicated Street food page-build line appeared. The sample showed a native Unity semaphore wait. Observed system conditions included less than 400 MiB free disk and about 19 GiB swap. An older BigAmbitionsMCP mod also logged `MissingFieldException` for `GameInstance.NetWorth`. The cause was not proven; this is neither a successful UI check nor an established Food Trucks hang defect.

**Checked cleanup:** the game was stopped. Original FT saves, settings and Steam manifest were byte-identical to their backups. Previous local mods were restored; the candidate was parked outside `ModsLocal`.

Local, untracked evidence is under `build/logs/game/mac-release-2026-10-14/`: `Player-first-run.log`, `game-hang-sample.txt`, `initial-state.json`, `ui-initial.json`, `commit.txt` and `FoodTrucks.sha256`. Build and test logs remain under `build/`. Raw logs, saves and game data are not included in this tracked handoff.

## Remaining release gates

1. **Street food UI:** complete current CHECKS B1–B7: rendering and wrapping, wheel/scrollbar access, vehicle details and editing, return navigation, save/reload, and operation with and without a warehouse.
2. **Park pitches:** check all three entrances for legal selling places, park-facing approach, queue/hatch clearance, map markers, first-come behavior and park presence readings.
3. **Normal launch:** developer shortcuts/commands must be absent without `-foodtrucks-test`; safe uninstall must remain available. Test-mode tools must still function.
4. **Current-package smoke:** licence, equipment, stock and selling; a crew route/restock; money/stock conservation; save/reload; safe uninstall and subsequent mod-free loading. The implementation plan calls for uninstall/mod-free loading once before release. Artificial throwing-listener cases already have focused harness coverage.
5. **Distribution:** local packaging and its independent review are complete. The implementation plan's private Workshop upload and clean-install check remain pending before public release. Version selection and publication remain separate actions.

Older checklist sections are historical snapshots. Later session evidence supersedes many bay, collision, shading and sale-sound rows; they do not all need replaying. Preserve checked/pending/unverified labels. Older behavior without newer evidence, including colour persistence, standing-order/restock presentation and catering/queue appearance, remains a validation gap to cover in representative play rather than a known defect. Peter explicitly waived another live check for the two-person staff/hatch change; unaccepted balance proposals are not new release requirements.
