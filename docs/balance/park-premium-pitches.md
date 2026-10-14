# Park premium pitches, 14 October 2026

Two premium pitches use `park` presence 1.0 and the existing premium footfall factor 1.25 and radius 40 m. There is no booking fee. The existing first-come rule applies: the first open vehicle holds the pitch until it closes, and later vehicles get ordinary demand.

## Evidence and selection

The read-only `parks` bridge capture (14 October 2026, session `e4cd4ee8`, operation 8; local `build/parks.json`) observed three `parkarea` volumes and nearby building entrances. These are building entrance anchors near parks, **not confirmed park gates**. A live bridge check confirms legal parking and a passing hatch rule at Lower Manhattan and Hell's Kitchen positions below. All visible queues, premium claims and map presentation remain **pending**. Industry City's proposed premium anchor is deferred and removed from the shipped pitch data after failed truck placement checks; automatic park demand there is unchanged.

| Pitch ID | Neighbourhood | Building entrance | Observed world x, z | Distance from park volume |
|---|---|---|---|---|
| lower-park-eleventh-street | Lower Manhattan | 23 Eleventh Street | -468.21, -326.48 | 4.14 m |
| hells-park-second-avenue | Hell's Kitchen | 5 Second Avenue | 333.10, 294.66 | 32.66 m |

The nearest Lower Manhattan candidate, 14 Tenth Street, overlaps the existing Sixth Avenue event street. Eleventh Street 23 is the next closest reported entrance and about 127 m from that event anchor in the offline city map, above the existing 110 m event separation target. Its premium circle partially overlaps Lower Twelfth Street (anchors about 63 m apart); the existing nearest-entrance selection chooses between them, without stacking both premium bonuses. Check both sides of this boundary in game.

The two closest Hell's Kitchen candidates, Fourth Street 1 and 3, overlap the cinema event street. Second Avenue 5 is the closest reported alternative outside it: about 200 m from the event and 91 m from the nearest premium anchor. The deferred Industry City candidate, Twenty-first Street 25, was about 240 m from its event and 112 m from the nearest premium anchor. These separation estimates use the same region's map anchors in `tools/balance/city.json`, not live entrance distances; verify them live.

## Live placement evidence, 14 October 2026

The local Mac game bridge recorded these checks in session `70663076`, day 4 at 13:01. Evidence is in the integration worktree's local `build/logs/game/mac-release-2026-10-14/park-{lower,hells,industry}-candidates.json` and corresponding `park-*-truck-state.json` files. These generated captures are not shipped or committed.

| Pitch | Truck x, z; heading | Entrance distance | Checked bridge result |
|---|---|---|---|
| Eleventh Street skate park | -479.86, -326.48; 180° | 11.65 m | `legal=true`, `hatch_to_pavement=true` |
| Second Avenue park | 333.10, 302.44; 90° | 7.77 m | `legal=true`, `hatch_to_pavement=true` |
| Twenty-first Street park | -1425.96, -1370.29; 90° and 270° | 28.90 m | `legal=true`, `hatch_to_pavement=false` at both headings |

The Lower Manhattan and Hell's Kitchen results are **checked for parking and the hatch opening rule only**. The tested truck was closed, unstocked and uncrewed: no premium claim, customer sale, visible queue or map marker was checked there. The bridge's `hatch_to_pavement` field reports whether `HatchCheck.Result.Opens` allows opening; its permissive `Unclear` verdict also returns true, so the field is not a visual confirmation of pavement or queue clearance. Lower Manhattan's candidate search reported no short entrance-to-street walk; its parking/hatch result does not establish park-gate pedestrian access. Industry City's new premium addition is **deferred**, with every tested legal candidate unsuitable under the hatch rule.

### Industry City premium pitch deferred

The Twenty-first Street 25 premium addition is removed for this release. Automatic demand from the game's Industry City park volume is unchanged; players may still use ordinary legal pitches there.

The nearest candidate (-1425.96, -1370.29) and westward alternatives (-1429.96, -1370.29), (-1433.96, -1370.29) and (-1437.96, -1370.91) were legal but failed the hatch rule at both 90° and 270°. The other tested legal point (-1385.16, -1348.41), heading 180°, also failed it. Other candidates were refused by `staticUnderHighway`, `BorderLimitCollider` or `TrafficCone`; no forced placement bypass was used. Local evidence: `park-industry-alternative-[0..3].json` and `park-industry-more-alternatives.json` in the same capture directory. No safe truck placement at this new anchor has been demonstrated, so a future premium pitch needs another validated anchor rather than a runtime exception.

## Calculator

Run from the repository root:

```sh
dotnet run --project tools/balance -- --reports pitches
```

Dollars of modeled profit, May, dry Wednesday/Saturday, market demand 100, neighbourhood ceiling pricing, restocking at the nearest wholesaler. The truck column is the calculator's existing two-server truck stand-in. This report compares each address's offline building crowds with the held premium bonus; `city.json` has no park volumes, so the ordinary baseline omits the natural park crowd seen by the runtime. These are balance estimates, not measured game profits or the incremental benefit over a live park.

| Pitch | Hot dog weekday ordinary / held | Hot dog Saturday ordinary / held | Truck weekday ordinary / held |
|---|---|---|---|
| Eleventh Street skate park | $647 / $948 | $202 / $528 | $1,497 / $2,527 |
| Second Avenue park | $943 / $1,337 | $880 / $1,642 | $1,695 / $2,795 |

The gains are below the strongest existing Midtown premium pitches. Keep the existing 1.25 multiplier; park presence supplies the park's weekend appetite and weather/season behavior without a new price or fee.

## Pending live checks

1. At each of the two shipped entrances above, place a permitted food vehicle legally within 40 m, facing the park approach. Confirm the localized name on the Street food map and route picker, and `premium spot <id>` in the opening log. For a truck, check a legal parking lane, hatch to pavement and queue clearance; do not assume the entrance coordinate itself is a parking position.
2. At Lower Manhattan, move between Eleventh Street 23 and the existing Lower Twelfth Street pitch. Confirm the nearer entrance picks the expected pitch in the overlap and both names remain discoverable. At both shipped sites, confirm nearby events do not unexpectedly capture the park approach.
3. Open a second vehicle at the same pitch: only the first holds it. Close the first and reopen the second; it can then claim the pitch.
4. Compare a dry weekend daytime with weekday/night/rain readings. The held pitch should retain a `park` crowd with the existing park demand factors. Record positions and logs; suitability as a park gate and visual presentation require a live check.
