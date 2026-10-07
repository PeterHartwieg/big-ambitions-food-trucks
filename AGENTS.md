# Food Trucks mod for Big Ambitions

A Workshop mod that adds food carts and trucks as an early-game business. Read `docs/implementation-plan.md` first, then `docs/design.md`. `CLAUDE.local.md`, if present, has the machine setup and is never committed.

## Layout

- `Core/`: pure C#, netstandard2.1. No Unity and no game types. Newtonsoft.Json is allowed because the game ships it. Builds and tests on any machine with `dotnet test`.
- `tests/`: xUnit tests for Core.
- `unity/FoodTrucks/`: the mod folder for the official SDK (`Assets/Mods/FoodTrucks` once linked). Game adapters, Harmony patches and the runtime live here, because the SDK compiles this folder against the game DLLs.
- `data/`: tuning tables shipped with the mod.
- `tools/`: build, install and package scripts.
- `CHECKS.md`: the in-game checklist for the current milestone.

## Rules

- Never commit game DLLs, decompiled game code, Harmony binaries or save files. Build scripts fetch Harmony from NuGet with a pinned checksum.
- Core decides, the game only shows. Keep game calls in thin adapters.
- Each Harmony patch logs one `Active` or `Not active` line at startup, with the method name.
- Mod state written into a save must stay loadable: version every schema, migrate forward, and never overwrite data written by a newer mod version.
- Mark evidence in the README as checked (seen in game), pending or unverified. Code reading is not a check.
- Neutral names: no references to any YouTube series or personal saves in shipped files.
