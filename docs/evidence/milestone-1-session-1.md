# Food Trucks — Milestone 1 playtest, session 1

Date: 2026-10-08 (Europe/Berlin). Peter operates the game; Codex documents user reports and supplied screenshots against `CHECKS.md`.

Status: Phase A finished per Peter; Phase B finished per Peter, with B1 partial and B1a skipped; C1 passed; all three phase logs preserved and game closure checked. Final mod parking unconfirmed. Partial Phase A checks remain explicitly listed below. Checklist targets build 0.1.0; A2 confirms the in-game status reports version 0.1.0. Package hashes remain unverified. Automated checks remain unverified; Phase A startup/gameplay log checks are recorded below. No gameplay fixes have been attempted.

## Checklist results

“Pass (screenshot)” means directly visible in supplied evidence. “Pass (reported)” means confirmed by Peter. Unreported steps remain pending; a partial result does not establish the unchecked parts.

| Step | Observation | Result | Evidence |
|---|---|---|---|
| A1 | Mods menu lists `FoodTrucks`, `LOCAL MOD`, `MOD ACTIVE`, with green toggle. Display name differs from the checklist's `Food Trucks`. | Pass (screenshot): listed and enabled | screenshots/A1-mods-active.png |

| A2 | Day 1, 09:27 status: `Food Trucks 0.1.0: data Fresh, save count 0, street food this tax year $0.00.` New-game creation and movement are not independently shown by the cropped screenshot. | Pass (screenshot): expected Fresh data and zero save count | screenshots/A2-fresh-status.png |

| A3 | In-person vehicle order dialog shows `Food cart`, vehicle price $1,200, delivery fee $0 and total $1,200; delivery checkbox unticked. Vehicle dropdown is closed. Exact dealer/address, normal stock and absence of `Food truck (test)` are not established by this screenshot. | Partial (screenshot): cart listing and price pass; remaining list/location checks pending | screenshots/A3-cart-price.png |

| A4 | Peter cannot perform the delivery test: no warehouse at game start and insufficient funds to rent one. No delivery attempt, refusal message or unchanged-money check was reported. CHECKS.md explicitly permits skipping this case when a new game lacks a warehouse. | Skipped (reported setup limitation); delivery refusal remains pending | Peter report, 2026-10-08 |

| A5 | Following the purchase dialog and skipped delivery test, screenshot shows the red food cart on the street beside the player, with green steering-wheel icon over it. HUD: $13.7K, Monday (Day 1). Exact dealer/address and purchase debit are not independently established by this crop. | Pass (screenshot): cart present outside after purchase sequence | screenshots/A5-cart-outside.png |

| A7 | Screenshot shows player behind the cart handle with both arms extended toward it; item panel says `FOOD CART`. Pickup/held position visible; approach animation not captured. Fingers appear to hang below the bar rather than wrap around it, especially the nearer hand. A normal hand pose is not established; possible grip issue, not yet confirmed by Peter. | Partial (screenshot): pickup/held position pass; hand-pose check unresolved | screenshots/A7-cart-held-hands.png |

| A8 | Peter reports: "pivot point feels much more natural now." Screenshot shows player no longer holding the cart, with the green steering-wheel icon over the released cart. Exact 50 m distance, left/right turns, staying in front, absence of sideways sliding, icon hiding while pushing have not been explicitly confirmed. The later A9 screenshot shows the cart held again after the released-cart screenshot, supporting release/re-grab functionality; the interaction itself was not captured. | Partial: improved pivot feel (reported); release and icon position (screenshot); remaining movement checks pending | screenshots/A8-cart-released-icon.png; Peter report, 2026-10-08 |

| A9 | Screenshot shows cart held inside a store beside the checkout. FOOD CART cargo panel reads 1/6; Hotdog shows 500 units x1 box and an orange dollar badge. OPEN HERE and PARK VEHICLE buttons visible. One box is in cargo; payment, outside carry-and-click loading prompt, second box and 2/6 capacity are not established. Screenshot records an inside-store route rather than the planned outside-loading test; no defect inferred from that variation. | Partial (screenshot): one hotdog box in cargo; planned two-box outside-loading/payment checks pending | screenshots/A9-hotdog-one-box-inside.png |

