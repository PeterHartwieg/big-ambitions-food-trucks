# Crew trigger-height certificate

Scope: the developer-only straight driving probe. Production crew journeys and fuel-refill jobs remain unchanged. This continues the [body-contract review](2026-10-21-crew-routing-body-contract.md), after one native endpoint-braking completion.

## Architecture review

Opus 5.5, GPT 6 Astra and GPT 6.1 Sol reviewed the next chunk independently. All recommended replacing the fixed low body band with a bounded search using the initialized traffic inventory, and making failures identify the actual trigger and reason. The recorded model profiles share a nominal local interval of 0.36–1.00 m. This is input evidence, not proof of live collision delivery or attribution of the earlier refusal.

The accepted scope keeps the existing upright-only Gley restriction and the 100 m live-check radius. Broader Gley pitch/roll support and a smaller observation radius need separate evidence and review. Physical body capture remains independent of traffic initialization; only a successful traffic certificate can grant the exact wheel-mesh visibility exception.

Traffic admission considers inactive pooled models and disabled triggers as well as active ones. It searches at most 17 candidate positions for one common 0.10 m band, accounting for the truck's permitted 0.05 rad tilt and an explicit 0.05 m limit on the difference between the traffic and truck root heights. This height limit is a restricted operating domain, not an assumption that the native game always satisfies it.

Each wheel's full yaw and suspension envelope must map to a padded prism inside one actual cooked convex authority. Different wheels can use different authorities; combining bodies to bridge a gap is unsupported. Cooked containment is established at admission. Observations recheck the selected geometry and live coverage without repeating candidate searches or cooked-shape queries. Full physical obstacle sweeps remain unchanged.

Reviewers also identified a stale-inventory risk: checking that `frontTrigger` remains nonnull does not detect its replacement or added collider descendants. This chunk therefore captures and validates exact front-root and collider membership, alongside the existing mask, override and pair-ignore guards.

Detailed diagnostics distinguish body faults, unsupported trigger shape/tilt, root-height differences, coverage failures and inventory changes. Read-only inspection must use the same profile and selection code as admission. Diagnostic results do not establish that native traffic actually stops.

## Validation status

The first implementation passed 46 adapter tests and compiled against native references with zero errors and nine existing warnings. The integrated harness compiles the actual body, shape, trigger and traffic adapters together; 10,000 successful observations allocated zero managed bytes and performed no cooked queries or band reselection. Stub results do not establish native behavior or frame-rate performance.

The first implementation review requested corrections to trigger rigidbody ownership, authoritative-body inventory membership, derived-scale rounding during native yaw, and diagnostic failure metadata. All were corrected, with regressions for nested/replaced bodies, inactive activation, foreign authoritative colliders, matrix-derived yaw rounding and diagnostic purity. Authored scales remain exact invariants; the small allowance for derived rounding is included in the profile margin. Diagnostic formatting retains the original reason if native reads fail, and inspection explicitly reports that masks are unchecked.

In the second implementation round, **all three reviewers approved controlled native rig validation with no remaining blockers**. Forty-six tests passed after the correction; a final affected-module rerun passed twelve, and native compilation again had zero errors and nine existing warnings. The official SDK package built successfully from the isolated updated-rig baseline, with all 32 recorded probe entries matching source. Native validation was pending at the end of that review round.

The initial rig install attempt was refused by the existing `persistent-city-classification-20261021` reservation before any backup, DLL replacement or process change. After that reservation was released, the reviewed package was installed under this task's own reservation and [tested natively](../evidence/2026-10-21-crew-trigger-native.md). The formerly refused checkpoint passed common-height selection and endpoint braking with Persistent City's physical traffic active. A longer drive stopped without contact for a persistent street-prop obstruction and was explicitly cancelled; it did not complete. The rig's original files were restored, all 263 manifest hashes verified, and the reservation released. No production code or safety check changed during the native session.
