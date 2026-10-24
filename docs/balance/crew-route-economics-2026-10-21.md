# Crew route economics: physical travel sensitivity

21 October 2026. **Calculated, not checked in game.** This study answers Peter's request to test whether physical crew travel leaves routes commercially useful. It uses the existing balance calculator's demand, expected queue throughput, finite stock and current input tables. Traffic, fuel, station access and clock inputs are assumptions, not a calibrated Persistent City forecast. No production driving, prices, wages or other shipped tuning changed.

## Result

All 12 selected moving routes remain profitable at default selling prices in the ordinary full-tank scenarios. Profit nevertheless falls substantially, and every moving route earns less than its matched one-pitch alternative once physical travel is included. Staying already wins **10 of 12 legacy pairs**. Two moving routes narrowly beat staying under legacy timing; both lose that advantage under physical travel.

The main cost is lost selling time with the same paid shifts. Fuel itself costs about $10–$27 per route-week in the ordinary scenarios. A refuelling detour can matter much more than the petrol bill.

| Travel case | Median weekly operating profit | Median paired change from legacy | Median paired percentage change | Profitable moving routes |
| --- | ---: | ---: | ---: | ---: |
| Legacy timer under the study's dispatch policy | $2,584 | — | — | 12/12 |
| “Normal” assumption: 8 m/s, 3 game minutes wait per leg | $2,141 | −$390 | −15.4% | 12/12 |
| “Heavy” assumption: 4 m/s, 10 game minutes wait per leg | $1,163 | −$1,277 | −52.9% | 12/12 |
| Normal plus low initial fuel | $1,891 | −$620 | −24.4% | 12/12 |
| Heavy plus low initial fuel | $662 | −$1,839 | −73.2% | 10/12 |

Changes are calculated within each route, then their medians taken; they need not equal subtraction of the median profit columns. Dollars are game dollars. Operating profit deducts sold-stock cost, wages, commissary rent, delivery allocation and fuel consumed; it is before tax, capital costs and the exclusions below. Daily mean profit is each route's weekly result divided by seven, with individual weekday results in the full evidence.

Normal traffic removes a median **16 additional lunch minutes per day** versus the timer; heavy traffic removes **40**. All ordinary cases return home within the paid shift without skipping a stop. Low-fuel heavy moving cases skip one stop each during their refuelling week; these are explicitly reduced operations, even when their remaining sales produce positive profit. Their matched single-pitch alternatives do not skip a stop.

## Paired routes at default prices

The demand rank describes the first pitch's Wednesday full-menu demand, not the completed route's profitability. Later pitches are selected for their expected contribution during their own windows, inside the specified distance band. The table's “long” cases are only **703–811 approximate straight metres in total**, including the depot legs; they are not cross-city routes.

| District / anchor rank / distance band | Total straight m | Legacy weekly profit | Normal | Heavy | Normal matched single pitch |
| --- | ---: | ---: | ---: | ---: | ---: |
| Garment / p25 / local | 672 | $2,428 | $2,056 | $1,164 | $2,778 |
| Garment / p25 / long | 707 | $2,413 | $1,976 | $758 | $2,801 |
| Garment / p50 / local | 609 | $2,540 | $2,203 | $1,420 | $2,778 |
| Garment / p50 / long | 733 | $2,516 | $2,079 | $1,162 | $2,778 |
| Garment / p90 / local | 516 | $2,779 | $2,530 | $1,896 | $2,778 |
| Garment / p90 / long | 703 | $2,701 | $2,281 | $1,410 | $2,620 |
| Lower Manhattan / p25 / local | 527 | $2,211 | $1,929 | $955 | $2,945 |
| Lower Manhattan / p25 / long | 811 | $2,030 | $1,626 | $382 | $2,945 |
| Lower Manhattan / p50 / local | 663 | $2,686 | $2,218 | $622 | $2,945 |
| Lower Manhattan / p50 / long | 735 | $2,628 | $2,073 | $370 | $2,945 |
| Lower Manhattan / p90 / local | 331 | $3,036 | $2,853 | $2,438 | $2,945 |
| Lower Manhattan / p90 / long | 707 | $2,800 | $2,424 | $1,821 | $2,945 |

