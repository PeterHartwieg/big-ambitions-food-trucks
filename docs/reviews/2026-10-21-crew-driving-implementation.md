# Crew driving Gate A implementation review — 21 October 2026

Review target: the developer-only unattended native truck probe described in [the scope](../crew-driving-spike.md). This is preparation for controlled testing, not acceptance of native driving or production crew travel. Existing timer travel, saves and fuel purchases are unchanged.

The repository’s requested panel reviewed the implementation: Opus 5.5, GPT 6 Astra and GPT 6.1 Sol. Four review rounds were used, with bounded implementation work also delegated to GPT 6.1 Sol. All three ultimately returned **APPROVE FOR CONTROLLED TESTING**, including turns and traffic. The earlier architecture discussion’s unresolved choice of shared kernel versus read-only Persistent City observations remains deferred.

## Corrections made

- Fixed native fuel hook signature and required both time-skip entry points.
- Separated signal stopping from endpoint completion, and rejected completion with lateral/angular body movement.
- Prevented stale owners and late damage callbacks from overwriting replacement controllers or save sessions.
- Added bounded prepared road geometry with exact native marker mappings, preserving original signal planes.
- Corrected obstacle distance references and behind-body filtering; replaced excessive dense-turn squares with oriented envelopes.
- Removed collider-layer overrides entirely; incompatible native visibility refuses admission.
- Tolerated bounded stationary suspension movement while retaining physical boundary and reverse-velocity checks.
- Preserved occupied junction records after interruption and released them after forward exit, complete reversal behind entry, or actual removal.
- Added blocker identity/query/gap and signal-transition diagnostics.

## Automated evidence

- Full Core suite: **2,078 passed, one existing skipped**. The focused crew-driving group has 68 cases.
- Native-boundary harnesses: **14 tests passed**, with multiple cases including 50 synthetic lifecycles, 500 noisy obstacle observations, a prepared 90-degree curve, callback ordering, replacement isolation and reverse junction clearance. These use stubs, not Unity physics.
- Whole native source compilation: **0 errors**, nine existing warnings outside the probe. The official SDK package was subsequently built in an isolated SDK checkout and its expected probe types/fields/control constants checked in the DLL. Metadata checks resolved all 14 current hook targets.
- The wider Python suite encountered 12 pre-existing mock-bridge import errors caused by metadata in the economy table; this turn does not claim that suite passed.

Local full briefs, reviewer outputs, build logs and source hashes are under ignored `build/crew-drive-implementation/`. Game DLLs, decompiled sources, save files and native binaries remain uncommitted.

## Remaining native gate

The panel’s approval permits controlled experimentation. It does not establish braking assumptions, native road-chain feasibility, mutual traffic visibility, measured performance, persistent fuel/damage behavior, or the 20-drive/50-lifecycle campaign. The conservative envelope can stop for adjacent-lane objects. Late red/amber stop flags remain denied and impossible stops count as failed runs; the probe never infers amber permission. See [implementation status](../crew-driving-implementation.md) for the current native evidence.

## Build isolation

A packaging attempt collided with another task using `build/sdk`. This probe’s subsequent builds use the separate `build/crew-drive-implementation/sdk`. The shared SDK’s mod symlink was changed during that collision; the other task’s package should be rebuilt from its own checkout before deployment. No artifact from that concurrent build is used as evidence here.

## Follow-up native evidence

The first native test passed hook startup and readiness but refused collider visibility before enabling driving. A captured native straight passed Core preparation; two nearby turns failed the sharp-corner contract. See the implementation checkpoint for exact evidence and the next chunk. This is an unresolved native gate, not a successful driving feature. After panel approval, only status diagnostics, the waypoint reply’s object wrapper, and the visibility error detail changed; the reviewed driving and visibility policy stayed intact.

A subsequent rig update replaced the temporary package with 0.10.2 while its reservation was held. The installed-hash guard refused the next update. The newer package was preserved, its manifest verified, and this task’s process/reservation were stopped/released.

The updated-rig rerun and parallel review rounds 5–7 are recorded in [the follow-up review](2026-10-21-crew-driving-rerun.md). They confirmed the exact wheel collider behind the refusal and corrected mask precedence and pre-acquisition cleanup. Powered driving remains unverified.
