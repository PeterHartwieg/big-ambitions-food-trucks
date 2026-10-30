The Windows installer previously copied the entire source but hashed only manifest entries, allowing unlisted files to be installed as verified. It now checks exact source and staged inventories before the swap, rejects unsafe/duplicate paths and reparse points, and preserves rollback.

Release packaging derives required JSON tables from the same repository data/*.json inventory the build copies. Independent regression coverage checks every shipped table is archived and every missing table is refused, even with regenerated checksums. Shared Python verification also rejects Windows path aliases and junctions.

Refs #16, Refs #17. On origin/main f7aaf825, #16 was already fixed by 4648a53; an offline packaging fixture confirmed this. #17 reproduced with the actual Windows installer and harmless text DLL fixtures before the fix.

Initial implementation validation:
- Package suite: 8 passed, including deterministic ZIPs, table inventory, tampering and native Windows junction rejection.
- Windows installer suite: valid/extra/missing/hash mismatch, unsafe and duplicate paths, hidden files, junctions, staged extras and failed-swap restoration; bridge lease/process check stubbed in a disposable profile.
- Mac installer suite: 5 passed, 1 file-symlink privilege skip on Windows.
- Dry CLI packaging twice under .scratch: identical ZIPs, all seven actual JSON tables included; binaries/bundles synthetic and not installable.
- git diff --check passed.

No gameplay source, version changes, SDK build, game installation, rig access or release. Open questions: none for implementation; broader suite environment limitations are recorded in the handoff.


Full tooling run (FT_TEST_TRANSPORT=inprocess): 215 tests, 22 errors, 8 skips. All 10 Windows installer tests passed. Remaining errors: 18 missing-dotnet harness errors, two hardcoded /private/tmp cwd errors, one Windows pipe-selector error, and one cleared-environment home-directory error. No unrelated gameplay/bridge test changes were made. Full local log: .scratch/tooling-final.log.



Review round 1 updates:
- Relative installer inputs resolve against PowerShell's current location, including nonexistent destinations; checks inspect the resolved manifest path before reading it.
- Backup names retain timestamps plus GUIDs. Empty repository data inventories now fail packaging.
- Current scoped checks: 14 Windows installer tests passed; 9 package tests passed; 5 Mac installer tests passed with one existing Windows file-symlink privilege skip. Two dry CLI archives were identical and included all seven data tables. Regression failures were observed before fixes. Full unrelated suite not rerun in this round; original environment limitations above still apply.
- Commits: 6ee38df, 6ec4aea. No product questions. PR remains draft.

