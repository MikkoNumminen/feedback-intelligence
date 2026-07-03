# PR #9 — feat(api): correction telemetry (Phase 0 closure, item 2)

Branch `feat/correction-telemetry` → `main` (local; no remote exists).

## What

`GET /telemetry/corrections?from&to` — the drift detector that replaced the
cancelled up-front model eval: per-field desk correction rates (denominator =
model-interpreted desk entries; manual-after-failure counted separately),
Monday-start weekly series with per-field breakdowns, truncation flag.
Live-smoked against the run-through DB (13 desk entries, severity rate 0.083).

## Findings → resolutions (all fixed)

1. Numerator counted corrections from ALL desk entries while the denominator
   excluded model-failed ones — rates over different populations (could
   exceed 100%) → one population everywhere (interpreted entries), plus the
   root cause closed at the API edge: corrections combined with
   `modelInterpretationFailed` are now rejected by the validator
   (defense-in-depth test keeps the service safe against directly-written
   data).
2. `QueryMaxLimit` fetch cap could silently drop the OLDEST weeks → response
   carries `truncated: true` when the cap bites (no-silent-caps rule).
3. No window validation (an inverted window returned an all-green zero
   telemetry) → /report-parity validation: positive window, max-days cap, 400
   otherwise.
4. "Over time" was only totals per week — per-field drift unreadable →
   weekly buckets now carry per-field correction counts.
5. Weekly denominator differed from headline rates (model-failed entries
   included) → weekly rows expose `interpreted` so every rate shares one
   definition.
6. Unbucketable timestamps vanished silently → `unbucketedEntries` count in
   the response (0 for all pipeline-written data).

## Verification

Build clean; 81/81 tests green (6 new/updated); endpoint live-smoked on the
run-through database.