The two legacy advantages are small: Garment p90/long earns $76 more than its matched single pitch, and Lower Manhattan p90/local earns $86 more. Normal physical travel turns those into disadvantages of $339 and $92 respectively. This supports fewer moves for these particular pairs, not a conclusion that moving is always a bad strategy. The heuristic does not optimize a complete route, there is no midday bay restock, and adjacent windows deliberately expose the opportunity cost of transfers.

**The comparison is also stock-constrained.** At default prices, every legacy moving and single-pitch case sells its 80 hot dogs out on all five weekdays: 60 item-days across each group of 12 cases. Coffee, croissants and soda do not sell out. The same hot-dog limit binds all ordinary normal-traffic cases; in heavy moving cases it binds 35 item-days in total. More demand at another pitch cannot sell hot dogs that are no longer on board. Because staying already wins most legacy pairs, its entire advantage cannot be attributed to traffic. This study does not quantify how much that advantage would change with a midday restock or a different menu.

A separate ceiling-price run selected exactly the same route stops and matched single pitches. All 12 ordinary cases remained profitable, and moving still lost to the matched single in every physical case. Higher prices improved absolute earnings without reversing that comparison. This is a robustness check, not a recommendation to change shipped prices.

## What is held equal

- One two-person truck, both workers on fixed 07:00–19:00 shifts. At skill 60 and satisfaction 85, the existing wage formula gives $27.7809 per employee-hour: **168 paid employee-hours and $4,667.19 wages per week**, including idle/travel time. Extra wages are zero in every scenario. These are proposed fixed shifts, not a simulation of production AutoSchedule.
- A model week of 4–10 May 2026, Monday through Sunday, dry, with weekday demand; dates label the hypothetical week. Default prices, market fallback 100 with existing per-product market overrides, and the same four pieces: hot dogs, coffee, croissants and soda.
- Morning loading takes 30 game minutes. The truck starts empty on Monday and tops up from an adequately supplied commissary each morning to 80 hot dogs, 320 coffees, 160 croissants and 160 sodas. Leftovers carry forward; food never refills between pitches. Sales are fractional expected quantities from the existing five-minute calculator, not individual native customer events.
- Three moving windows: 08:00–11:30, 11:30–14:30 and 14:30–18:00. Travel reduces these windows; closing does not move later. The model stays until the fixed close even if stock sells out. It reserves time and fuel to return home by 19:00 and skips a stop if no service can fit. There is no overtime, backdated sale or daily teleport.
- Matched single pitch: choose the best legacy weekly operating result among that moving route's three pitches once, then freeze it. It may sell continuously from 08:00–18:00, with the same depot, loading, stock, wages, return policy and scenario-specific outward/home travel. Neither option is reoptimized for physical traffic.
- Rent is $41.10/day and delivery allocation $250/week for one vehicle sharing one delivery with nobody else. Goods cost is deducted only when sold. Commissary inventory and purchase funding are assumed sufficient; midnight cash flow and delivery-arrival constraints are not simulated.

For each district and p25/p50/p90 anchor, all distinct downstream pairs within total straight-distance bands `[0,700)` and `[700,1600)` metres are considered. The pair maximizing the sum of its Wednesday nominal-window contributions from a common full load is chosen, with shorter distance then ordinal address breaking ties. The actual weekly simulation carries finite stock across all stops. Selection happens before physical scenarios and is a bounded heuristic, not a claim of optimal or representative player routes. All 12 bands have candidates. Exact addresses, coordinates, windows, selection scores and leg lengths are retained in the [numeric results](crew-route-economics-2026-10-21.json).

## Travel, fuel and sensitivity

Legacy uses `Core.Fleet.Travel.Minutes` unchanged inside the **same proposed dispatch policy**, with no fuel cost. It is a timing counterfactual, not a replay of the complete shipping route runner. Physical time is `ceil(straight metres × 1.3 / speed m/s) + per-leg wait`, assuming one real second equals one game minute. All coordinates come from the balance model's approximate regional map frame. Native bay coordinates are excluded because they use a different frame; bay offsets and legal parking are unknown. The earlier map study estimates 15–35% distance uncertainty.

The configured timer covers a straight kilometre in 40 game minutes. With the assumed 1.3 road factor, the normal physical case takes 162.5 minutes before waits; heavy takes 325. The exact zero-wait/no-fuel control uses 32.5 m/s and reproduces every legacy economic result. That is a mathematical time-compression control, above the truck asset's configured 50 km/h top speed, not an intended driving setting.

