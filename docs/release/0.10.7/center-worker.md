# Street Food 0.10.7 — centred cart worker

Employees, player stance and first customer share the midpoint of the two fitted worktop spots. The Unity adapter reads the actual fitted display anchors; Core computes their midpoint. With fewer than two fitted worktop pieces, service uses counter-local z=0. The dedicated below-counter fridge is excluded. Current anchors at z ±0.30 give z=0. The server remains 0.85 m across from the cart centre on the side away from customers, facing across the counter.

The register remains at (±0.29, 0.95, 0.615). Moving it to z=0 would crowd the gap between appliances and their controls; its existing front corner has verified clearance. Operator-facing equipment yaw and worker-side fridge mounting remain as in the grill-facing fix. Food truck paths and geometry are unchanged.

Crew placement caches follow changed equipment centres. A changed centre releases a held player stance and rebuilds the cart queue. Both customer and server pavement checks, road/parking/crosswalk exclusions, player reachability and bounded counter-side fallbacks remain. No end-of-cart service.

Validation and native screenshots pending this revision’s run. Full session report: `/Users/peterhartwieg/Coding_Projects/.agent-logs/streetfood-0.10.7-center-worker.md`.
