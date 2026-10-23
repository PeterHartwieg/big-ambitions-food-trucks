# Crew driving spike

Revised after the independent architecture reviews on 21 October 2026. The immediate scope is **Gate A, an unattended native driving probe**. Gates B to D define the path toward the [crew driving and fuel proposal](crew-driving-and-fuel.md); they are not part of the first implementation. This planning task changes no runtime code and supplies no new gameplay evidence.

## Architecture decision

Use a dedicated per-truck controller in Food Trucks, drawing only the necessary steering and geometry code from Driven Rides. Keep Persistent City optional. For the eventual integration, prefer a separate external-journey registry inside Persistent City using its existing traffic kernel. Food Trucks retains business intent and native vehicle ownership; Persistent City becomes the sole authority for accepted road progress and congestion.

The panel agreed on the unattended probe, per-vehicle ownership, limited source reuse and aggregate performance budgets. Astra and Sol recommend shared traffic ownership. Opus recommends Food Trucks-owned background timetables using Persistent City only for estimates. The latter is simpler and independent of provider backlog, but never owns a place in the shared queue: background delays are estimates and background trucks do not delay residents. Retain it as a comparison baseline or an explicitly different future mode, not as an unnoticed substitute for shared congestion.

Do not start a shared transport-library refactor, introduce a Driven Rides Workshop dependency, or invent citizens for trucks. Do not migrate saves or build a cross-mod API until the native probe passes. A failed probe leaves the existing route implementation usable and the future architecture open for revision.

## Gate A scope

Prove that one **actual owned food truck**, with nobody in it, can drive a short validated road path, turn, wait for visible traffic and a signal, stop at a specified endpoint, and hand control back safely. The player can remain nearby in their own vehicle or on foot. Persistent City should be tested both enabled and absent, but this probe does not submit the truck to its logical traffic kernel.

Use a disposable test truck and a prepared route of roughly 200 metres. Begin aligned in a valid driving lane; finish at a known clear stopping position. Retain the stopped native body at the endpoint or hand it to the player; never delete it while it is still a physical road obstacle. Full parking manoeuvres and finding suitable parking positions are separate work.

### Included work

| Work item | Concrete deliverable |
| --- | --- |
| Controller lifecycle | A `CrewDriveProbe` adapter keyed by save session, native vehicle ID, controller instance and generation, with explicit Starting, Driving, Holding, Finished and Cancelled states. Reject duplicate ownership and stale callbacks. |
| Native startup | Wait for native initialization and its stationary-after-load handling; capture input, physics, engine and other changed settings. Start unattended physics with cached native bindings, without entering the player or setting `controlledByPlayer`. |
| Steering and stopping | Reuse the minimum pure pursuit/geometry/braking code in a Food Trucks namespace with source revision and tests recorded. Drive a fixed validated path; stop before a supplied boundary with a conservative braking margin. Do not use `Ride`, `OwnCarDrive`, `NwhCar.Spawn` or full `NwhDrive` unchanged. |
| Traffic observation | Observe nearby Gley cars, protected service vehicles, player vehicles and other owned NWH vehicles. Read native signal permission. Vehicle collider registration and junction participation must be explicit and reversible. Avoid Driven Rides' shared `VehicleEnter(-1)` / `VehicleLeft(-1)` identity (`GleyTraffic.cs`) and test coexistence with a player ride through the same junction. |
| Ownership cleanup | Stop commands before releasing ownership; synchronize native pose, tank, damage and parking state; restore captured control settings rather than assuming player defaults. Handle normal completion, cancellation, player takeover, native destruction, save/unload and invalid geometry. |
| Diagnostics | A development-only bridge operation or fixture to start, inspect and stop the probe. Report native ID, controller generation, phase, path progress, speed, stopping boundary, control owner, fuel before/after and remaining subscriptions/registrations. |
| Measurements | Record control/observation/physics cost and allocations, stopping accuracy, contacts and player-side effects. One physical truck is the initial cap. |

Native fuel consumption stays enabled as configured by the game and installed mods. Record its change and synchronize it before cleanup. Do not refill the tank or introduce background consumption in this probe. Cargo, equipment and money are never changed by the probe.

