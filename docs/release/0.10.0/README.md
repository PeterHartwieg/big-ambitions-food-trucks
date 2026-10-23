# Food Trucks 0.10.0 Workshop release kit

**Pending update, 21 October 2026:** the description, player guide, announcement draft and gallery source now describe 19 products and a soda-only cooler after removal of the factory water ingredient. These edits have not been published. The original release archives, gallery images, hashes and publication evidence below remain the record of 0.10.0; regenerate the menu gallery and package the updated runtime for the next upload.

Prepared 18 October 2026 from runtime commit **4a68189086831aac34d201920964877315f05af2**. Peter accepted the mod's functionality and save safety after playtests. This preparation changes release documentation, artwork and packaging only.

**Status: published as [HART. Street Food](https://steamcommunity.com/sharedfiles/filedetails/?id=3822355169).** Peter completed the initial upload. The full description, five gallery images in order, and initial 0.10.0 change notes are live and checked on the Workshop page. The HART. thumbnail is displayed correctly. The announcement remains an unsent draft.

**Windows handoff completed:** the HART. thumbnail is installed at `ModsLocal\FoodTrucks\thumbnail.jpg`. The complete release kit is on Peter's desktop in `Food Trucks 0.10.0 Pre-release`, with `START-HERE.txt`, `Preview.html`, artwork, paste-ready Workshop text and the prepared mod. All 40 delivered files were checked against SHA-256 hashes. Peter completed the first upload. The installed runtime was preserved: its DLL matches the prepared release apart from the PE build timestamp and module identifier. Receipt: `build/workshop-0.10.0/pc-handoff-receipt.json`.

## Ready to use

| Material | File |
| --- | --- |
| Workshop title and publication record | [publication.json](publication.json) |
| Workshop description | [Readable copy](workshop-description.md), [paste-ready Steam BBCode](workshop-description.bbcode.txt) |
| Player guide | [player-guide.md](player-guide.md) |
| Bug and balance discussion templates | [feedback.md](feedback.md) |
| Initial change notes | [release-notes.md](release-notes.md), [Steam BBCode](release-notes.bbcode.txt) |
| Viewer announcement draft | [announcement.txt](announcement.txt); the published Workshop URL is filled in |
| Thumbnail | [00-thumbnail.jpg](../../../art/workshop/0.10.0/00-thumbnail.jpg), 1024 × 1024, 170,095 bytes |
| Gallery | [01 Progression](../../../art/workshop/0.10.0/01-progression.jpg), [02 Menu](../../../art/workshop/0.10.0/02-menu.jpg), [03 New foods](../../../art/workshop/0.10.0/03-new-foods.jpg), [04 Working day](../../../art/workshop/0.10.0/04-working-day.jpg), [05 Fleet](../../../art/workshop/0.10.0/05-fleet.jpg); each 1920 × 1080 |
| Local visual preview | `build/workshop-0.10.0/preview.html` |
| Verified runtime archive | `build/FoodTrucks-0.10.0-prerelease-4a68189.zip` |
| Ready upload folder | `build/workshop-0.10.0/upload/FoodTrucks/` (runtime plus `thumbnail.jpg`) |

Runtime archive: **54,420,062 bytes**, SHA-256 `a920d652f84cb3b28acd6a00656e95570d61f136ecebc0861a19dad2544371f9`.

## Visual provenance

All promotional renders use the actual repository FBX models and their shipped base-colour, mask and normal textures. Vehicle equipment mounts match the Unity generators. The four new food renders use the same source models used for the shipped item icons, including `CornCob` for grilled corn. The gallery labels these as renders of the mod's models; they are not screenshots of gameplay.

[Model source hashes](../../../art/workshop/0.10.0/model-sources.json) identify the inputs. No model or texture was altered. Rebuild the transparent assets with `tools/release/render-models.py` through Blender, then lay out the gallery with `tools/release/compose-gallery.cjs` through Node and Sharp. The editable SVG layouts are in `build/workshop-0.10.0/`.

The thumbnail and all five gallery cards carry the original [HART. wordmark](../../../art/workshop/brand/hart-wordmark-on-light.svg), copied unchanged from the HART brand kit. Its outlined lettering, green dot, proportions and clear space are preserved. The Workshop description credits this as a HART. mod by Peter Hartwieg. HART artwork retains its existing rights; the mod's MIT licence does not grant rights to the brand.

## Checks completed for this preparation

- The official SDK built the latest runtime sources for Windows and Mac successfully.
- Both bundle manifests and the exact package file set passed verification.
- Every archived file was independently read back and its SHA-256 compared with the runtime manifest; the upload stage matches those bytes plus the thumbnail. Details: `build/workshop-0.10.0/verification.json`.
- The archive includes all seven data tables, including the vehicle diagrams. The packager now derives its default archive version from ModManifest rather than the old hard-coded 0.9.10 value.
- Seven focused packaging tests passed, covering file hashes, determinism, missing requirements, forbidden game DLLs/source/save files, unsafe paths and existing-output protection.
- All six artwork files were inspected for layout, legibility and the correct models. Thumbnail is below the SDK's 1 MB limit.
- Source inspection confirms 20 products without Bakery and five optional Bakery products, two cart equipment slots and four truck slots. Those figures match the listing and gallery.
- No gameplay code, tuning or source model changes were made. Earlier checked/pending/unverified README evidence is preserved in [the historical archive](../../evidence/readme-history-before-0.10.0.md).

Build log: `build/workshop-0.10.0/build.log`. SDK log: `build/logs/build.log`. Runtime manifest: `build/FoodTrucks.sha256`. Upload staging manifest: `build/workshop-0.10.0/upload.sha256`. The runtime compiled with existing duplicate-type warnings from the SDK import/compilation environment; the SDK reported success.

## Follow-up after publication

1. Confirm the Steam-downloaded copy's version and files, with the duplicate local install removed from the load path during that check. This distribution check remains pending.
2. The bug and balance discussion starters remain prepared drafts; post them when requested.
3. The announcement now contains the Workshop URL and remains unsent.
4. Update this existing Workshop item for future releases. Item ID: `3822355169`.

Live listing evidence: `build/workshop-0.10.0/workshop-live.png` and `workshop-live-full.png`. The published title is **HART. Street Food**; the package folder and internal mod identifier remain **FoodTrucks**.

The in-game upload route and 1 MB thumbnail limit follow the [official Big Ambitions SDK instructions](https://github.com/hovgaardgames/bigambitions#5-upload-your-mod-in-game). General publishing behaviour is documented in the [Steam Workshop implementation guide](https://partner.steamgames.com/doc/features/workshop/implementation); the public default above was also checked against the installed game version's uploader implementation.
