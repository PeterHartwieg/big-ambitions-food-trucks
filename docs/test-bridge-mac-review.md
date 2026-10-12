# Mac review of the built test bridge

Updated against `f1eaf4b` (12 October 2026), which addresses the earlier Mac review. These are code findings and mock tests, not checks in the game. No game-side code was changed in this pass.

## What was fixed

The numbers refer to the original findings.

| Finding | Status in the game code |
|---|---|
| 1. Truck legality used the cart pavement rule | Fixed. Trucks use the wheel parking test; both kinds have a reason prefix. |
| 2. Order quantities were unavailable | Automated order setup fixed through `commissary order`. The phone quantity input remains unavailable through `ui`. |
| 3. Crew choices had duplicate text and no stable selector | Automated assignment fixed through `crew` and `state.employees` ids. Duplicate UI row text still needs ids. |
| 4. List pickers require selection and confirmation | Still applies. The game's confirm label needs a live UI list. |
| 5. Map picks, drags and equipment/reference placement | Still manual. No new commands cover these. |
| 6. Load acknowledgement could accept the old city | Fixed through `load.status`, including a fresh mod-data attach when reloading the same name. Poll for done and inspect failed/error. |
| 7. Walking and hatch startup had no completion phase | Fixed through `player.activity` and `clock_held_by`. Enter and hatch replies still precede arrival by design. |
| 8. Clock overshoot at payment boundaries | Fixed through the bridge-only tick clamp and `clock.run_until/arrived_at`. Scenarios stop at 07:59 and 23:59. |
| 9. Bays had no readiness query and door could use another entrance | Fixed through `commissary.bays` and the nearest-bay entrance for door views. |
| 10. Transaction evidence was missing | Payment checks fixed through `transactions_today`, which includes yesterday too. Displayed wages, tax and day-summary presentation still need review. |
| 11. Vehicle success could mask a panel refusal | Fixed through `result.done`. Walk-start success still needs an arrival wait. |
| 12. Calls blocked health/events and timed-out mutations could be repeated | Fixed through separate HTTP request threads and retained operation outcomes. The client polls the original op after a main-thread timeout. |
| 13. UI clipping/overlap and stale dropdown options | Still applies. `TestBridgeUi.Visible` checks canvases, alpha and screen bounds; id clicks check the component, not whether its option text/content still matches. |
| 14. Loading from a non-FT city discarded unrelated changes | Fixed. The source city must be FT too. |

## Changed on the Mac

- The mock supplies operation ids and retained pending/done/failed outcomes, concurrent HTTP requests, load progress, vehicle outcomes, crew assignment, per-item order quotes, transactions, employees, activity and bay state. Truck legality and exact clock targets follow the new implementation. Its prices, navigation and pictures remain fake.
- `ftlib` recovers a main-thread timeout by querying its op for up to 30 seconds. It never resubmits the changing call. Longer work and failed recovery retain the op id for `ft op` or `ft_op`. A transport failure before an id arrives reports an unknown outcome.
- All six scenarios wait for load completion and check vehicle outcomes and activity where relevant. Orders are created through the command, crew is hired and assigned by id, payment boundaries are exact, and bay shots poll readiness. Art references and routes remain prepared by hand.
- Order payments are checked by signed quote amount, date, time, label and count. Refused deliveries have no transaction or retry. Crew revenue is checked by its exact paid-in log amount, previous-day date, vehicle label and single transaction, independently of simultaneous expenses. Small runner additions (`where`, `pick`, `scale`, named event `captures`) support those checks.

## Remaining input for the game side

- **The statement that every /call reply has op is too broad.** `cmd: op` queries return only ok/result (or ok/error), and malformed JSON, missing cmd and invalid op queries have no op. The client and mock follow the code. The contract should state those exceptions.
- **The full phone order path is still not scriptable from the declared inputs.** Quantities are input fields; the bridge lists buttons and dropdowns only. The payment scenario records a manual phone counterpart using the mod's exact English locale labels, rather than assuming that adding an item twice sets two boxes. A quantity input command and one live list of the vanilla item/confirm controls would make that UI path repeatable.
- **UI discovery still needs care.** Clipped or covered controls can be listed; id clicks can select a changed dropdown option. Refresh after changes. Generic confirm labels and duplicated crew rows are unchanged; the direct crew command avoids the latter.
- **Prepared FT saves remain necessary.** New-game creation, route world/map picks, dragging and positioning equipment or vanilla comparison objects are outside the bridge. The scenario notes identify that setup explicitly.

Live navigation, UI, camera exposure, clock clamping and financial integration remain unverified. The Mac suite passes with the in-memory HTTP handler transport. The normal loopback suite was attempted, but this sandbox refuses listening sockets with `Operation not permitted`; it needs a run outside that restriction.