| A10 | Asked whether OPEN HERE released the cart without opening the sleep panel, Peter confirmed: "Yes, open here worked fine." A11 screenshot corroborates subsequent selling. Exact opening notification and pavement placement were not independently captured. | Pass (reported): opening/release without sleep panel; exact notification text unverified | Peter report, 2026-10-08; A11 sale screenshot (corroboration) |
| A11 | Day 1 notifications: 15:35, `Sold Hotdog for $11.65. 498 left.`; 16:24, `Sold Hotdog for $11.65. 488 left.` Second screenshot HUD is 16:29. Stock drops 10 units across 49 notification minutes, consistent with roughly one sale per five minutes (inference from endpoints; intervening sale timestamps not captured). Earlier A9 cargo showed 500 units. More than 30 in-game minutes of ongoing sales evidenced; account balance increase remains unverified. Player/cart distance is not shown by these crops. | Partial: ongoing sales and approximate cadence supported by screenshots; balance check pending | screenshots/A11-hotdog-sale-498-left.png; screenshots/A11-hotdog-sale-488-left.png |

| A12 | Econoview Last Transactions shows seven visible Day 1 entries labelled exactly `Street food sale: Hotdog`, with green positive amounts displayed as $11. Earlier sale notifications show $11.65; this screenshot does not establish cents in the transaction view or a before/after cash balance. Taxes pane: rate 2%, no taxes due, upcoming projection below taxable minimum, Day 60 (58 days left), total deductions $2,278.88. Tax correctness is not tested by this image. | Pass (screenshot): sale labels and positive income entries; exact credit/balance remains unverified | screenshots/A12-econoview-sale-labels.png |

| A13 | Peter confirms no sales while away, pause notification worked, and "resuming worked aswell." Screenshot at Day 1, 18:21 reads `Nobody at the cart: sales paused until you come back.` Earlier 17:15 close summary reports 0 sales, $0.00 in a later session distinct from the 16:44 close with 15 sales. Exact distance/time away and once-only notification frequency were not independently measured; resumption is reported rather than shown by the pause screenshot. | Pass: pause notification (screenshot); no sales away and resumed sales (reported) | screenshots/A13-away-zero-sales-close.png; screenshots/A13-away-pause-notification.png; Peter reports, 2026-10-08 |
| A14 | Day 1, 16:44 notification: `Cart closed: 15 sales, $174.75.` Total matches 15 x $11.65 from the captured sale notifications. Screenshot confirms closing notification; grab interaction itself is not visible. | Pass (screenshot): expected close summary; grab trigger not independently captured | screenshots/A14-cart-closed-15-sales.png |

| A15 | Day 1, 17:46 status: `Food Trucks 0.1.0: data Fresh, save count 3, street food this tax year $174.75. Cart NiFBpZ: open, stock 485, 15 sales, $174.75 lifetime.` Cart open again and lifetime figures preserved across closing/reopening. Stock 485 matches initial 500 minus 15 sales. Keypress/open action not captured; earlier partial notification is cropped and not transcribed as a full message. | Pass (screenshot): open status, stock and lifetime totals | screenshots/A15-open-status-15-sales.png |

| A16 | Peter supplies screenshot "after saving and loading." Day 1, 18:58 status: `Food Trucks 0.1.0: data Loaded, save count 4, street food this tax year $314.55. Cart NiFBpZ: open, stock 473, 27 sales, $314.55 lifetime.` Also visible: 18:57 sale for $11.65, 473 left. Cart ID matches A15; stock 473 = 500 - 27 and revenue $314.55 = 27 x $11.65. No immediately pre-save lifetime snapshot supplied, so exact before/after equality is not independently established. Save name FT1 open, quit-to-menu path, whether the visible sale occurred before or after loading, no burst and continued normal cadence remain unverified. | Partial: save/load (reported), Loaded/open state and consistent lifetime figures (screenshot); no-burst/normal-rate checks pending | screenshots/A16-loaded-open-status.png |