Fuel assumptions: 80 L tank, 8 L reserve, 2 L/km, $1.30/L reference price and 0.02 L per game minute of per-leg waiting only. Engines are off while selling, loading and filling. These inputs are uncalibrated. Ordinary cases start with 80 L and do not refill during this week; their weekly numbers are not a steady-state refuelling schedule. Stress cases start with 10 L and each refills once during the week. A morning forecast triggers a synthetic station loop of 400 straight metres each way plus 15 game minutes of filling. With road factor and waits, that is 151 normal or 295 heavy game minutes. Actual empty capacity is purchased at the station; initial fuel is not treated as a free operating resource. Expense is fuel consumed; purchase cash and final tank inventory are separate.

The two p50/long routes have additional crossed speed/fuel grids (1, 2, 4, 6, 8, 12, 16 and 25 m/s; 0.5, 2 and 6 L/km) and waits of 0, 30, 90 and 180 game minutes per leg at 8 m/s. Findings at 2 L/km:

| Assumption | Garment p50/long weekly profit | Lower Manhattan p50/long weekly profit |
| --- | ---: | ---: |
| 2 m/s, 3 minutes wait | −$496, one stop skipped daily | −$2,299 |
| 4 m/s, 3 minutes wait | $1,363 | $738 |
| 8 m/s, 3 minutes wait | $2,079 | $2,073 |
| 8 m/s, 30 minutes wait | $1,341 | $944 |
| 8 m/s, 90 minutes wait | −$1,814 | −$1,956 |

One and two m/s are diagnostic checks of the probe's low-speed range, not proposed production speeds. Four m/s is the lowest tested positive point for these pairs with the three-minute wait, not a fitted break-even threshold. Results are not monotonic when changing speed changes which stops get skipped. The 180-minute-wait cases include wholly unserviceable days and are marked infeasible; their incurred costs are still recorded. Above about 13.9 m/s, grid points are labelled time-compression controls. The normal/heavy 3/10-minute waits are mild illustrations, not measured traffic-light distributions.

## Gameplay implications and remaining evidence

Use actual arrival estimates and shift coverage when planning crew routes. Compact peak-following routes and a worthwhile single pitch should both remain viable choices, as the design allows. Avoid importing the old timer's apparent travel speed into physical route estimates. Refuel in planned slack when possible; do not assume an emergency morning detour is economically harmless. The current 1–2 m/s probe profile is not an economically acceptable production default for these cases.

Before production integration, calibrate complete journey times, road lengths, the modpack's game clock, signal/queue/parking delays, consumption and station visits on native routes. Then rerun the same cases and add later evening windows, scheduled bay restocks and adapted schedules. Regulars, reputation, health, own-fleet competition, catering, seasonal/rain variation, spoilage, station congestion, stochastic traffic, repairs, fines, tax, capital costs and financing are outside this study. Its full-supply, fixed-shift, daytime assumptions limit any wider profitability claim.

## Verification and reproduction

The calculator builds without warnings or errors. **29 regression checks pass**, including exact timer/control equivalence, map-frame consistency, delayed demand at actual arrival time, no service during travel, stock and fuel conservation, finite/reachable refuelling, fixed paid shifts, missed windows, explicit infeasible outcomes, actual stock exhaustion and visible reduced-operation flags. The reporting correction preserved every pre-existing result field across 512 weekly cases in the two pricing studies. The parent also checked the accounting identities on all main cases and confirmed all 112 input/source hashes matched. Final independent review is recorded in [the review notes](../reviews/2026-10-21-crew-economics.md).

```sh
dotnet build tools/balance/Balance.csproj
dotnet tools/balance/bin/Debug/net10.0/balance.dll \
  --reports crew-economics,crew-economics-checks --pricing default \
  --out build/crew-economics/current.md --json-out build/crew-economics/current.json
```

Use `--pricing ceiling` for the second price case; `--crew-economics-config` accepts a versioned assumption file. The run above uses current checkout inputs; compare its hashes with the archived results before expecting identical numbers. The recorded runs used nine frozen input JSON files under ignored `build/crew-economics/inputs/`, with exact commands and results in that directory's parent. The [compact numeric export](crew-route-economics-2026-10-21.json) contains both price cases, policy, configuration, routes, aggregate/daily-mean results, sensitivity grids and provenance. Full ignored JSON retains every weekday, trip, stock boundary and station fill. No game DLL, save or native binary is part of this evidence.
