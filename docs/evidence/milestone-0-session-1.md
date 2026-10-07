# Food Trucks 0.0.1-spike — manual test results

Date: 2026-10-07 (Europe/Berlin). Peter operated the game; Codex recorded user reports, screenshots and local logs. Build identity is confirmed by in-game status and logs. No fixes were attempted.

**Finished:** Big Ambitions process count was verified zero. Phase D log was copied before parking FoodTrucks. Final folder checks: `ModsLocal/FoodTrucks` absent; `FoodTrucksParked/FoodTrucks` present. All saves were retained. Only the FoodTrucks folder was moved; no other mod folder, code or settings were changed by Codex.

The automated attempt failed at screenshot capture with `SetIsBorderRequired failed: Element not found. (0x80070490)` on Steam, Big Ambitions and Notepad, including after restarting Codex. Gameplay was therefore tested manually. Later hotkey difficulty was resolved by Peter identifying unusual Ctrl+Fn keyboard switching; a Camera Tools conflict was never established.

Other active mods: Camera Tools (Peter's report and saved-mod tooltip), CampaignObjectives, HARTDeliveryDrive, HARTFreeCamera and HARTRivalBalance. Results apply to this configuration; other-mod causation was not isolated.

## Checklist

Screenshot names below refer to `screenshots/`. “Pass (reported)” means Peter confirmed behavior; it was not independently observed through computer use. Partial findings retain unchecked parts rather than assuming them.

| Step | What was done | Expected | Observed | Result | Screenshot files |
|---|---|---|---|---|---|
| A1 | With mod parked, checked Mods/search, started default new game, reached day 2, Save as FT baseline, quit. Peter reported completion. | Food Trucks absent; day-2 FT baseline created. | Screenshot shows other mods; search/day/save completed per Peter. Later save-list screenshot verifies FT baseline Day 2. Closure and phase-A log copy checked. | Pass (reported; save presence checked) | A1-mods.png; C1-save-list-mod-mismatch.png |
| B1 | Opened Mods after restoration. | Mod listed/enabled. | Exact name FoodTrucks; LOCAL MOD; MOD ACTIVE and green toggle. | Pass | B1-mods-active.png |
| B2 | Loaded FT baseline and displayed status. | data Fresh; save count 0. | Day 2, 00:33: data Fresh, count 0; street food $0.00; taxable days 1–1 $0.00; statement $0.00; projected tax $0.00. Initial baseline-load warning not reported. | Pass | B2-fresh-status.png |
| B3 | Opened vehicle order list at General US Trucks, Industry City, 1 7th Avenue; selected both test entries. | Both test vehicles at $1 alongside normal stock. | Food truck (test) and Food cart (test) listed; each price $1, delivery fee $0, total $1. Delivery can be unchecked; warehouse was not required for these purchases. | Pass | B3-vehicle-list.png; B3-truck-price.png; B3-cart-price.png |
| B4 | Purchased cart first, then truck after cart pickup. | Truck appears at dealer; Ctrl+F6 fallback only if needed. | Both purchases corroborated by -$1 transactions. Cart outside first; next purchase blocked until pickup. Truck dealer purchase confirmed by Peter. Truck appears with Food truck (test), Storage: 0/20, CLICK TO DRIVE. Ctrl+F6 not needed/tested. | Pass | B4-dealer-purchase-response.png; B4-cart-outside.png; B4-pickup-required.png; B4-food-truck-present.png; B10-no-sale-transactions.png |
| B5 | Drove and parked truck. | Normal driving about 100 m. | Driving and parking normal per Peter; exact distance not measured. Access-storage action unavailable despite storage label. Vehicle looks like pink sedan with white structure at rear; placeholder intent unverified. | Pass (reported driving/parking); storage issue | B4-food-truck-present.png |
| B6 | Later pressed Ctrl+F7 on pavement. | Red box cart in front, handle toward player. | Spawn worked per Peter and screenshot. Green steering-wheel icon is offset beside cart near pavement. Exact handle orientation not clearly established; spawn notification not supplied. Extra spawned cart retained. | Partial: spawn pass; icon defect; orientation unclear | B6-spawned-cart-icon-offset.png |
| B7 | Clicked purchased cart; supplied side view while held. | Walk to handle; two-handed grip; cart in front. | Pickup works, cart in front, arms toward handle; hands/fingers visibly distorted. Peter reports no hover highlight and no access-storage/drive menu before pickup. Approach animation not independently captured. Panel FOOD CART (TEST), 0/6, PARK VEHICLE [P]. | Partial: pickup/front position pass; hand-pose defect | B7-cart-held-side.png |
| B8 | Walked and ran holding cart. | Cart follows in front for about 50 m, then running. | Stays in front while walking/running per Peter. Slides sideways to remain in front; feels held rather than realistically pushed. Distance not measured. | Pass (reported following); physics issue | B7-cart-held-side.png (static context) |
| B9 | Released and re-grabbed cart. | Both work. | Peter explicitly confirms both worked. | Pass (reported) | B7-cart-held-side.png (context) |
| B10 | Initial sales inputs had no visible result; after keyboard resolution performed four successful sale actions. Checked transactions/status. | Three +$10 sales, Street food entries, notification screenshots. | Four Day 3 +$10 entries labeled exactly Street food sale: test sale; status/log verify $40. Before HUD 14.7k, too rounded for exact balance check. Four rather than planned three sales; sale notifications not supplied. | Partial: sales/ledger/labels pass; count deviation and missing notification/balance evidence | B10-no-sale-transactions.png; B10-four-sales-tax-dashboard.png; B10-status-40-sales.png |
| B11 | Single tax fixture action; status captured. | At least $200,000 taxable sales; $200,000 statement row; positive projected tax; money unchanged. | $200,000 fixture on day 2 confirmed by log as ledger only. Status: total $200,040; taxable days 1–2 $200,000; row $200,000; projection $119,994.59. No explicit before/after money confirmation; later HUD still 14.7K cannot resolve cents. | Partial: ledger/tax figures pass; unchanged money unverified | B11-tax-test-status.png |
| B12 | Opened Econoview Taxes. | Positive projection matches status. | Projection $119,994.59 matches exactly; displayed rate 2%, deductions $270.35; No taxes due; Day 60 (56 days left). Projection semantics not verified. | Pass (UI match) | B12-econoview-tax-projection.png |
| B12a | Issued statement; opened IRS message. | Street food income and tax due; do not pay. Checklist originally expects $200,030. | Actual income $200,040 because four sales; deductions $270.35; taxable $199,769.65; rate 2%; total tax $3,995.39; deadline Day 23. Period displayed Day -56-3. Unpaid bill later visible in C4 screenshot. | Pass with documented four-sale deviation | B12a-statement-notifications.png; B12a-irs-statement.png; C2-econoview-raw-mod-labels.png |
| B13 | Ctrl+F9 on/off; captured standing and absent person. | Customer approaches roughly 2 m ahead, stays, then removed. | Peter confirms worked; standing/removal screenshots. Log confirms walking 8.2 m to target and arrival/standing; target distance from player not measured. Notifications not supplied. | Pass | B13-customer-standing.png; B13-customer-removed.png |
| B14 | Saved/reloaded FT parked before successful sales/tax tests. | Objects/data retained; count increases by one. | Save/reload works. Log: saved through count 8, loaded count 8, zero sales. Cart usable after load but green icon missing (reported). D1 later confirms original status; Peter reports reload/object check worked. Autosave/multiple writes prevent isolating exactly one increment. | Partial: persistence pass; icon issue; precise increment unverified | B10-status-40-sales.png (later status, not original baseline); D1-parked-restored-status.png |
| B15 | Held spawned cart, captured status, saved FT holding, closed. | Held-cart save and exit. | Held cart screenshot; pre-save count 11, $200,040; last B log write count 12. Save list verifies FT holding exists. D3 verifies held state and count 12. Closure checked; phase-B log preserved before parking. | Pass | B15-held-cart-status.png; C1-save-list-mod-mismatch.png; D3-held-cart-restored-status.png |
| C1 | With mod parked, loaded FT parked after separate held-save run. | Observe load, warnings, object visibility. | At saved Industry City location; truck/cart gone per Peter. Walking/interactions work. Warning screenshot belongs to earlier FT holding run; no separate parked-run popup screenshot. | Pass (reported location/object result) | C2-parked-persona-malformed.png |
| C2 | Walked/interacted; opened Persona and Econoview in FT parked. | Normal movement/apps for one minute. | Movement/interactions work (duration not measured). Both apps open. Persona malformed: PERSONAL WEALTH -%9.653; Cash 5; Investments 5; Assets/Loans $120B each. Econoview raw mod vehicle IDs; no sales/tax bill; deductions $270.35. Baseline Persona not captured, so cause of malformed values not isolated. | Partial: movement/apps accessible; abnormal Persona/raw labels | C2-parked-persona-malformed.png; C2-parked-econoview.png |
| C3 | Save as FT parked removed; closed completely to preserve log. | Separate removed save; originals retained. | Peter confirms saved/quit; D2 successfully loads named save. Closure checked; phase-C.log copied before restoration. C4 had already been performed out of sequence. | Pass | D2-removed-save-restored-status.png (later verification) |
| C4 | First removal run actually loaded FT holding, accepted mismatch warning; quit. Peter corrected save attribution. | Observe worst-case held-cart removal load. | Relocated to very different city location; cannot interact with world. Phone apps accessible; Persona malformed; Econoview raw IDs, four sales and unpaid $3,995.39 bill. Missing prefab and null-reference exceptions. No scene-location screenshot supplied. | Fail (usable held-save removal load) | C1-save-list-mod-mismatch.png; C1-load-anyway-warning.png; C2-persona-malformed-wealth.png; C2-econoview-raw-mod-labels.png |
| D1 | Restored mod, loaded original FT parked; status. | Original objects/count/data. | Peter reports worked. Data Loaded, count 8, all street food/tax figures $0; matches original B log state. Cropped screenshot does not independently show both vehicle positions. | Pass (status checked; object check reported) | D1-parked-restored-status.png |
| D2 | Loaded FT parked removed with mod installed. | Observe objects/data kept or lost. | Peter explicitly says both vehicles back. Loaded count 8, all figures $0. Data survives mod-absent save at original zero ledger; nonzero removed-save roundtrip not tested. | Pass | D2-removed-save-restored-status.png |
| D3 | Loaded FT holding with mod installed, checked held state/status, closed. | Cart held; normal interaction; data retained. | Screenshot confirms cart held; Peter reports worked. Loaded count 12; total $200,040, taxable/row $200,000, projection $120,000. Log corroborates load. Closure checked; phase-D log preserved; mod finally parked. | Pass | D3-held-cart-restored-status.png |

## Exact captured mod notifications and status texts

Line wrapping is normalized; wording, punctuation and amounts below are transcribed from screenshots. Screenshot status ranges use an en dash; plain log versions use a hyphen. Notifications not supplied for successful sales, spawning or customer actions remain unverified; see preserved mod-line logs for diagnostic text, which is not assumed to have appeared on screen.

B2, Day 2, 00:33:

```text
Food Trucks 0.0.1-spike: data Fresh, save count 0, street food this tax year $0.00. Taxable sales days 1–1: $0.00; statement row $0.00; projected tax $0.00.
```

B10, Day 3, 02:59:

```text
Food Trucks 0.0.1-spike: data Loaded, save count 9, street food this tax year $40.00. Taxable sales days 1–2: $0.00; statement row $0.00; projected tax $0.00.
```

B11, Day 3, 04:39:

```text
Food Trucks 0.0.1-spike: data Loaded, save count 9, street food this tax year $200,040.00. Taxable sales days 1–2: $200,000.00; statement row $200,000.00; projected tax $119,994.59.
```

B12a, Day 3, 09:37 (two notifications):

```text
Tax statement sent: open the IRS message on your phone.
Internal Revenue Service has been added to your Contacts app.
```

B15, Day 3, 12:35:

```text
Food Trucks 0.0.1-spike: data Loaded, save count 11, street food this tax year $200,040.00. Taxable sales days 1–2: $200,000.00; statement row $200,000.00; projected tax $120,000.00.
```

D1, Day 3, 01:15; D2, Day 3, 02:15 (identical text):

```text
Food Trucks 0.0.1-spike: data Loaded, save count 8, street food this tax year $0.00. Taxable sales days 1–2: $0.00; statement row $0.00; projected tax $0.00.
```

D3, notification timestamp Day 3, 12:57 (HUD at screenshot 12:59):

```text
Food Trucks 0.0.1-spike: data Loaded, save count 12, street food this tax year $200,040.00. Taxable sales days 1–2: $200,000.00; statement row $200,000.00; projected tax $120,000.00.
```

## Every captured warning and dealer dialogue

B4 dealer response:

```text
Thank you very much. Your vehicle is waiting for you outside.
Can we help you with something else?
```

B4 warning, Day 2, 13:13:

```text
Please pick up your recently purchased vehicle, before buying another one.
```

Saved-mod tooltip (FT holding run; filenames retain C1 prefix):

```text
Saved with Mods
Not all mods in this save are enabled
Camera Tools
FoodTrucks
HARTRivalBalance
CampaignObjectives
HARTDeliveryDrive
HARTFreeCamera
```

FoodTrucks is orange. Load confirmation for FT holding:

```text
ARE YOU SURE?
This save was created with mods that are not currently enabled.
Loading it can cause issues with mod compatibility or even break your save. Make sure to create a backup to avoid dataloss.
Do you want to load this save anyway?
CANCEL
LOAD ANYWAY
```

No other error popup was supplied. Initial FT baseline mismatch warning and a separate FT parked removal warning were not captured/reported. The capture-tool error was not a game popup.

## IRS statement

Wording/amounts retained; spacing normalized:

```text
Notice of outstanding tax payment
Dear Sir or Madam:
The tax assessments for the period Day -56-3 is now available.
Income:
Street food                         $200,040.00
Tax Deductions:
Taxi Ride                               $268.35
Vehicle - Food cart (test)                 $1.00
Vehicle - Food truck (test)                $1.00
Subtotal                                $270.35
Grand Total:
Income:                             $200,040.00
Tax Deductions:                        -$270.35
Taxable Income:                     $199,769.65
Tax Rate:                                    2%
Income Tax:                           $3,995.39
Real Estate Tax:                         $0.00
Total Tax Due:                        $3,995.39
Please pay any outstanding taxes by Day 23 at your local IRS office:
Sincerely,
Internal Revenue Service
```

The issued bill matches `(200040 - 270.35) × 0.02`, rounded to cents. The substantially larger upcoming projection is a different measure; its calculation/annualization semantics were not verified. No IRS payment was instructed or performed by Codex; the held-save removal screenshot verifies the bill remained unpaid then.

## Surprises, limitations and log evidence

- Dealer purchasing works without warehouse delivery when Deliver to is unchecked. Buying the cart first temporarily blocks another purchase until pickup. The dealer is General US Trucks, 1 7th Avenue.
- Cart grip/fingers distorted; cart follows by sliding rather than natural wheel-constrained pushing. Cart hover/menu omissions, offset interaction icon on spawn and missing icon after reload were reported/captured as distinguished in the table. Truck storage interaction unavailable despite capacity label.
- Removing mod while cart held caused relocation and world-interaction failure. Removing it with vehicles parked retained location and walking/interaction, with objects absent. Both removal runs show malformed Persona values; no baseline Persona screenshot or independent mod isolation establishes the cause. The UI values are not proof that actual balances became $120B.
- Mod-removed Econoview retains transaction records with raw IDs: `foodtrucks:transaction_sale`, `foodtrucks:vehicletype_spiketruck`, `foodtrucks:vehicletype_spikecart`.
- Restoring the mod recovers vehicles even after saving FT parked removed. Restoring original FT holding recovers held cart and nonzero ledger. A nonzero-ledger save made with mod absent was not tested.
- Four successful sales instead of three; B14 happened before sales/tax; C4 happened before C1–C3. These order/count deviations are accounted for. FT parked had count 8 and zero sales; FT holding had count 12 and $200,040. Multiple saves/autosaves mean exact one-write increment at B14 is unverified.
- Exact money change for each sale and unchanged money during fixture were not independently checked. HUD was rounded 14.7k/14.7K. Successful transaction entries and ledger data are verified; tax fixture log explicitly says ledger only.
- Every supplied image is retained (31 files). Missing notification screenshots, exact walking/driving distances and precise handle orientation remain unverified.

Preserved logs and corresponding `-mod-lines.txt` files: `phase-A.log`, `phase-B.log`, `phase-C-holding.log`, `phase-C.log`, `phase-D.log`. Phase C required two launches; the first failure log was preserved separately before relaunch. Logs copied only after game closure, always before relaunch/folder move. No recorded file was overwritten.

Exception-heading counts in preserved logs (counts of lines, not distinct root causes):

| Log | Exception headings |
|---|---|
| phase-A.log | None matching Exception: |
| phase-B.log | 10 × `MissingReferenceException: SleepEnvironment config is not assigned.` |
| phase-C-holding.log | 2 × missing spikecart; 1 × missing spiketruck; 60 × NullReferenceException; 1 × System.NullReferenceException |
| phase-C.log (parked run) | 1 × missing spikecart; 1 × missing spiketruck; 4 × NullReferenceException |
| phase-D.log | None matching Exception: |

Exact missing-prefab messages:

```text
Exception: Prefab wasn't found: Vehicles/PlayerVehicles/spikecart
Exception: Prefab wasn't found: Vehicles/PlayerVehicles/spiketruck
NullReferenceException: Object reference not set to an instance of an object
System.NullReferenceException: Object reference not set to an instance of an object
```

NavMesh creation failures also appear in logs. Neither those nor Phase B's SleepEnvironment errors were causally attributed to FoodTrucks; no code changes made. Missing-prefab/null-reference errors accompany removal runs, but exact failure chain is not established.

## Seven questions

| Question | Answer | Evidence and limits |
|---|---|---|
| 1. Can a mod vehicle be bought? | Answered yes | B3–B4: both $1 entries, purchases and spawned dealer vehicles; no warehouse required. |
| 2. Can the cart be pushed? | Answered yes, functionally | B7–B9: pickup, walking/running follow, release/re-grab; hand pose and sliding physics defective. |
| 3. Does mod data survive save/load? | Answered yes | B15/D3: count 12 and $200,040 ledger with cart held. D1/D2 retain count 8/zero ledger; vehicles recover after mod-absent save. Nonzero removed-save roundtrip untested. |
| 4. Does tax integration work? | Answered yes for ledger/UI/statement | B11–B12a: $200,000 fixture, matched projection and Street food $200,040 in 2% bill. Projection semantics and unchanged money not fully verified. |
| 5. Does test customer work? | Answered yes | B13: standing/removal screenshots and log arrival; user confirms. |
| 6. Does transaction labeling work? | Answered yes with mod installed | B10: Street food sale: test sale. After removal, raw IDs shown (C2/C4). |
| 7. Is uninstall/removal safe? | Answered no overall | C4 held save loses world interaction/relocates. C1–C3 parked save responsive but Persona abnormal/raw labels; D1–D3 restore objects/data. Persona cause not isolated. |