| A17 | Peter reports pressing F6 hid the UI but also spawned the test truck; he successfully put a box of hotdogs in it. Screenshot shows pink test vehicle and CARGO panel: BOXES 1/20, Hotdog 500 units x1 box, orange dollar badge. Truck spawn/loading pass; exact Ctrl modifier, empty-hands store entry and payment are not independently established. | Pass: truck spawn/loading (reported), cargo contents (screenshot); payment not verified | screenshots/A17-test-truck-hotdog-cargo.png; Peter report, 2026-10-08 |

| A18 | Peter reports driving the test truck past the open cart, triggering sales. Driving works (reported). Legal parking within about 5 m and cargo showing in the in-truck item panel have not been supplied; A17 shows a separate cargo window while outside. | Partial: driving confirmed (reported); planned parking/in-truck cargo checks pending | Peter report, 2026-10-08 |
| A19 | Peter reports "sleeping worked" in the ongoing test-truck sequence. Sleep action works (reported); exact one-hour duration, absence of sales during sleep, no burst on waking and normal-rate resumption after waking are not yet confirmed. | Partial: sleep works (reported); sleep-related sales/timing checks pending | Peter report, 2026-10-08 |

| A20 | Day 2, 12:59 fixture notification: $200,000 street food recorded for today; no money moved (notification claim, balance not independently measured). At 13:23: `Tax statement sent: open the IRS message on your phone.` and IRS added to Contacts notification. Econoview shows tax rate 2%, taxes owed $3,940.83, due Day 22 (20 days left), upcoming projection below taxable minimum, Day 60 (57 days left), deductions $0.00. Positive unpaid tax bill established in Econoview; IRS message contents/itemized calculation not supplied. | Partial: fixture, statement issuance and tax owed confirmed (screenshots); specified IRS message check pending | screenshots/A20-tax-fixture-day2.png; screenshots/A20-statement-sent.png; screenshots/A20-econoview-tax-owed.png |

| A21 | Day 2, 13:42 notification: `A tax bill is still unpaid; no new statement issued.` Matches expected refusal after the A20 tax statement and unpaid $3,940.83 bill. | Pass (screenshot): unpaid-bill guard notification | screenshots/A21-unpaid-bill-refusal.png |

| A22 | Day 2, 14:01 notification: `Remove and refund 2 Food Trucks vehicles and cancel 0 deliveries? Press again within 10 seconds.` Expected two-vehicle confirmation matches purchased cart and spawned test truck. Not-holding/not-driving setup not independently visible. Also shown: Day 2, 14:00 new IRS message notification; message contents not shown. | Pass (screenshot): uninstall confirmation counts and two-step prompt; actual removal pending A23 | screenshots/A22-uninstall-confirmation.png |

| A23 | Day 2, 14:14 notification: `Removed 2 vehicles, refunded $3,279.60. Save now; then the mod can be removed.` Completion notification confirms reported removal count/refund. Vehicle absence, exact before/after cash increase and refund breakdown are not independently shown. Top of screenshot includes cropped prior confirmation; real-time double-press interval was not measured from in-game timestamps. | Partial (screenshot): removal/refund completion notification pass; world absence and cash delta unverified | screenshots/A23-removed-refunded.png |

| A24 | In response to the reminder to save as FT1 removed and quit, Peter confirms "done". Windows process check found no BigAmbitions process. Preserved log ends with save counts 8 and 9 after uninstall. Exact save filename is reported, not independently inspected. | Pass (reported save/quit; game closure and post-uninstall writes checked) | Peter report; Phase A Player.log |

| B1 | Peter reports "Bone seems to work fine. Interaction walking etc all good." Interpreted in checklist context as B1, not a hand-bone finding. Movement/interactions work (reported); Econoview opens and shows expected raw `foodtrucks:transaction_sale` labels on Days 1/2. Refund entries: Sold Food truck (test) $1,076; Sold Food cart $2,202 (UI whole-dollar display; exact amounts in Phase A log). Exact save filename/location, mod parking state, Persona appearance and retained unpaid tax bill are not independently confirmed. | Partial: walking/interactions (reported), accessible Econoview/raw keys/refund entries (screenshot); Persona/tax-bill checks pending | screenshots/B1-removed-transactions.png; Peter report |

