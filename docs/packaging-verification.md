# Packaging and installer verification

The build copies every top-level `data/*.json` into the runtime folder.
`tools/package-release.py` derives its required data inventory from that same
repository directory, not from the candidate folder or its checksum manifest.
Add runtime tables there; all must be present and nonempty in release packages.
Packaging refuses an empty repository data inventory, including a missing data directory.
Other runtime files remain explicitly allowlisted, with additional locale JSON
files permitted. Checksums do not authorize arbitrary DLLs, saves or sources.

`tools/Install-Windows.ps1` verifies the source before copying, then verifies the
complete staged file set and SHA-256 digests before backing up the existing
installation. Both checks include hidden files. Manifest names must be relative
forward-slash paths (an initial `./` and SHA-256 binary `*` marker are accepted).
Traversal, case-insensitive duplicates, Windows device names, alternate streams,
wildcards and trailing-dot/space aliases are rejected. Links and reparse points,
including directory junctions, are rejected before traversal. The shared Python
verifier applies the same portable manifest-name restrictions and rejects junctions.
The installer remains a single script so `tools/install-remote.sh` can copy it alone.

Relative installer inputs resolve against PowerShell's current location, including
nonexistent destinations, before checking path components for reparse points.
Staging and backup folders have unique names outside ModsLocal; backups retain a
timestamp prefix for chronological sorting. Verification
failure retains the source, manifest and existing installation. A failed swap
restores the previous installation. The normal bridge lease and game-closed
checks remain in the production entry point.

## Offline checks

Run from the repository root. Keep temporary files inside the worktree:

```powershell
New-Item -ItemType Directory -Force .scratch/tmp | Out-Null
$env:TEMP = (Resolve-Path .scratch/tmp).Path
$env:TMP = $env:TEMP
python -m unittest discover -s tools/tests -p test_package_release.py -v
python -m unittest discover -s tools/tests -p test_windows_install.py -v
python -m unittest discover -s tools/tests -p test_local_mac.py -v
$env:FT_TEST_TRANSPORT = 'inprocess'
python -m unittest discover -s tools/tests
```

The Windows tests invoke the actual installer in a child PowerShell, with a
temporary profile, stubbed bridge lease and process check, and harmless text DLL
fixtures. They never access the real bridge controller or game installation.
Junction and hidden-file tests run natively on Windows; creating file symlinks
requires Developer Mode or elevation and that test skips explicitly when unavailable.
The package tests independently copy the repository's actual data tables, check
ZIP contents, and remove each table in turn with a fresh checksum manifest to
ensure an incomplete build is refused. They also test deterministic archives,
checksum failures, unsafe paths and changes during archiving.

## 28 October 2026 baseline

At `origin/main` revision `f7aaf825edc49640b4b86932d7b1a5c3ee1b790c`, issue #16's
original omission was already fixed by `4648a53` ("Package the vehicle diagram
table and use the current release version"). An offline fixture containing all
seven repository data tables successfully packaged with the origin/main script.
Issue #17 still reproduced: the actual Windows installer accepted an unlisted
`unexpected.dll` and reported a verified installation. The new regression test
failed on that original installer and passes with exact inventory validation.

These are tooling checks, not SDK-build or in-game evidence. Dry packaging uses
synthetic binaries and bundles and must not be installed or released.
