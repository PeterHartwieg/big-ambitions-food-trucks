# Cart business closure — 0.10.5

Based on 0.10.4 commit `0dd12dc`: retains the released cart-menu, safe-unload, water-removal and unlimited-roster changes. Only the cart-sale feature is carried from the shared development checkout; unrelated cooler, branding, demand and crew-driving work is excluded.

On foot at a commissary bay, Sell cart opens the game's explicit negative confirmation with a resale quote for the cart and its paid equipment/stock. Closure releases crew assignments, permit, rental, routes, shifts, partnership and active pitch holds. It retains historical accounts, earned wage obligations, shared commissary stock and orders. Outstanding sales settle separately. A failed native resale payment is persisted and retried without paying while the cart remains owned.

## Validation

- Core: 2,022 passed, one existing opt-in skip. Six cart-sale cases passed again after adding explicit pending/standing-order retention assertions.
- Production sale-adapter harness: passed cancellation, unavailable confirmation UI, stale state/quote, repeated confirmation, read-only state, deletion failures, payout retry/reload, nested/unpaid cargo, and commissary CTA scope.
- Packaging and retained roster regression suites: eight Python tests passed.
- Official SDK: Windows and Mac packages built and bundle-validated. No runtime sources changed after this build.
- Native Mac headless rig, session `6e5ada0d`, exact 0.10.5 package with the rig's established DLL port/profile adaptation. Normal game and saves untouched. Disposable copies of an existing FT roster fixture.
- Native sale: a cart with equipment, 45 dumplings, three crew, a week of route stops, permit and rented bay quoted $3,247.13. Confirmation paid that plus $337.50 outstanding cart sales. All three employees became unassigned; the other truck's $606 takings remained pending. Shared 10 dumplings and the pending 80-dumpling delivery remained unchanged.
- Native midnight: sold cart retained $337.50 historical sales, $9.86 rent and $116.67 wages for the three employees' earned hours. Other truck retained its separate $233.34 wages. No repeat sale payout.
- The saved mod payload has the sold cart closed/unpermitted, with no crew, schedule, shifts, transit, arrival or return operation.

Raw native replies, package/build logs and test scripts remain under `build/cart-sale` and `build/` in this release worktree. Initial development-build evidence is separately recorded in the shared checkout's `docs/evidence/2026-10-22-cart-sale.md`.

## Limits

Windows gameplay and interactive visual layout are not established by the Mac headless session. The rig retains its usual optional mods and emits known headless graphics messages; this is not a globally clean game-log claim. Venue partnerships and pitch release have Core coverage, not a live property/event acceptance test. The native item picker did not expose its rows in headless mode; standing-order setup through that UI is unverified.

No Workshop publication is performed by this task.
