# PR #5 — docs/demo: end-to-end run-through, README, demo script

Branch `docs/run-through` → `main` (local; no remote exists).

## What

The autonomous end-to-end run-through on placeholder data, plus the README
design story, the 5-minute demo script draft, the corpus push script
(`tools/push-corpus.ps1`), and the register/TODO bookkeeping.

## Run-through protocol and results (2026-07-03)

Constraint honored: the mikkonumminendev RAG stack was UP the whole time, so
per the hard rule ZERO LLM/GPU calls were made — the LLM-inclusive loop waits
for the announced window (TODO #7). Everything else ran live over HTTP against
the real API on a dedicated, gitignored demo DB:

1. `generate --seed 42` over the dev-placeholder pool → 32 items, 3 planted
   stories, auto-labeled non-evidential.
2. All 32 pushed through `POST /feedback` (the public endpoint, never the DB):
   32 created, 0 failed. Story items via the accepted-structure path (mock
   structures derived from ground truth — registered); 15 noise items stored
   honestly as `structure_failed` (LLM down ≠ feedback lost).
3. Desk simulation: one entry with a severity correction (audit round-trips),
   one manual entry with `modelInterpretationFailed` (telemetry marker works).
4. `GET /report`: 34 items, 4 theme groups, deterministic alert fired
   (dev-variant "murtui" — a keyword the REAL safety corpus must avoid,
   which is why the corpus is verified against config/alert-keywords.json),
   0 dropped claims, 5 honest LLM-fallback counts.
5. Machine-checkable ground-truth verification (the Phase 4 acceptance
   mechanism, previewed): dairy 9/9 story ids grounded in the maito_kylma
   theme (min 4) — PASS; trend worsening → "paheneva" — PASS.
6. Live moment mechanics: desk save → next report fetch shows count 10 → 11
   (ingest-driven cache invalidation) — PASS.
7. Snapshot: `/report/snapshot` (JSON) and `/report/snapshot.html` both 200.

## Verification

65/65 unit tests green; the run-through itself is the integration evidence.
Everything mock is in `docs/mock-data-register.md` with named replacements.
