# Gate A pure control seam

Implemented and Core tested; native driving, stopping, envelope clearance and traffic integration remain **unverified in game**. This is a transient fixed-path probe, with no route planning, production crew scheduling, save schema or dependency on Driven Rides.

## Source provenance

Source checkout: sibling `big-ambitions-driven-rides`, git revision **90981d0061418a4d4742792d90c62c6a686f2a80**. The source is MIT, copyright (c) 2026 Peter Hartwieg; the same copyright and permission notice is retained in this repository's `LICENSE.txt`.

| Source at that revision | Git blob SHA-1 | File SHA-256 |
| --- | --- | --- |
| `Core/Drive/PursuitDriver.cs` | `ea81d7075b03b41d3351d6f272ef46329fd53d3a` | `067a0052a3343a5697a9c08d86b9aba32ffec34159abe98ac67a4bfc5d274edf` |
| `Core/Drive/DriveLine.cs` | `924b0e0ace8c536deee1eee7705f3ed6b0374770` | `cf082de58076962c04529dc968ada47bcdb6bf9448e8a3e654cbb51f5ce18e2a` |
| `Core/Drive/CarModel.cs` | `8addbfadb3d6fd2a0d66cb025e3203a607d52593` | `05800926ac3bc28bdd0f5030d4d691160eb3f5b36fc8ea71e928d86a72c8f955` |

The inspected working tree's `PursuitDriver.cs` had one uncommitted stationary-obstacle braking addition (SHA-256 `a47652537b8f21619a030d4aca9d5bd5487761e01f041b5a8ba2cf7d75ee377f`). The port uses the unchanged equations identified below, checked against `git show HEAD:<file>`; it does not depend on that uncommitted change. The other two inspected files match their revision hashes above.

Minimal equation reuse/adaptation:

- `PursuitDriver.Step` and `LookaheadAt`: heading `atan2(dx,dz)`, lookahead `max(wheelbase + 1.5, 1.5 + 0.45*speed)`, rear-axle pure pursuit `k = 2*sin(alpha)/max(1,distance)`, road-wheel angle `atan(wheelbase*k)` for a plain bicycle. Food Trucks omits the predictive yaw/feed-forward and NWH Ackermann dependency tree.
- `PursuitDriver.Approach` / `Need`, and `Follow`'s reaction-time form: stopping `v² = 2ad` and `v = a*(sqrt(t² + 2d/a) - t)`. Food Trucks adds a one-metre front margin and one timestep of reserve, treats every observed obstacle as stationary, and explicitly measures front-corner plane crossings.
- `PursuitDriver`'s standstill handbrake rule: do not keep applying service brake at a standstill where NWH can choose reverse. No reverse command is generated.
- `DriveLine.Locate`: dot-product projection and signed lateral offset, rewritten to clip the projection itself to both ends of a bounded local window. `CornerSpeeds`: sampled heading-change curvature and `sqrt(lateralAcceleration/curvature)`, with a conservative whole-probe cap and **no minimum corner speed**.
- `CarModel`'s plain bicycle curvature `tan(wheelAngle)/wheelbase` informs the test rig. Tests integrate actual pedals and smoothed/rate-limited steering, including optional command latency; they never assign actual speed from desired speed.

No game/decompiled code, native lifecycle, planner, `Ride`, `OwnCarDrive`, `NwhDrive`, traffic kernel, parking system or shared DLL is copied.

## Public API and coordinate contract

Namespace: `FoodTrucks.Core.CrewDriving`. All position/distance quantities are **metres**, speed is **m/s**, timestep/response time is **physics seconds**, fuel is **litres**, acceleration is **m/s²**. Time is not game minutes. Heading is `atan2(forwardX,forwardZ)` **radians**; road-wheel steering is **degrees**, positive toward +X from +Z (right).

| File / API | Runtime use |
| --- | --- |
| `ProbeTypes.cs`: `ProbePoint`, `ProbeGeometry` | Supply X/Z points and measured wheelbase, front/rear bumper offsets **from the rear axle**, full body width, minimum usable steering lock over the chosen speed range, and maximum speed. Geometry is immutable. |
| `ProbePath.cs`: `new ProbePath(points)` | Copies/validates the fixed directed path. `Length`, `MaxCurvature`, `ChordErrorBound`, `ValidateGeometry(geometry)` and `RequiredSweptRadius(geometry)` expose validation/envelope information. |
| `ProbePath.Sample(s)`, `HeadingAt(s)` | Allocation-free binary-search sampling, clamped to `[0,Length]`; nonfinite input throws. Coordinates/markers always describe rear-axle distance along the path. |
| `ProbePath.TryProject(x,z,from,to,out projection)` | Allocation-free nearest projection strictly within `[from,to]`, with at most 100 m of path searched and at most 2047 segments. Invalid windows/numbers return false. Projection reports along, signed lateral, full Euclidean miss, tangent heading and nearest point. Endpoint misses include longitudinal error. |
| `ProbeOwnership(session)` | One main-thread registry per active save session; supports independent native vehicles. `TryAcquire(nativeVehicleId,controllerInstance,out token)`, `Owns(token)`, `Release(token)`, `Count`. Controller instance is a nonzero identity for the actual native component. |
| `ProbeController(owners,token,path,geometry)` | Binds a live token to exactly one single-use pure controller. A second controller for that generation is refused. Validate geometry/path before changing native settings. |
| `ProbeController.Step(token,observation)` | Allocation-free physics decision. State is Starting, Driving, Holding, Finished or Cancelled. Starts near path origin at rest, waits while `NativeReady=false`. No automatic restart after cancellation. |
| `ProbeController.Cancel(token)` | Idempotently revokes driving and requests a synchronous native stationary hold; retains ownership for cleanup. Stale cancellation cannot mutate another controller. |

