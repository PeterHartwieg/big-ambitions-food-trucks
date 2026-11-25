# Park premium pitches, 14 October 2026

Two premium pitches use `park` presence 1.0 with the existing premium footfall factor 1.25 and radius 40 m. There is no booking fee. The existing first-come rule applies: the first open vehicle holds the pitch until it closes, and later vehicles get ordinary demand. The remaining in-game checks are in [CHECKS.md](../../CHECKS.md#park-pitches).

## Evidence and selection

The read-only `parks` bridge capture (14 October 2026, session `e4cd4ee8`, operation 8, local `build/parks.json`) observed three `parkarea` volumes and nearby building entrances. These are building entrance anchors near parks, not confirmed park gates.

| Pitch ID | Neighbourhood | Building entrance | Observed world x, z | Distance from park volume |
|---|---|---|---|---|
| lower-park-eleventh-street | Lower Manhattan | 23 Eleventh Street | -468.21, -326.48 | 4.14 m |
| hells-park-second-avenue | Hell's Kitchen | 5 Second Avenue | 333.10, 294.66 | 32.66 m |

The nearest Lower Manhattan candidate, 14 Tenth Street, overlaps the Sixth Avenue event street. 23 Eleventh Street is the next closest entrance, about 127 m from that event anchor in the offline city map, above the 110 m event separation target. Its premium circle partly overlaps Lower Twelfth Street (anchors about 63 m apart). Nearest-entrance selection picks one of them, so the two bonuses do not stack.

The two closest Hell's Kitchen candidates, 1 and 3 Fourth Street, overlap the cinema event street. 5 Second Avenue is the closest alternative outside it, about 200 m from the event and 91 m from the nearest premium anchor. These separation estimates use the region's map anchors in `tools/balance/city.json`, not live entrance distances.

## Live placement evidence, 14 October 2026

The local Mac game bridge recorded these checks in session `70663076`, day 4 at 13:01. The captures were local `build/logs/game/mac-release-2026-10-14/park-*.json` files in the integration worktree and are not committed.

| Pitch | Truck x, z; heading | Entrance distance | Bridge result |
|---|---|---|---|
| Eleventh Street skate park | -479.86, -326.48; 180° | 11.65 m | `legal=true`, `hatch_to_pavement=true` |
| Second Avenue park | 333.10, 302.44; 90° | 7.77 m | `legal=true`, `hatch_to_pavement=true` |
| Twenty-first Street park | -1425.96, -1370.29; 90° and 270° | 28.90 m | `legal=true`, `hatch_to_pavement=false` at both headings |

Lower Manhattan and Hell's Kitchen are checked for legal parking and the hatch opening rule only. The test truck was closed, unstocked and uncrewed, so no premium claim, sale, queue or map marker was checked there.

The `hatch_to_pavement` field reports whether `HatchCheck.Result.Opens` allows opening. The permissive `Unclear` verdict also returns true, so the field does not confirm pavement or queue clearance visually. Lower Manhattan's candidate search found no short entrance-to-street walk, and the parking result says nothing about pedestrian access from a park gate.

## Industry City rejected

The proposed Industry City premium pitch at 25 Twenty-first Street is not shipped. It was about 240 m from its event and 112 m from the nearest premium anchor, but no legal truck placement passed the hatch rule. Automatic park demand from the game's Industry City park volume is unchanged, and players can still use ordinary legal pitches there.

The nearest candidate (-1425.96, -1370.29) and the westward alternatives (-1429.96, -1370.29), (-1433.96, -1370.29) and (-1437.96, -1370.91) were legal but failed the hatch rule at 90° and 270°. The other legal point, (-1385.16, -1348.41) at 180°, also failed. The rest were refused by `staticUnderHighway`, `BorderLimitCollider` or `TrafficCone`. No forced placement bypass was used. A future premium pitch here needs another validated anchor, not a runtime exception.

## Calculator

```sh
dotnet run --project tools/balance -- --reports pitches
```

The report compares each address's offline building crowds with the held premium bonus. `city.json` has no park volumes, so the ordinary baseline leaves out the natural park crowd the runtime sees. The figures are balance estimates, not measured profits. They came in below the strongest Midtown premium pitches, so the existing 1.25 multiplier stays.