Explicitly check usable fuel and vehicle condition while driving; an empty enabled tank or a wrecked truck triggers a safe hold/termination. The native player-only update cannot be assumed to enforce these for an unattended truck. Capture origin parking state, use the appropriate moving state while driving, and evaluate the actual position on finish/cancellation without manipulating the player's HUD. Restore the origin parking state only if the truck never moved; never label a moved truck from its old parking location.

The probe uses temporary in-memory state and introduces **no journey save schema**. Before saving, pause, native acceleration, a time-machine skip or unload, stop the probe and synchronize the truck through the native save path. Reload produces an ordinary stopped owned vehicle, not a resumed probe. Pause advances nothing; resuming a cancelled probe requires a new explicit start. Full journey continuation through these transitions belongs to Gate C.

Interruption cleanup cannot depend on another physics frame. Revoke commands and establish a synchronous stationary hold at the current pose before serialization or suspension proceeds; handle residual velocity and native state explicitly. If this disposable probe needs a zero-velocity/kinematic hold, log it as interruption cleanup and exclude it from braking-success results. Endpoint, obstacle and signal stops must demonstrate actual physical braking.

Restore captured settings only while the same probe generation still owns that native controller. For player takeover, remove probe overrides before native entry, then let native entry establish player input/engine/physics. A delayed cleanup must not overwrite a newer owner or save session. Restoring settings never restores the starting tank or damage. Native `SavePosition` synchronizes pose and fuel; collision damage has a separate deferred callback, so verify current native damage independently without replaying collision effects or repairing it.

Existing crew routes must not run the probe vehicle. Use a disposable truck without an active crew schedule, and an explicit transient ownership guard against conflicting legacy removal or player-ride control. Refuse Open for business, hatch serving, restocking and bridge/autopark teleports while the probe owns it; sale/deletion must first end that ownership. No production route behavior or `ArrivesAt` semantics changes in Gate A.

### Build sequence

1. **Pure control seam.** Define per-vehicle ownership and generation checks, bounded progress, stopping commands and idempotent cleanup. Port the smallest steering/geometry subset with provenance and relevant tests. A two-controller harness checks independence without requiring two native trucks yet.
2. **Stationary native ownership.** Acquire a parked/held test truck after initialization, start and stop its engine/control, and hand it back. Verify player selection, HUD, camera and clock remain unaffected. Stop here if native ownership cannot be isolated safely.
3. **Straight drive and controlled stop.** Exercise the actual truck at low speed, then the relevant operating speeds up to its configured limit. Measure stopping distance and timestep effects. Increasing reported progress must never hide an actual boundary overshoot.
4. **Turn and visible traffic.** Run the prepared road path with a signal and queued traffic, including a player vehicle as an obstacle. Validate the truck's turning envelope and both directions of collision/avoidance interaction with Persistent City's visible cars.
5. **Interruption and evidence.** Repeat completion/cancel/takeover cycles and save, pause, speed-change and unload interruptions. Preserve source/build hashes, logs, diagnostic results and representative rendered observations.

### Acceptance and stop conditions

- One native vehicle ID and at most one probe controller own the truck. Duplicate starts are refused; stale callbacks cannot regain control.
- The truck moves without a player in it, follows the directed path, negotiates the selected turn and stops before commanded boundaries. Test physical front/body coordinates, not just clamped logical progress.
- Native red signals and stationary traffic are obeyed. The provisional acceptance campaign is 20 completed controlled routes with no probe-caused contact or forbidden boundary crossing. Log every contact with pre-contact motion, commands and braking context. Attribute contacts to the probe when its commanded motion or failure to stop initiated the collision; classify an independently moving vehicle striking the held truck separately. Ambiguous attribution requires inspection rather than an automatic pass. This is a bounded regression gate, not a general safety guarantee.
- Takeover stops crew commands before player input begins. Cancellation and ordinary ending restore captured settings and leave the native truck accessible. A failed cleanup holds the body safely and reports failure rather than destroying it or starting another controller.
- Fifty repeated start/stop/interruption cycles leave no probe controllers, stale callbacks, save subscriptions or junction registrations. Do not count legitimate native subscriptions as leaks.
- Save after interruption and reload retain vehicle identity, cargo, equipment, damage and the actual remaining tank. The probe never resumes silently after load.
- No probe-induced player attachment, selection change, camera change, global clock modification or inappropriate player-only HUD/to-do occurs. Explicit player takeover may change selection through native entry. Native game reactions to a real collision or ordinary vehicle interaction remain valid.
- Normal-speed CPU and allocations are reported against a matched baseline, including physics and observation work. No fleet performance claim follows from this one-truck result.