| B1a | Peter could not get the console working and explicitly skipped the test. `IRSForcePayment 1` was not run; console setup/input cause not diagnosed. | Skipped (reported); forced tax payment after removal remains unverified | Peter report |
| B2 | Peter reports "B2 done aswell", confirming the checklist sequence: new normal game, Save as FT1 plain, quit. Completion is reported; save filename, Food Trucks absence in Mods and have not been independently inspected for this phase. Subsequent local process check confirms game closure; Phase B log preserved. | Pass (reported) | Peter report |

| C1 | Peter supplies phase C screenshot following B2 plain-save completion. Day 1, 08:04: `Food Trucks 0.1.0: data Fresh, save count 0, street food this tax year $0.00.` Expected Fresh/zero state with restored mod verified. Exact loaded filename not visible; FT1 plain attribution follows checklist context. | Pass (screenshot; save attribution by session context) | screenshots/C1-plain-fresh-status.png |

## Session setup and reminders

- A1 screenshot also shows CampaignObjectives, HARTDeliveryDrive and HARTRivalBalance active. Checklist requests these three off; reminder given. Their deactivation remains pending confirmation.
- HARTFreeCamera is also active in the screenshot. The checklist does not request its deactivation.
- A20 Econoview screenshot also shows a Day 1 `Purchase from Titans of Industry Supply` transaction for -$1,075. This confirms a wholesale purchase was paid, but does not identify which box or establish both A9/A17 payment sequences.
- FT-only save use and Steam `-console` setup have not yet been confirmed.

## Coverage still pending

- Automated checklist: tests, generated build/package contents and installation/parked-build state.
- Phase A: remaining A3 list/location checks; A6 hover outline; A7 hand pose; remaining A8 movement checks; remaining A9 payment/outside-loading/two-box checks; remaining A11 balance check; remaining A16 save-name/pre-save-total/timing checks; remaining A18 legal-parking/in-truck cargo checks; remaining A19 sleep-sales/timing checks; remaining A20 IRS-message check; remaining A23 independent vehicle-absence/cash-delta checks. A24 save/quit complete. A4 was skipped; delivery refusal remains pending for a warehouse-capable setup.
- Phase B: remaining B1 Persona/tax-bill/save-location checks. B1a skipped because console would not work; forced tax payment remains unverified. B2 complete (reported).
- Phase C: C1 passed; closure checked and log preserved. Final mod parking unconfirmed.
- Phase A startup/save/gameplay log checks completed below; Phase B logs preserved below; Phase C log verified below. Logs do not establish exact no-burst timing or sleep-sales behavior.

## Reported issues and unresolved causes

- **Driving past the open cart triggers sales.** Peter observed sales while passing the cart in the test truck. Milestone 1 CHECKS.md describes sales eligibility within 8 m, including while in a nearby truck; the reported behavior is consistent with proximity-based eligibility, though exact distance was not measured. Peter questions whether drive-by serving should remain long term and suggests player assignment to a workstation. Record as future design feedback, not an implemented assignment feature or established Milestone 1 defect.

- **Daily summary omits recorded street-food activity.** Peter reports no profits on the daily P&L. Screenshot shows DAY 1 SUMMARY, TOTAL PROFIT $0, BUSINESSES 0, despite earlier Day 1 sale transactions and a $314.55 lifetime sales/status total at 18:58. Verified display discrepancy; intended behavior remains unresolved. Sales revenue is not established net profit (stock cost/other expenses not reconciled). The design describes a mod-owned simulation outside rented-building BusinessRegistration; that is a possible explanation, not a verified runtime cause. Daily P&L integration is not an explicit Milestone 1 CHECKS.md requirement. Evidence: screenshots/day1-summary-zero-profit.png, A12 transactions and A16 status. No fix attempted.

- **F6 hides UI despite a different configured camera-controls keybind.** Peter reports this during the A17 spawn action; the test truck still spawned and accepted cargo. Camera-controls mod identity/version, exact configured binding, Ctrl modifier and input handling were not captured. Cause and owning mod remain unresolved; this is not a confirmed FoodTrucks defect. No diagnostic or code changes attempted.

## Phase A log verification

