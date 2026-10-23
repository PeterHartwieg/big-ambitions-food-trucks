# Crew route economics review

21 October 2026. Scope: an offline profitability sensitivity study in `tools/balance`, using the existing demand calculator and explicit hypothetical travel/fuel inputs. No production driver, save schema, game data, prices or wages changed. GPT 6.1 Sol implemented the bounded calculator work; independent reviewers were Opus 5.5, GPT 6 Astra and a separate GPT 6.1 Sol session.

## Methodology

Astra and Opus reviewed the study specification before final implementation. Their corrections require one shared dispatch policy and paid shifts across timing cases, fixed stock targets, a continuous matched single-pitch window, explicit fuel consumption versus purchase accounting, and a mathematical control reproducing the timer. The main study uses current 30-minute restocking, freezes route choices before physical scenarios, reports incurred losses for infeasible operations, and includes speed/fuel/wait sensitivity.

The parent and implementer identified a coordinate-frame mismatch in the first draft. The city's approximate regional map anchors cannot be combined with native bay coordinates. The final model uses the commissary's building anchor in the same map frame as pitches and tests that invariant. No economic conclusion uses the erroneous mixed-frame distances. Source and input hashes identify the corrected runs.

## Implementation round 1

Astra and Sol returned **APPROVE FOR CONDITIONAL STUDY**. Their independent read-only audits covered 256 weekly results, with stock/fuel conservation, expense accounting, weekday continuity, service/shift boundaries and all 112 source/input hashes consistent. The timer-equivalent control exactly reproduces baseline economics. The regression fixtures include delayed arrival, unreachable station, impossible fuel capacity and missed-window cases.

Opus requested two reporting corrections while accepting the underlying arithmetic:

1. Expose stock exhaustion. Staying already wins 10 of 12 legacy pairs; every legacy case sells out its daily hot dogs on five weekdays. The full moving-versus-staying difference cannot be attributed to traffic under a no-midday-restock policy.
2. Flag reduced operations in every comparison and sensitivity table. A route with skipped stops must not appear to be an ordinary completed schedule merely because it returns home; distinguish reduced operation from wholly infeasible cases and preserve incurred expenses for both.

## Correction and implementation round 2

The calculator now records each day's actually exhausted items and weekly item-day counts, excluding empty unserved stock. The comparison names the legacy stay advantage and stock constraint without claiming a quantified cause. Every comparison and sensitivity row identifies full, reduced-with-skips or infeasible operation; the discrete speed summary lists reduced and infeasible cases separately.

The build has zero warnings/errors and **29 regression checks pass**. Both default and ceiling-price studies were rerun with frozen inputs. All pre-existing result fields, including monetary figures and route definitions, stayed exactly unchanged across **512 weekly cases**. Source/input hashes and the compact numeric export were verified. Markdown table columns and report links also pass checks.

**Opus 5.5, GPT 6 Astra and GPT 6.1 Sol all returned APPROVE FOR CONDITIONAL STUDY.** All three checked the reporting fixes; the final panel also reviewed the parent's narrative and its limits. Two wording refinements clarify that only moving cases skip a stop in the low-fuel heavy comparison and round the ordinary fuel-cost range to about $10–$27. This is approval of the offline calculator and conditional study only; native calibration and production acceptance remain open.

## Evidence limits

Approval of this study cannot establish actual Persistent City economics. Map geometry, clock, traffic, station and fuel inputs need native calibration. Fixed daytime windows, finite food stocks, a heuristic choice of routes, two workers on fixed shifts and ample supply/funding limit the comparison. “Long” selected routes total only 703–811 approximate straight metres. The matched single-pitch choice is the best among each route's three pitches, not a globally optimal business strategy.

Full briefs, reviewer outputs, model metadata, builds, regression logs and detailed per-day runs are local under ignored `build/crew-economics/`. The [study](../balance/crew-route-economics-2026-10-21.md) and its numeric export retain the conclusions, assumptions and provenance in the repository.
