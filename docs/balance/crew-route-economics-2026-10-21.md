# Crew route economics: physical travel sensitivity

21 October 2026. Calculated, not checked in game. The question was whether physical crew travel leaves routes commercially useful. The study uses the balance calculator's demand, expected queue throughput, finite stock and input tables. Traffic, fuel, station access and clock inputs are assumptions, not a calibrated forecast. No shipped tuning changed.

Route-level numbers, sensitivity grids, policy, configuration and input hashes for both price cases are in [crew-route-economics-2026-10-21.json](crew-route-economics-2026-10-21.json). This page keeps the conclusions and the assumptions they depend on.

## Result

All 12 selected moving routes stay profitable at default prices in the ordinary full-tank scenarios. Profit falls a lot, though, and once physical travel is included every moving route earns less than its matched one-pitch alternative. Staying already wins 10 of the 12 pairs under the old timer. The two moving routes that narrowly beat staying under the timer lose that lead with physical travel.

Median weekly operating profit drops from $2,584 under the timer to $2,141 with "normal" travel (8 m/s, 3 minutes wait per leg) and $1,163 with "heavy" travel (4 m/s, 10 minutes). Normal traffic costs a median 16 lunch minutes a day, heavy 40.

The main cost is lost selling time inside the same paid shifts. Fuel itself costs about $10 to $27 per route-week in the ordinary cases. A refuelling detour costs much more than the petrol. With low starting fuel and heavy traffic, 2 of 12 routes go unprofitable and moving cases skip one stop during the refuelling week.

The comparison is stock-constrained. At default prices every case sells out its 80 hot dogs on all five weekdays, so a better second pitch cannot sell hot dogs that are no longer on board. Staying's advantage is therefore not all traffic. The study does not measure how a midday restock or another menu would change it. A ceiling-price run picked the same stops and reached the same verdict.

This supports fewer moves for these pairs. It does not show that moving is always a bad strategy. The route heuristic does not optimize a complete route.

## What is held equal

- One two-person truck, both workers on fixed 07:00 to 19:00 shifts at skill 60 and satisfaction 85. That is 168 paid employee-hours and $4,667.19 in wages a week, travel and idle time included. These are proposed fixed shifts, not production AutoSchedule.
- A dry model week of 4 to 10 May 2026 with weekday demand, default prices, market fallback 100 with the existing per-product overrides, and four pieces: hot dogs, coffee, croissants and soda.
- Morning loading takes 30 game minutes. The truck tops up each morning from an adequately supplied commissary to 80 hot dogs, 320 coffees, 160 croissants and 160 sodas. Leftovers carry forward. Nothing refills between pitches. Sales are fractional expected quantities from the five-minute calculator, not native customer events.
- Three moving windows: 08:00 to 11:30, 11:30 to 14:30 and 14:30 to 18:00. Travel shortens the windows and closing does not move later. The model reserves time and fuel to be home by 19:00 and skips a stop that cannot fit.
- The matched single pitch is the best of the moving route's three pitches under the timer, frozen once. It sells 08:00 to 18:00 with the same depot, loading, stock, wages, return policy and scenario travel. Neither option is reoptimized for physical traffic.
- Rent is $41.10 a day and delivery allocation $250 a week. Goods cost is deducted only when sold. Commissary inventory and purchase funding are assumed sufficient. Midnight cash flow and delivery arrival are not simulated.
- Route selection: for each district and p25/p50/p90 anchor, the best downstream pair within total straight distance `[0,700)` or `[700,1600)` metres by Wednesday window contribution. The "long" routes are only 703 to 811 straight metres including depot legs. This is a bounded heuristic, not a claim about optimal or typical player routes.

## Travel and fuel caveats

- The timer case uses `Core.Fleet.Travel.Minutes` inside the same dispatch policy with no fuel cost. It is a timing counterfactual, not a replay of the shipping route runner.
- Physical time is `ceil(straight metres x 1.3 / speed m/s) + per-leg wait`, with one real second per game minute. Coordinates come from the balance model's approximate map frame, which has 15 to 35% distance uncertainty. Native bay coordinates use a different frame and are excluded. Bay offsets and legal parking are unknown.
- The zero-wait, no-fuel control at 32.5 m/s reproduces every timer result. That speed is above the truck asset's 50 km/h top speed and is a time-compression control, not a driving setting.
- Fuel: 80 L tank, 8 L reserve, 2 L/km, $1.30/L and 0.02 L per waiting minute. Engines are off while selling, loading and filling. None of this is calibrated. Ordinary cases start full and do not refill that week, so they are not a steady-state refuelling schedule. Stress cases start at 10 L and refill once through a synthetic 400 m station loop plus 15 minutes of filling.
- The speed and wait grids on the two p50/long routes are not monotonic, because a speed change can change which stops get skipped. The 1 to 2 m/s probe profile is not an acceptable production default for these cases. The 3 and 10 minute waits are illustrations, not measured traffic.

## Native calibration gate

Before production integration, calibrate on native routes: complete journey times, road lengths, the modpack's game clock, signal, queue and parking delays, fuel consumption and station visits. Then rerun the same cases and add evening windows, scheduled bay restocks and adapted schedules.

Out of scope: regulars, reputation, health, own-fleet competition, catering, season and rain, spoilage, station congestion, stochastic traffic, repairs, fines, tax, capital costs and financing. The full-supply, fixed-shift, daytime assumptions limit any wider profitability claim.

## Reproduction

```sh
dotnet build tools/balance/Balance.csproj
dotnet tools/balance/bin/Debug/net10.0/balance.dll \
  --reports crew-economics,crew-economics-checks --pricing default \
  --out build/crew-economics/current.md --json-out build/crew-economics/current.json
```

Use `--pricing ceiling` for the second price case. `--crew-economics-config` takes a versioned assumption file. The run uses the current checkout's inputs, so compare its input hashes with the tracked JSON before expecting identical numbers.