The campaign must explicitly cover Persistent City enabled/absent, player on foot/in another vehicle, red signal, stationary obstruction, takeover, save, pause, speed change and unload. Include interruptions during Starting and Holding, absent/replaced native controllers, empty-fuel/condition holds, conflicting Food Trucks actions and Driven Rides junction coexistence. Separately test a legitimate contact immediately followed by save/destruction to verify damage preservation. The twenty contact-free drives and fifty lifecycle cycles must cover these categories; a full Cartesian product is unnecessary.

Stop and revise if the native engine/controller cannot operate independently of player state, if measured stopping cannot respect boundaries, if nearby traffic cannot see the truck, or if cleanup leaves the vehicle inaccessible. Do not compensate by setting the truck as the player's active vehicle or by teleporting it past a blocked segment.

## Future ownership and packaging

The following decisions guide Gate B; they do not expand Gate A.

| Component | Authority |
| --- | --- |
| Food Trucks Core | Business route intent, crew eligibility, legacy-versus-provider backend, and eventual parked-and-ready-to-sell decision |
| Food Trucks runtime | Owned native vehicle lifetime, actual cargo/tank/damage, per-truck physical controller, recovery checkpoint and player handover |
| Persistent City external registry | Accepted route geometry, source admission, queue occupancy, permitted progress, background motion and retained terminal result |
| Shared traffic kernel | One queue/capacity model for residents and external vehicles; no duplicate truck entry |

Add explicit external-owner dispatch to restore, network rebind, completion, lease validation, save validation and clock conversion (`TrafficJourneys`, `PhysicalJourneys`, `SaveCodec`, `TravelTiming`). Preserve resident behavior and identities. `TrafficKernel.Cancel` already exists; do not rebuild the city to cancel a truck.

Use a provider-owned versioned facade, discovered once per save/provider session and called through cached delegates. Use BCL primitives and detached arrays/caller-owned buffers; copy route geometry on acceptance, not each frame. No implementation-assembly dependency or duplicated Core/contract DLL. The inspected native `ModAssemblyLoader.LoadAssemblyFromPathAsync` and `RegisterLoadedAssembly` deduplicate assemblies by simple name, so a shared contract DLL distributed in independently updated mods adds unnecessary version coupling for this first consumer.

Readiness includes the active save, compatible provider/dependency, current topology and supported checkpoint semantics. The key includes owner namespace, native vehicle ID, journey ID and durable generation; runtime lease tokens also include provider-session generation. Same-key/same-content submission is idempotent; changed content under that key is refused. Acceptance, source admission, physical admission and terminal acknowledgement are separate states.

Expose committed directed geometry, topology/route revision, a defined front-reference point, segment/fraction progress, cumulative metres and processed-through time. Nominal route minutes are geometry coordinates, not elapsed travel time. `CanCrossBoundary` is advisory: physical motion waits for the kernel's actual transfer of ownership. Clamping a report after NWH has crossed a boundary is not enforcement.

Do not reuse a Gley body-invalidation response that destroys a pooled car for an owned NWH truck. Overshoot or lost permission must stop commands, retain a physical obstruction/ownership as necessary, and enter a diagnostic hold. Release to background only after confirmed physical cleanup. Releasing visibility never cancels traffic demand.

## Gate B traffic and capacity

Before investing in weighted storage, run two feasibility checks alongside or immediately after Gate A: measure the current resident-only provider backlog at normal speed, 6x and sleep/skips; and prove a pre-serialization interception can abort a prepared save safely. Use the normal route windows to judge whether backlog leaves useful selling time. These checks can reject the shared-queue design early; they do not implement Gate C's full checkpoint protocol. Establish a version-controlled Persistent City baseline before changing its currently uncommitted source; this planning task does not commit that other project's work.

