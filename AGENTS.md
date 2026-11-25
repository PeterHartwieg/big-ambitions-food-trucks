# Food Trucks mod for Big Ambitions

A Workshop mod that adds food carts and trucks as an early-game business. Read `docs/design.md` first: it is the register of decisions still in force and the game constraints behind them. `CHECKS.md` is the current in-game acceptance checklist. `docs/test-bridge.md` covers the test bridge, the rigs and the playthrough and scenario tools. `docs/vendor-api.md` is the public VendorApi v1 contract for other mods. `CLAUDE.local.md`, if present, has the machine setup and is never committed.

## Layout

- `Core/`: pure C#, netstandard2.1. No Unity and no game types. Newtonsoft.Json is allowed because the game ships it. Builds and tests on any machine with `dotnet test`.
- `tests/`: xUnit tests for Core.
- `unity/FoodTrucks/`: the mod folder for the official SDK (`Assets/Mods/FoodTrucks` once linked). Game adapters, Harmony patches and the runtime live here, because the SDK compiles this folder against the game DLLs.
- `data/`: tuning tables shipped with the mod. Exact prices, IDs and weights live here, not in the docs.
- `tools/`: build, install, package and test scripts. `tools/balance/README.md` documents the balance calculator.
- `art/`: Blender sources and exported models. `art/README.md` has the pipeline and the measured game constraints.

## Core verification

- `dotnet test tests/FoodTrucks.Core.Tests.csproj` runs the Core suite, including the short soak: 20 fixed seeds for 60 days each (`--filter Category=Soak` runs only that).
- `FT_SOAK_LONG=1 dotnet test tests/FoodTrucks.Core.Tests.csproj --filter Category=SoakLong` runs seeds 0 to 199 for 365 days each. Without `FT_SOAK_LONG=1` the long test reports skipped. Add `FT_SOAK_SEED=<non-negative Int32>` to replay one seed. Failing seeds, timings and coverage are in the test output.
- The soak covers Core only: ledger, routes, stock, payroll and save round trips, with lag injected across midnight. Unity, native interaction and the game's real save serialization are outside it and need `CHECKS.md`.
- `python3 -m unittest discover -s tools/tests` runs the tools tests, including the comment and markdown bans.

## Release

- Build with the .NET SDK, Unity 2022.3.62f2 and the official Big Ambitions SDK, for Windows and macOS: `dotnet test`, then `tools/build-mod.sh` (add `--generate` when regenerating assets).
- Package with `python3 tools/package-release.py --output build/FoodTrucks-<version>-rc.zip`. The packager reads the version from `unity/FoodTrucks/ModManifest.asset` and checks the built SHA-256 manifest. Re-verify with `dotnet run --project tools/release/VerifyRelease.csproj -c Release -- build/FoodTrucks unity/FoodTrucks/ModManifest.asset`.
- Upload with the game's in-game SDK uploader to the existing Workshop item `3822355169` (title "HART. Street Food"; folder and mod ID stay `FoodTrucks`). The thumbnail must be under 1 MB. See the [SDK upload instructions](https://github.com/hovgaardgames/bigambitions#5-upload-your-mod-in-game).
- The current player-facing change note is `workshop/CHANGENOTE-<version>.md`, pasted by hand. `docs/release/0.10.0/publication.json` and the BBCode files next to it record the first publication. Version-specific evidence goes in `CHECKS.md`, the PR, or a receipt file under `docs/release/<version>/`.
- The HART. wordmark and brand art under `art/workshop/brand/` keep their existing rights. The MIT licence does not cover them.

## Local fallback testing

When the Windows PC is unavailable, the sibling Driven Rides checkout provides a manually launched Mac rig: read `../hart-self-driving/tools/agent-tests/MAC.md` (repository `PeterHartwieg/hart-self-driving`), resolved next to the main checkout (from a worktree, check the path exists). Set `BA_BRIDGE_URL=http://127.0.0.1:28330` for bridge clients, acquire the separate `agentmac` lock with its local launcher, and stop/release after testing. No tunnel or schedule is needed. Confirm its installed mod snapshot includes the code under test. Lease, restore and release steps for all rigs are in `docs/test-bridge.md`.

## Rules

- Never commit game DLLs, decompiled game code, Harmony binaries or save files. Build scripts fetch Harmony from NuGet with a pinned checksum.
- Core decides, the game only shows. Keep game calls in thin adapters.
- Each Harmony patch logs one `Active` or `Not active` line at startup, with the method name.
- Mod state written into a save must stay loadable: version every schema, migrate forward, and never overwrite data written by a newer mod version.
- Mark evidence in `README.md` and `CHECKS.md` as checked (seen in game, with date, rig and build), pending or unverified. Code reading is not a check.
- Neutral names: no references to any YouTube series or personal saves in shipped files.
- No code comments. Fix the problem, or open an issue, instead of leaving an excuse in the code. `tools/comment_ban.py` (run by the tools tests) fails on any comment that is not a directive or listed in `docs/comment-excuses.md`.
- No new markdown files unless Peter explicitly asks for one (a plan or a review); put status in the PR description or the issue. `tools/md_ban.py` (run by the tools tests) fails on new `.md`/`.mdx` paths unless the PR has the `docs-approved` label.
