# Crew driving architecture review

21 October 2026. Independent source reviews by Opus 5.5, GPT 6 Astra and GPT 6.1 Sol. The panel reviewed the [evaluation](../crew-driving-and-fuel.md), the [first spike scope](../crew-driving-spike.md) and relevant Food Trucks, Persistent City, Driven Rides and local game source. This reviews a plan, not implemented functionality.

## First round

All three returned **REVISE**. They agreed the original scope put traffic persistence before the decisive native risk: operating an owned truck without the player. The revised immediate scope is Gate A, a transient unattended-driving probe with native vehicle preservation and no new journey save schema.

| Topic | Agreement or difference | Disposition |
| --- | --- | --- |
| Native ownership | All three require a per-truck controller and early proof that native startup, stopping and handover work without player state. | Gate A precedes provider contracts and schema work. |
| Code reuse | All three reject the player-ride singleton and a new Driven Rides Workshop dependency. | Reuse a pinned minimal steering/geometry subset with provenance and tests. |
| Provider binding | All three favor a versioned static facade with cached delegates over a shared contract DLL for this first consumer. | Deferred to Gate B; BCL values and bounded buffers, no hot-path reflection or internal types. |
| Background architecture | Astra and Sol prefer explicit external owners sharing the live traffic kernel. Opus prefers Food Trucks timetables with optional read-only traffic estimates. | Shared queues remain the target because camera-independent queue continuity is part of the proposed behavior. Timetables are a simpler comparison baseline with weaker congestion semantics. Gate A is useful to either architecture. |
| Truck storage | Astra and Sol recommend conservative weighted occupancy. Opus identifies the kernel changes but considers a truck nominally within one slot. | Prototype weighted occupancy at Gate B; measure native section coverage and rear clearance. Do not assume a roughly six-metre truck has enough clearance in a six-metre unit. |
| Persistence | Astra and Sol require a coordinated common checkpoint; Opus avoids it with one Food Trucks journey authority. | No cross-mod persistence in Gate A. Gate C must prove the save boundary, failure guard and owner/provider recovery before shared journeys can resume. Independent callbacks alone are insufficient; that fact does not establish coordination is impossible. |
| Provider backlog | All agree time and simulation lag must be explicit. Opus emphasizes independence from lag; Astra and Sol require permission-based holds. | Measure current backlog separately from congestion. A historical accelerated result is not a current bound. A delayed journey must not stall every Food Trucks vehicle's sales cursor. |
| Performance | All reject per-truck duplication of Driven Rides' planning allowance. | Aggregate budgets and matched native measurements; no performance claim from the one-truck demonstration. |

## Changes required by the review

- Separate the immediate native probe from traffic ownership, persistence and fleet gates.
- Make source acceptance, physical admission, ownership transfer and completion distinct. An advisory crossing permission or clamped progress report does not physically stop NWH wheel forces.
- Retain a stopped truck as an obstruction until it clears or ownership transfers. Do not copy ambient-car disposal behavior onto an owned vehicle.
- Cover NWH trucks and player/service vehicles in observations; audit the existing shared junction identity.
- Extend every resident-only traffic path, including time conversion and strict save validation, before accepting external journeys.
- Keep native tank/cargo/damage authoritative, define cancellation generations and checkpoint recovery, and preserve future/unreadable payloads.
- Separate one-truck feasibility from concurrency correctness and native fleet cost.

## Review evidence and limits

The first-round source inventory contains SHA-256 hashes for 304 C# files. Food Trucks baseline is `a09a814`; Persistent City has no committed HEAD in its current checkout, so the source inventory is the baseline there. Full briefs, frozen proposal revisions, responses and model metadata are retained locally under ignored `build/review-crew-driving/`. The actual Opus response metadata identifies `claude-opus-5-5`; Codex identifies `gpt-6-astra` and `gpt-6.1-sol`, both at high reasoning effort.

No game was launched, save changed, mod installed, build run or runtime test executed for this architecture review. Existing unrelated checkout edits are outside this task. The final documentation check verifies local links and whitespace and records source-hash comparisons; it is not a runtime test.

During the review, separate work changed Persistent City's three RLO compatibility adapters and added `Compatibility/RloBridge.cs`, a read adapter for RLO's public citizen/reservation/road-speed contract. The remaining 301 inventoried files, including the traffic, persistence and physical-driving sources discussed here, still match the captured baseline. This panel does not approve those concurrent compatibility changes. Gate A must verify its installed mod snapshot; provider compatibility/readiness is rechecked before later integration.

## Second round

All three reviewers returned **READY FOR GATE A**, with acceptance clarifications incorporated into the final scope. They approve implementing the transient native probe only. They do not approve runtime behavior, shared traffic, resumable saves or fleet performance.

| Reviewer | Verdict | Final clarifications incorporated |
| --- | --- | --- |
| Opus 5.5 | Ready for Gate A with corrections | Explicit interruption policy; parking state; empty-fuel/condition holds; guard Food Trucks actions; junction coexistence; contact attribution. Check current provider backlog and save interception before substantial Gate B work. Detect provider failure and establish a version-controlled provider baseline. |
| GPT 6 Astra | Ready for Gate A | Separate physical braking from synchronous interruption hold; restore only while still owning the controller; preserve deferred collision damage; cover a named test matrix. Gate B must have a nonresumable save policy and test exact-boundary transfer without deadlock. |
| GPT 6.1 Sol | Ready for Gate A | Ownership-aware player takeover, actual tank/damage preservation, synchronous hold and save-state recovery. Prevent transient Gate B entries from reaching an unsupported checkpoint; test save-abort bookkeeping and subsequent saves. |

The chosen interruption policy is an explicit stationary hold at the current pose, logged separately from measured physical braking. It may use a synchronous zero-velocity/kinematic hold in this disposable development probe; it is not counted as a braking success. Parking state is recomputed where a moved truck stops rather than blindly restoring its origin state. Contact attribution includes a failure to brake, not only contacts while throttle is applied. These resolve ambiguous reviewer wording while preserving their stated acceptance intent.

The longer-term preference remains split: Astra and Sol favor shared queues; Opus favors estimated background travel. All three accept that Gate A is useful to either approach and that the revised document represents the difference. No consensus on the complete future architecture is claimed. The shared-queue recommendation remains conditional on the capacity, backlog, physical-control and checkpoint gates.

## Implementation handoff

Start with Gate A's pure control seam and stationary native ownership test. Ship no production behavior from the probe. Stop if unattended control, actual braking or recoverable handover cannot be demonstrated. Preserve the fuel/refuelling scope for later, and use the review's early feasibility checks before making the larger provider changes.