Before coupling the probe to production traffic, prove two external trucks and residents share source waits, queues, signals, cancellation and terminal results in an offline harness. Prototype conservative weighted storage, initially two six-metre units per truck with one journey identity. Apply the same footprint to admission, following distance, partial exits, forecasts, blocked-cycle detection and restore validation. Never model a truck as two dummy vehicles.

This is a design candidate, not a claim that two units fit every route. Survey native section lengths and reject undersized sections; measure whether useful routes remain. If short sections make this impractical, revise the section/footprint representation before integration rather than silently downgrading trucks to one unit. Rear clearance across section and junction boundaries requires an explicit hold/reservation policy. A road endpoint retains occupancy while a physical truck still obstructs the section, or completes only after reaching a validated refuge outside it.

The one-truck live integration follows only after Gate A stopping and Gate B capacity tests pass. It must stop behind a real resident queue and retain that queue position when the player moves away. It still does not open for business or search for parking.

Until Gate C passes, the live Gate B demonstration is explicitly nonresumable and uses a disposable session. Verify cancellation, queue release and native synchronization before saving/unloading, or defer this live portion until coordinated persistence is available. Never let the normal provider save handler serialize unsupported or uncoordinated external entries. At exact traffic boundaries, test stop, observation, actual ownership transfer and subsequent advance; a conservative stopping setback must not deadlock the transfer it awaits.

## Gate C continuity and persistence

Persistent City alone persists and advances the committed road journey. Food Trucks stores a recovery envelope: native vehicle reference, journey/generation, common checkpoint revision, last validated pose, recovery location/phase and cancellation tombstone. The envelope never advances progress independently. Native vehicle state remains authoritative for tank, cargo and damage.

Independent `onSaveGame` subscription order is not a checkpoint protocol. Before native serialization, a coordinator must stop ownership transitions, synchronize physical observations and native vehicle state, prepare and validate both detached payloads, and publish a common checkpoint. Existing save callbacks must not overwrite the prepared result. Validate a save guard that aborts an inconsistent write if preparation fails; merely preserving old mod JSON beside a newer native pose is insufficient. This coordination must be demonstrated at the real save boundary before resumable journeys ship. An inability to intercept that boundary is a Gate C blocker, not an assumption that can be ignored.

Test preparation failure separately from native serialization failure and restore save-in-progress bookkeeping so later saves remain usable. Throwing from a save callback is not a sufficient abort mechanism: native save bookkeeping has already begun by then.

Reconcile before external traffic advances or any body is published:

- Matching checkpoints resume in background mode with new runtime leases.
- An owner missing after explicit discovery completion permits removal of its orphan traffic entry. Never infer absence from a timeout while Food Trucks is still loading.
- Provider unavailable, removed or failed mid-session: Food Trucks holds the native truck at a validated recovery position, records cancellation of that generation, and never advances the old journey by a second clock. The facade must become unready when Persistent City disables itself or stops saving. A returning provider consumes the tombstone before accepting new work.
- Mismatched checkpoints, changed topology and unreadable/future schemas preserve protected data and refuse automatic resume. Do not downgrade an active new-format journey to an older timer merely because extension fields survive.
- Cancellation revokes commands and callbacks first. A physically obstructing truck continues to be represented until it clears or control transfers to a native obstacle owner. Cleanup must not erase a real obstruction from both models.

The contract clock is fractional game minutes since day-one midnight. Convert Persistent City's current epoch internally and sample after the native hourly loop. Keep native time, provider processed time and observation time separate. On native 6x speed, indoors and time skips, synchronize/retire physical leases before background advance. During backlog, admission and commands use valid permissions without extrapolation.

Do not freeze the entire Food Trucks sales cursor while one journey lags: `HireRoutes.RoutedThrough` feeds a fleet-wide sales watermark. Other vehicles must continue their own selling and midnight settlement. A delayed road completion never authorizes historical selling; service requires a later explicit parking result at the actual Food Trucks processing time. If provider lag makes routes unusable, shared-queue integration fails its performance gate rather than pretending the truck arrived.