`ProbeObservation` contains rear-axle X/Z, heading, signed speed, timestep, actual road-wheel angle, readiness/control/fuel/condition flags, optional `StopBoundaryAlong` and optional `GapAhead`. Null stop boundary means no denied signal; null gap means no detected obstacle. A stop/signal marker's coordinate is rear-axle path distance, but its tangent plane restricts the vehicle's **FRONT**, including both front corners. `GapAhead` is already free distance from the actual foremost body to the obstacle, never distance from the vehicle pivot or rear axle. The observer must refresh permissions/gaps every step and treat query overflow/unknown visibility as a stop or cancellation; null is not an observation-failure fallback.

`ProbeCommand` supplies wheel degrees, throttle/brake/handbrake in `[0,1]`, desired speed, state/reason, `RearAlong`, `StopBoundaryAlong`, `FrontBoundaryDistance`, `BoundaryViolationMetres`, `MayApply` and `RequiresStationaryHold`. Rear progress is monotonic and bounded by local projection/observed displacement; it never authorizes teleporting. Clearance always retains the signed actual front-corner/tangent-plane measurement; far away it additionally takes the smaller conservative route-distance bound. Negative clearance measures physical penetration independently of clamped rear progress, even after a large off-corridor jump. The prepared path must remain on the permitted approach side of the endpoint and any supplied signal plane; winding past a plane on an earlier leg is deliberately refused. Obstacle gap can reduce clearance; `StopBoundaryAlong` still identifies the nearest route plane, not a synthetic obstacle marker.

Stop with handbrake near standstill; do not turn these commands into reverse gear. The adapter must preserve forward-only native operation. `MayApply=false` means apply **nothing**, including stop pedals, to a potentially newer owner. Recheck `Owns(token)` and native component identity immediately before every native write/restore. `RequiresStationaryHold=true` requests immediate interruption cleanup, without waiting for another physics frame; it is not a successful physical-braking result.

Ownership lifecycle: acquire once; bind controller; drive/hold; cancel or finish; revoke callbacks/commands; synchronously hold as needed; synchronize current native pose/tank/damage/parking and restore captured settings only while the exact token still owns the component; **then** release. Releasing twice has no further effect and returns false the second time. Keep ownership after failed cleanup. If construction fails after acquisition, release that unused token after any necessary native cleanup. Dispose of the session registry only after all captured native bodies have been cleaned up. Separate registries are isolated authorities, not a substitute for sharing the one live session registry across adapters.

## Validation, limits and reasons

Paths have 2–2048 finite points, coordinates within ±100,000 m, segments 0.1–100 m and total length at most 1000 m. Degenerate, retraced and self-intersecting paths are refused. A turn must be supplied as chords of an **externally prevalidated smooth forward curve**: adjacent turning segments at most 2 m, heading change at most 0.12 rad, chord-error estimate at most 0.04 m, and sampled curvature at most 65% of `tan(maxSteer)/wheelbase`. The controller does not invent a fillet for raw street-waypoint kinks. These checks do not reconstruct or certify an unknown curve between arbitrary points.

The rear axle may deviate at most 0.6 m from the supplied polyline and heading at most 0.35 rad from its local tangent. `RequiredSweptRadius` conservatively encloses the **entire body** in a tube of radius `hypot(max(front,rear),width/2) + 0.6 + chordErrorBound` around the path. This includes pursuit corner-cutting, front swing and rear overhang, and can be much wider than a lane. The caller must validate that tube, or provide a tighter measured swept-envelope validation accounting for tracking tolerance and native steering lag; passing sampled curvature alone does not establish obstacle/curb clearance. Existing low-level raw waypoint geometry may therefore be refused until a suitable fixed probe path is prepared.

Geometry bounds: wheelbase 1–10 m, front between wheelbase and 15 m, rear 0–8 m, width 1–4 m, minimum lock 5–50°, top speed 0.2–15 m/s. Native steering/yaw must match the assumed effective bicycle wheel angle; NWH input normalization, Ackermann and speed-sensitive lock require adapter validation/calibration.

Reasons and their measurements:

- `None`, `WaitingForNative`: ordinary driving or stationary startup wait.
- `Endpoint`, `Signal`, `Obstacle`: limiting front boundary/gap in metres; Holding can resume after signal/gap release. Only a stopped endpoint becomes Finished. Nominal target is 1 m before the boundary, with a 0.2 m arrival band and stopped speed at most 0.05 m/s.
- `InvalidObservation`: nonfinite/out-of-range pose, speed, wheel, tank or observer boundary. Fuel is 0–10,000 L, wheel ±90°, signed speed −50 through `1.3*maxSpeed+1` m/s, heading ±1000π rad; observer stop is `[0,Length]`, gap −1000 through 10,000 m.
- `InvalidTimestep`: dt outside 0.001–0.1 physics seconds, including zero/NaN. Runtime must cancel before pause, skip, acceleration, save or unload.
- `PositionJump`: translation beyond `max(previousSpeed,currentSpeed)*dt + 1.5*dt² + 0.03` m, or yaw change beyond `maxSpeedObserved*maxCurvature*dt + 0.03` rad.
- `WrongDirection`: signed speed below −0.02 m/s, backward translation over 0.03 m, or projected retreat over 0.03 m. Small measurement jitter does not create reverse commands.
- `OutOfCorridor`, `HeadingMismatch`, `SteeringLimit`: rear miss over 0.6 m, heading error over 0.35 rad, requested steer beyond measured lock, or observed wheel beyond lock plus 0.5°.
- `ControlLost`, `FuelEmpty`, `Wrecked`: lost adapter authority/readiness, enabled tank at zero, or unusable native condition. Disabled fuel permits a zero tank. Failures latch Cancelled with zero throttle and a stop/hold request.
- `BoundaryCrossed`: signed physical front clearance/gap below −0.000001 m. Violation is `max(0,-clearance)` metres, even when rear progress cannot advance. A crossing is checked before rejecting a finite pose jump.
- `Cancelled`, `StaleOwnership`: explicit terminal revocation, or a token/session/native-component/generation mismatch. Stale commands cannot mutate/release the live owner.

## Braking assumptions and verification

The conservative plan assumes at least 1.5 m/s² braking, a 4 m/s² full-brake mapping, response latency no more than 0.3 s plus the current timestep, and 0.8 m/s² lateral acceleration. The tightest sampled turn caps the whole short probe, with no copied high minimum corner speed. Actual tyre grip, slope, body inertia, steering response, gearbox and handbrake effectiveness are not proven by these equations. A newly revealed close obstacle can already be physically unstoppable; full brake is an attempt, not permission or proof. Native Gate A must measure these bounds and actual front/body stopping at relevant speed/timestep combinations before asserting successful stops.

Tests exercise pedal-driven bicycle motion at 0.02/0.05/0.1 s, up to 15 m/s, delayed command response, a 90° sampled turn, signal and obstacle stop/release, front-centre/corner/far overshoot, reversed/jumping/drifting/invalid observations, fuel/wreck/control loss, independent controllers/sessions, duplicate/stale ownership, lifecycle idempotence and zero managed allocation in 10,000 steady steps plus sampling/projection. These are Core checks only, not native collision/braking or frame-cost evidence.

Normal focused command:

```sh
dotnet test tests/FoodTrucks.Core.Tests.csproj --no-restore --filter FullyQualifiedName~CrewDriving --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false
```

The restricted local sandbox denies VSTest sockets. `tests/CrewDrivingRunner.cs` is an optional actual in-process xUnit host guarded by `CREW_DRIVE_RUNNER`; compile it directly with the SDK's `csc.dll`, net10 reference assemblies and the already-built xUnit runner/abstractions DLLs, producing `tests/bin/Debug/net10.0/CrewDrivingRunner.dll`. Run with `dotnet exec --runtimeconfig tests/bin/Debug/net10.0/FoodTrucks.Core.Tests.runtimeconfig.json tests/bin/Debug/net10.0/CrewDrivingRunner.dll tests/bin/Debug/net10.0/FoodTrucks.Core.Tests.dll`. Its filter selects only these CrewDriving classes; ordinary test discovery is unchanged.

## Prepared native geometry (implementation follow-up)

`ProbeRoadPath` is new Food Trucks code. It certifies bounded cubic interpolation of a fixed connected chain inside a 0.5 m capsule per original segment, then samples at 0.1–1 m with exact marker mappings. Continuous curvature, strict forward projection, 2,048 samples, 1,000 m total length, recursion depth 14 and 131,072 certification visits bound admission. It is not a city router or a clearance guarantee. The native adapter retains original signal planes, uses the prepared curve for steering/projection/sweeps, and refuses unsupported geometry rather than widening the corridor. Tests use synthetic geometry, not captured game measurements.

After review, Gate A no longer overrides collider layers. It admits only natively visible colliders and retains only explicit occupied-junction records after interruption. Physical stop assumptions and conservative adjacent-lane clearance remain pending native evidence.
