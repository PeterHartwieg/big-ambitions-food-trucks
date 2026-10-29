# Rig virtual mouse (AgentRigInput)

A rig-only assembly that lets the isolated agent test player drive mouse-only UI without OS input or window focus. Commands, coordinates, timing, gating, cleanup, installation and the issue #14 resize cases are in [docs/test-bridge.md, "Rig virtual mouse"](../../docs/test-bridge.md#rig-virtual-mouse).

It is never part of the Food Trucks mod: nothing in `unity/FoodTrucks` references it, `tools/build-mod.sh` does not build it, and the release verifier and packager refuse it. The agent test runtime (`hart-self-driving` `tools/agent-tests`) builds it from `$BA_BRIDGE_TOOLS/rig-input`, installs it into the rig's Managed folder with a hashed receipt, and starts it once the bridge pump runs.

| Path | Contents |
|---|---|
| `Shared/` | Unity-free code shared with the tests: per-frame state and timeline, gesture sequences, argument rules, the interaction engine (ownership, watchdogs, cleanup, after-release barrier), IL call scanning and the call-site transpiler, traces |
| `Runtime/` | Unity side: entry point, gate, bridge adapter (reflection; no compile reference to the mod), legacy shims and their patching, the rig pointer input module, the Input System mouse, UI geometry, Schedule observers, the service and player-loop hooks |
| `build.py`, `build.ps1` | Build against an explicit game `Managed` folder (references only) with the pinned Harmony 2.4.2; write `AgentRigInput.dll` and `rig-input-manifest.json`. Install nothing |
| `tests/` | Offline xUnit tests (`dotnet test tools/rig-input/tests`) |

Status: **unverified** in game. The offline tests cover the state machine, the engine's lifecycle, IL scanning and transpiling of synthetic methods with real Harmony, and the release guard. Harmony patch generation in the Mono player, Unity frame ordering, the EventSystem swap and the Input System device are proven only by a live rig run.