## Gate D fleet and performance

Use one shared road snapshot/kernel, bounded physical bodies and aggregate scheduling. Do not give each truck Driven Rides' existing three-millisecond per-ride lookahead allowance. Unity reads stay on the main thread. Start with bounded single-threaded work; background workers require immutable versioned inputs and a measured benefit.

Provisional targets for review and calibration, not measured results:

| Work | Initial target |
| --- | --- |
| Gate A physical bodies | One; prove two independent controllers offline |
| Fleet experiment | 0, 1, 4 and 16 logical trucks; measure 1, 2 and up to 4 physical trucks separately |
| Aggregate added frame work | p95 at most 1 ms, p99 at most 2 ms, including added physics/observation work |
| Planning/admission | One search in flight and one materialization attempt per frame; 0.5 ms soft aggregate planning slice; investigate individual synchronous stalls above 5 ms |
| Background traffic | Share Persistent City's existing event/time allowance; no per-truck copy of that budget |
| Allocation | Zero steady allocations in controller/progress steps; separately bound and report planning, snapshot and transition allocations |
| Sixteen-journey checkpoint | Initial target at most 256 KiB added payload and 20 ms p95 added capture/serialization time |
| Backlog | No steadily increasing lag at normal speed; compare accelerated lag and time to catch up against the matched resident-only baseline |

Measure p50/p95/p99/worst CPU, fixed-step work, managed allocation, searches, body transitions, permission overshoots, save/load cost and total provider lag. Compare identical saved traffic state and scripted camera paths with the candidate disabled/enabled. Distinguish congestion delay from simulation backlog. Historical reports of approximately 90 minutes of accelerated backlog are a risk indicator from an earlier run, not a current bound or proof of acceptable performance.

Inspect full-pool observation scans, queue permission scans, forecast projection allocations, resident selection/sorting and serialization. Share observation snapshots only if profiling warrants it; disable expensive diagnostic dumps during timing. No FPS claim follows from synthetic .NET tests. Native scalability and rendered appearance are separate acceptance gates.

## Route economics before production integration

Peter requested a profitability comparison on 21 October 2026. Physical movement must also work as a business mechanic: completing a drive does not establish that existing routes still earn useful returns. Compare fixed route definitions, selling windows, menus, stock and paid shifts under the legacy timer, ordinary traffic assumptions, heavy traffic and fuel-stop stress. Include a matched single-pitch alternative, actual weekday demand, lost lunch service, skipped stops, return-to-bay feasibility and daily/weekly operating profit. Separate fuel consumed as an expense from cash spent refilling the tank.

An assumption-based sensitivity study can expose where schedules need adjustment before the full driving backend exists. It cannot establish the shipped profitability impact. Before production integration, replace its travel, clock, station and consumption assumptions with representative measurements under the installed modpack, including road detours, signal/queue delay and parking. Review any baseline-profitable routes that become loss-making, or cases where moving loses its advantage, as gameplay decisions. Do not silently compensate with extra sales, free labour or fuel, higher prices, or backdated arrivals. Keep the economic result separate from physics correctness and CPU performance acceptance.

The [first conditional study](balance/crew-route-economics-2026-10-21.md) now compares 12 fixed routes and matched single pitches over a week. It finds lower profits from the assumed physical travel, with time and refuelling detours more consequential than fuel cost. Its stock limits and heuristic route selection matter: staying already wins 10 of 12 legacy pairs. Native economic calibration remains pending; no tuning decision follows automatically from this study.

## Deferred functionality

Production departure/parking manoeuvres, full crew schedules and pay consequences, route-editor ETAs, automatic replanning, refuelling and money/ledger changes follow these gates. Legacy hiding, timer arrival, recovery, `HireCrew.Check` and crew removal must recognize the backend before provider journeys enter the real route runner. New trips can use legacy travel when the optional provider is absent; an accepted provider trip must settle ownership before fallback.

The detailed reviews and final disposition belong in the [architecture review record](reviews/2026-10-21-crew-driving.md). No architecture approval establishes that Gate A, B, C or D has passed in game.
