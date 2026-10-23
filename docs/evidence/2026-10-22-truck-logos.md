# Custom truck logos — 22 October 2026

Implemented in the working tree; not published. Open **BizMan → Street food → truck → Branding → Customize logo**. The copied native shop editor supports built-in shapes, imported PNGs, font and three colours. **Logo text** changes the displayed business name independently of the numbered fleet name; **Save logo changes** applies the draft. **Remove logo** restores an unbranded truck. Existing vehicles start unbranded. Custom PNGs use the game's CustomIcons folder, as they do for normal shops; the image files are not embedded in the mod save.

## Implementation

`Core/Fleet/VehicleLogo.cs` validates independently versioned cosmetic data in the optional `VehicleState.Logo` token. Old saves require no new fields; financial schema 2 is unchanged. Unknown/malformed/newer logo tokens survive round-trips untouched and cannot be edited. Tests cover per-vehicle persistence, legacy saves, unknown tokens, invalid text and icon-path traversal.

Three Harmony prefixes apply only to the private `LogoCustomizer` copy. A fourth scopes the native logo generator's display text to a unique private generation key. Ordinary shops retain their methods, registrations and cache entries. The generator renders the native 1024 × 1024 square artwork; temporary generated files are removed after loading. Matching truck designs share a texture within the session. Signs/materials/textures are cleaned up after replacement, removal, despawn or unload.

Both 55 cm signs attach to the sprung body: below the customer serving window and above the driver-side generator service hatch. Thin solid backing reaches the body. A small bundled forward shader draws the native artwork as a modestly illuminated panel, with normal depth testing and no spawned light. Its fixed luminance is calibrated in the native camera; wider night/brightness-setting checks remain pending. Signs follow visibility/layer changes and have no active colliders. No truck FBX or paint texture is replaced. `art/tools/review_truck_logos.py` checks 10,201 surface samples per side against the actual exported mesh. Run it with Blender's `--python-exit-code 1` so an assertion fails the command.

## Verification

- Full Core suite: **2,109 passed, one existing soak test skipped**. Ten new logo cases pass. Logs: `build/truck-logos/core-all.log`, `core-focused.log`.
- Windows and Mac bundles/package build successfully. Final build log: `build/truck-logos/build-verified.log`.
- Both dense Blender placement checks pass: `build/truck-logos/geometry-verified.log`.
- Native Mac controls checked: open the editor from the truck page, select fonts, change text to “Corner Kitchen”, save, reopen after a full game restart, select an imported PNG, change its colour and save, then remove the logo. The native preview showed the imported `ft-logo-test-20261022` shape and selected colour; removal returned the page to “No custom logo” and removed the body sign. The temporary imported PNG was removed after testing.
- Native UI tree checks confirm the toolbar buttons have nonzero size and the native shape grid, colour lists, font chooser, preview and save button are present. Background-mode game-frame capture was unavailable, so this is control/tree evidence rather than a complete desktop UI screenshot review.

Evidence: `build/truck-logos/native-editor-fixed.json`, `native-editor-tree.json`, `native-import.json`, `native-remove.json`, `truck-removed.png`. The test uses a dedicated Save As copy, **FT Truck Logos 20261022**, derived from the earlier isolated cooler test. The original lab save was not overwritten.

Final runtime SHA-256:

| File | SHA-256 |
| --- | --- |
| `FoodTrucks.dll` | `bfba71f3cffbb1c27706615ec5e8cf6df547ce6a8ca47023c79e20f3df7ba26e` |
| `Dependencies/FoodTrucks.Core.dll` | `694bc05be5ed6052b27909af6b6390b7a99804944b0b25d3e6a537e467ba6ea3` |
| Mac bundle | `b2bff1e923dc0e8ab53a332b6f1a33737f70a310eff2249b3ea2aedac1d7c900` |
| Windows bundle | `a20e5dcf51a401afa928f0134ce4b5b31bb209b668d7aee400ab464d286d7e23` |

Final native camera images in session `3fe436ea` confirm readable artwork on the shaded driver side and the customer side with the serving hatch closed and open. The open view also shows the dedicated drinks fridge. Evidence: `build/truck-logos/truck-left.png`, `truck-right.png`, `truck-open.png`, and `native-final-left.json`, `native-final-right.json`, `native-final-open.json`.

The isolated Mac rig was stopped, its original installation restored with **263 file hashes verified**, and its lease released. The final runtime log is retained in `build/truck-logos/native-final.log`. No Windows installation or publication was performed.

Wider driving/visibility lifecycle coverage, Windows-native testing and ordinary-shop regression testing remain pending. Existing missing-bundle errors from other optional mods mean these runs are not evidence of a globally clean game log.