Game closure was checked locally (no BigAmbitions process). Repository helper `tools/fetch-log-remote.sh milestone-1-phase-A homepc` preserved Player.log and Player-prev.log in `build/logs/game/milestone-1-phase-A-092745/` on the Mac. The directory suffix comes from the Mac clock; session date follows Europe/Berlin. Relevant mod lines are retained in [milestone-1-phase-A-mod-lines.txt](milestone-1-phase-A-mod-lines.txt). Raw logs remain under build; no saves were copied.

- Version 0.1.0, both vehicle registrations, Harmony 2.4.2.0 and all eight expected patches Active appear in the expected startup order.
- Fresh and Loaded schema-1 data, debug-actions readiness, vanilla hand-truck grip/offset line, Open button, openings/closings, sales and save writes present.
- No `Exception:` headings or `Prefab fixes:` lines found in Player.log. This does not verify other launches or establish the cause of reported UI issues.
- Before save count 4/load: 26 sales, stock 474. After load: one sale to stock 473, then status 27 sales/$314.55. This reconciles A16's pre/post-load ledger progression; log lines have no per-sale clock timestamps sufficient to prove normal cadence or no burst.
- Final close: 34 lifetime sales, $396.10, stock 466. Tax fixture recorded $200,000 for day 2 (ledger only); statement issued.
- Uninstall: cart refund $2,202.72, test-truck refund $1,076.88; total $3,279.60. Log reports 2 removed, 0 left, unpaid refund $0.00, then save counts 8 and 9. The uninstall line's `unpaid` refers to refund status; the outstanding IRS tax bill is separately evidenced in Econoview.

## Phase B log preservation

No BigAmbitions process found after B2. Helper preserved Player.log and Player-prev.log in `build/logs/game/milestone-1-phase-B-095236/`. Player.log contains 0 `Exception:` headings and 0 FoodTrucks-prefixed lines. No forced-payment test was run, so this log does not verify that regression. Mod absence is consistent with no FoodTrucks-prefixed lines, but physical folder state has not been checked.

## Phase C log verification

No BigAmbitions process found after the C1 report; logs preserved in `build/logs/game/milestone-1-phase-C-095729/`. [Mod-line extract](milestone-1-phase-C-mod-lines.txt) confirms version 0.1.0, all eight patches Active, schema 1 Fresh data, save count 0, and zero revenue. Player.log has 0 `Exception:` headings and 0 `Prefab fixes:` lines. Game closure checked; final mod parking not performed by Codex or independently confirmed.

## Findings and summary

Phase A completed per Peter, with partial coverage retained rather than treating every step as passed. Buyable $1,200 cart, pickup, improved pivot feel, stock/sales notifications, transaction labels, opening/closing, pause/resume, loaded open state, truck loading, sleep action, tax fixture/statement/unpaid-bill guard and uninstall notifications were evidenced. Logs corroborate 34 sales/$396.10 and two removed vehicles/$3,279.60 refunded; no exception headings found in the preserved Phase A log.

Open observations: fingers below the cart handle; F6 hides UI despite a different configured camera keybind; daily summary shows $0 profit/0 businesses; passing the cart in a truck triggers proximity-based sales. Causes/intended behavior remain distinguished from observed results. Workstation assignment is future feedback.

Remaining Phase A coverage: complete dealer list/location, hover outline, full movement check, two-box outside loading, exact cash delta, no-burst/normal-rate after reload, parked in-truck cargo panel, sleep-related sales suppression/wake timing, IRS itemization, and confirmation other gameplay mods were disabled. A4 delivery refusal was explicitly skipped for lack of a warehouse. Phase B finished per Peter: B1 movement/interactions work and Econoview shows expected raw sale keys plus refund income entries; Persona and unpaid bill retention remain pending. B1a was explicitly skipped because the console would not work, leaving the Milestone 0 forced-tax-payment regression unverified. B2 new normal game/FT1 plain save/quit is complete per Peter. C1 confirms Fresh data, save count 0 and $0.00 street-food revenue with restored version 0.1.0; exact plain-save name follows session context. Final game closure checked and Phase C log preserved; final mod parking remains unconfirmed. Codex has not parked or restored the mod during this session; current physical mod-folder state is not independently verified. B1 is interpreted from the user report and raw-label screenshot.
