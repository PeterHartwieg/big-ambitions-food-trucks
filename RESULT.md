# ft-packaging-16-17 handoff

Status: done (scoped fixes and regression tests complete; broader tooling suite has environment/platform errors).

Branch: `codex/packaging-allowlist`

Draft PR: https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/22

## Baseline verification

- Fetched `origin/main`; starting HEAD and origin/main were both `f7aaf825edc49640b4b86932d7b1a5c3ee1b790c`.
- Read both issues and comments with `gh issue view 16 --comments --json number,title,body,comments` and the equivalent for 17.
- #16's exact omission was already fixed by `4648a53`. Executed origin/main's packaging code against synthetic binaries/bundles plus all seven actual repository JSON tables: packaging succeeded, including vehicle-diagrams.json.
- #17 still reproduced. The regression test invoked the actual original Windows installer with a disposable profile and stub bridge lease/process check. It installed an unlisted harmless `unexpected.dll` and returned success. The test failed before the fix, then passed after it.
- The task continued because #17 remained unfixed; the #16 work adds protection against future inventory drift.

## Changes

- Required release data files now derive from repository `data/*.json`, matching the build's source inventory. Candidate contents cannot define their own requirements.
- Regression coverage uses actual data files independently of the allowlist, checks archive contents, and removes every table in turn with fresh checksums to prove incomplete builds fail.
- Windows installer validates exact source and staged inventories and hashes before replacing an install. It rejects hidden extras, missing files, unsafe names, case-insensitive duplicates, links and junctions; uses literal paths and unique staging/backup directories; and retains failed-swap rollback.
- Shared Python verifier rejects Windows path aliases/device names and reparse points before directory traversal.
- Native Windows integration tests cover valid/extra/missing/hash mismatch, malformed/unsafe/duplicate manifests, hidden files, junctions, staging tampering and rollback. No actual game or bridge controller is involved.
- Added `docs/packaging-verification.md`; made the Mac file-symlink test explicitly skip when Windows lacks the required privilege, while exercising junction rejection natively elsewhere.

Commits pushed:

- `3a5cccf` Derive release data requirements from shipped tables and harden verification
- `0fdfdd7` Verify the complete Windows installer payload before swapping packages

## Commands and results

Temporary files were redirected into this worktree's `.scratch/tmp` with TEMP/TMP. No SDK build or game installation was performed.

- `git fetch origin main`, `git rev-parse HEAD origin/main`: baseline verified.
- Initial `python -m unittest discover -s tools/tests -p test_windows_install.py -v`: 1 expected regression failure, 1 pass against the original installer.
- `python -m unittest discover -s tools/tests -p test_package_release.py -v`: 8 passed.
- `python -m unittest discover -s tools/tests -p test_windows_install.py -v`: initial expanded run 9 passed; final full-suite run includes the added hidden-file test, all 10 passed.
- `python -m unittest discover -s tools/tests -p test_local_mac.py -v`: 5 passed, 1 skipped because file-symlink creation needs Windows Developer Mode/elevation (WinError 1314). Native junction tests passed.
- `FT_TEST_TRANSPORT=inprocess python -m unittest discover -s tools/tests -v` (environment set in PowerShell): final run reports 215 tests in 108.959s, 22 errors, 8 skips. Log: `.scratch/tooling-final.log`.
  - 18 errors from harnesses invoking missing `dotnet` (WinError 2).
  - 2 errors from Unix-only `/private/tmp` working directories (WinError 267).
  - 1 reconnect test using a Windows pipe with a socket selector (WinError 10038).
  - 1 bridge-lock test clearing the environment and then failing to determine the Windows home directory.
  - Skips: six shell-build cases without `shasum`, one missing-dotnet case, one file-symlink privilege case. Unrelated tests were left unchanged under the scope constraint.
- `python .scratch/dry-package.py`: executed origin/main packaging and two real CLI calls to `tools/package-release.py --source ... --manifest ... --output ...` under `.scratch/dry-package`. Both new ZIPs are byte-identical; all seven actual data files match archive bytes. SHA-256: `755a781d88e8ab77c5b0f5c02e8adfbc00023a894372df197c555fc8cb88e331`. These contain synthetic binaries/bundles and MUST NOT be installed or released.
- `git diff --check origin/main`: passed.
- `git push -u origin codex/packaging-allowlist`: succeeded; only this branch pushed.
- `gh pr create --draft --base main --head codex/packaging-allowlist ... --body-file .scratch/pr-body.md`: draft PR #22 created. Uses Refs #16 / Refs #17, not closing keywords.

## Open questions and next steps

No implementation questions remain. Review draft PR #22. Run the broader tooling suite on a machine with the .NET SDK and supported Unix test assumptions, or address those platform limitations separately. Real SDK packaging/install validation remains unperformed by design; use the normal release workflow when authorized.

No gameplay/mod source, versions, game/profile/ModsLocal, agent rigs, other worktrees or branches were changed. No release, publication, merge, issue closure or unrelated messages were performed. RESULT.md is intentionally uncommitted; a copy is at `C:\Users\Peter\.codex\handoffs\leftover-2026-10-28\ft-packaging-16-17-RESULT.md`.


## Review round 1

Status: done. Synced branch to origin (already up to date), read all three reviews, and deduplicated 11 findings: 4 FIX / 7 SKIP / 0 QUESTION; HIGH_MEDIUM_FIX: 1. Fixed PowerShell-relative path handling and manifest junction checks, added overlap/destination-junction coverage, restored timestamped unique backups, and reject an empty runtime data inventory. Pushed 6ee38df and 6ec4aea normally. PR #22 remains draft; description updated because behavior changed. No comments posted.

Scoped validation: 14 Windows installer tests passed, 9 package tests passed, 5 Mac installer tests passed with one existing file-symlink privilege skip. New regressions failed before fixes. Two dry CLI archives (.scratch/review-r1-first.zip and second.zip) matched byte-for-byte and included all seven data tables. git diff --check passed. Full unrelated suite not repeated; original environment limitations remain above.

Review handoffs: TRIAGE-food-trucks-22-r1.md and COMMENT-food-trucks-22-r1.md in the requested handoff folder. No QUESTIONS file was needed. Next step: review the commits and optionally post the prepared comment. No game installation, gameplay changes or publication.


## Review round 2

Status: done. Fetched origin and fast-forward check reported already up to date at 6ec4aea. Re-read all three reviews and current code. Triage: 1 Low FIX, 2 SKIP, 0 QUESTION; HIGH_MEDIUM_FIX: 0. Strengthened overlap rejection regression to require its diagnostic while keeping preservation checks. Committed and pushed ba18835, "Assert the installer overlap rejection reason (review round 2)".

Validation: all 14 Windows installer tests passed in 14.294s; git diff --check passed. Test-only change; production behavior unchanged. PR #22 remains draft; description intentionally unchanged. No comments posted. Requested TRIAGE-food-trucks-22-r2.md and COMMENT-food-trucks-22-r2.md are in the handoff folder. No QUESTIONS file needed. Next step: review the test-only commit and optionally post the prepared comment.
