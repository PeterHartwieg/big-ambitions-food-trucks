# Commissary address: Windows native evidence, 28 October 2026

**PASS for the address fix, with the limits below.** Tested draft PR #23, source `c01090782c5837295df672e9cb22ded1a1fc307f`, in the isolated agent1 rig. No mod code, locales, data or tests changed; neither package was rebuilt.

| Check | RED: Workshop 0.10.9 | GREEN: PR #23 | Evidence |
| --- | --- | --- | --- |
| CA1 | Industry City and Lower Manhattan contact headers had no address or View on map. | Existing contacts show 2 21st Street / 5 12th Street. Closed-map and already-open-map actions select the matching building card. Switching to addressless Street Food Bookings clears the address and map button; returning restores the Industry City target. Industry City called successfully at 23:00. | [Before](CA1-before-industry-contact.jpg), [after](CA1-after-industry-contact.jpg), [already-open map/card](CA1-after-industry-map-already-open-card.jpg), [night call](CA4-after-night-call-rental-text.jpg) |
| CA2 | Rented-space panels at both sites had no address or commissary Set destination. | Industry City shows its address, bay note and Set destination. The native destination confirmation names 2 21st Street. At the actual adjacent bay, supplier delivery, fitting a Hotdog Grill and loading 20 hotdogs succeeded; 980 remained in shared stock. Physical driving was not tested. | [Before](CA2-before-industry-bizman.jpg), [after](CA2-after-industry-bizman.jpg); native GPS/stock receipts retained locally |
| CA3 | The two original sites reproduced the missing address controls; the other three were not run on RED. | All five sites show their own address in contacts and BizMan, select their own map card and produce the matching destination confirmation. Before/after snapshots around each site's UI actions match for money, commissary state (including rent, orders and stock), ledger and transactions. Rental/move fixture preparation is outside those comparisons. | [New Garment District contact](CA4-after-new-visit-welcome.jpg); all five native UI/state receipts and screenshots retained locally |
| CA4 | Original welcome, rental and move history names sites without street addresses. | Save/reload retains address/map/GPS controls, two spaces and stock (20 onboard / 980 shared). New call, rental, move and welcome texts name addresses. Old history still reads. A separate approach to previously locked Garment District triggers a native `visited` unlock and welcome at 51 2nd Street. | [Reloaded BizMan](CA4-after-reload-verified-bizman.jpg), [new welcome](CA4-after-new-visit-welcome.jpg), [rental and call](CA4-after-night-call-rental-text.jpg) |
| CA5 | Industry City and Lower Manhattan both draw purple commissary markers with the filter enabled. | Both markers still draw. The Industry City marker defect was **not reproduced**; no separate issue established. | [Industry City before](CA5-before-industry-marker.jpg), [Industry City after](CA5-after-industry-marker.jpg), [Lower Manhattan after](CA5-after-lower-marker.jpg) |

All five resolved native addresses: Industry City **2 21st Street**; Lower Manhattan **5 12th Street**; Garment District **51 2nd Street**; Hell's Kitchen **8 2nd Avenue**; Murray Hill **12 5th Street**.

## Packages and save provenance

| Package | SHA-256 |
| --- | --- |
| BEFORE `FoodTrucks-0.10.9.zip` | `080f5cc068a8a9ac307b9d55b0aeca09e63dfac68b21b433038b925d88defaef` |
| AFTER `FoodTrucks-0.10.9-commissary-c010907.zip` | `8b728c215bb10adb3c552e54d75292a7819fd1ececa231db60c0101f6cc6dd55` |

Both ZIP hashes, CRCs and all 19 staged/installed file hashes verified. Installed FoodTrucks DLL hashes were `d5d096fadad9d019c58fdc2cf3ef9cede3b53cf8e456798ec9217a0273c377b9` (RED) and `398bc0cdac222df4f223942918ee9af786b5fad1449b56e952093bdae32458fa` (GREEN).

RED created disposable character FT Playtest and `FT Commissary Address PR23 Red Baseline`, with Industry City rented and Lower Manhattan history. GREEN loaded that exact pre-change save; its retained copy and live file hashes matched before GREEN started. Subsequent fixtures used only that rig character. Saves remain outside Git.

## Runtime logs

`python tools/check-log <retained Player-agent-after.log> 0.10.9` exited **0**: **115 active patches, 0 inactive patches, 0 mod errors, 0 FoodTrucks exceptions**. There is no version-specific 0.10.9 rule file; these are the repository's always-on checks.

Representative native lines:

```text
[Mod:FoodTrucks] Patch ContactsApp.SetUpHeader(Contact): Active.
[Mod:FoodTrucks] Map filters: Street food line with 8 filters.
[Mod:FoodTrucks] Commissary industry-city: View on map, ba:street_twentyfirststreet#2.
[Mod:FoodTrucks] Commissary industry-city: Set destination, ba:street_twentyfirststreet#2.
[Mod:FoodTrucks] Commissary garment-district: unlocked on day 1 (visited); its contact added.
```

## Lease and restoration

This second attempt waited from **17:03:40 UTC** until acquisition at **19:30:41 UTC**, polling every three minutes. It observed the Mod Pack, issue #15, Persistent City rerun and issue #14 sessions in turn; none was taken over. The earlier two-hour blocked attempt remains separately recorded.

Restored at **19:51:44 UTC**: all **4,828** original profile file hashes and **107** receipt references verified, together with `agent-install.json`, `run.json`, original Player-agent.log and registry preference export. Original receipt SHA-256: `9785d1de5c7360875b2a2a47f511815660dd98a38d6f64ab4ecff8c20627b949`. The tested profile was retained outside the active rig profile. Lease released at **19:51:56 UTC**, unheld with zero pending operations; the release observation found no Big Ambitions process. Normal installation/profile/saves/ModsLocal and Steam were untouched.

## Limits and retained evidence

- These are native game screenshots and UI callbacks through the existing bridge, not harness stand-ins. Map views were centered by moving the disposable player and opening Voogle Maps, or through View on map; mouse panning was not exercised.
- Physical driving remains **unverified**. The bridge's travel command simulates a journey; it was not used as driving evidence. Bay arrival used rig teleport tools, followed by actual delivery/fitting/transfer operations.
- The 23:00 call checks nighttime availability at the vacant Industry City host; it does not establish every possible occupied tenant's opening-hour configuration.
- A city reload while the phone had been open left stale bridge/menu state. Closing and reopening the phone recovered it; the repeated contact, map/card and BizMan/GPS checks passed. Initial failed navigation captures are excluded from this evidence set. Expected fixture refusals (unfitted cargo, paused Park, cramped cart spawn) were resolved before acceptance evidence; the complete final log still passes.
- The committed images are resized JPEGs, each under 400 KB. Original 1920×1080 PNGs are under `C:\Users\Peter\HART-Builds\screenshots\commissary-address-2026-10-28\`.
- Raw UI replies, state comparisons, package receipts, logs, screenshot hashes, backups and the retained tested profile are under `C:\Users\Peter\HART-Builds\street-food-commissary-address\rig\`. Main local report: `C:\Users\Peter\Coding_Projects\.agent-logs\streetfood-commissary-address-rig.md`.
