# PR #4 — feat(analysis): Phase 4 grounded management view + snapshot

Branch `feat/analysis-report` → `main` (local; no remote exists).

## What

`GET /report` (validated window) + `GET /report/snapshot(.html)`;
`wwwroot/index.html` management view (Finnish): alerts on top, theme cards,
clickable ID chips opening source items, live/snapshot badge. Grounding is
structural: grouping/counts/direction computed deterministically; the LLM only
writes cited Finnish narratives (invalid citations → deterministic fallback,
logged + counted) and alert nominations (only provided IDs accepted). Report
generates with the LLM fully down; snapshots persist per generation.

## Review

One combined subagent angle + inline pass.

## Findings → resolutions (all fixed)

1. Empty first window-half made any new theme "paheneva" (even pure praise) →
   "paheneva" now requires a non-empty baseline half; new themes are
   "kasvava". Test pins it.
2. Snapshot unreachable when the backend is truly down (fallback fetched the
   same dead origin; rendered HTML had no route) → `/report/snapshot.html`
   route added; frontend fallback chain extended with a static-host copy
   (`./report-latest.json`), published at deploy time (Phase 5, TODO #3).
3. `droppedClaimCount` conflated citation failures with LLM unavailability —
   a false Finnish claim of model misbehavior in the degraded mode → separate
   `llmFallbackCount`; dropped-claims counts ONLY failed citations.
4. One report could issue ~48 sequential LLM calls, starving the desk
   `/interpret` path on the shared 2-slot gate → per-report LLM call budget
   (config, default 8; overflow logged, never silent), single-flight
   generation, and a report cache invalidated on every ingest so the live
   desk moment still appears on the next refresh. Tests pin budget=0 and
   cache/invalidation behavior.
5. Snapshot writes were non-atomic (readers could see truncated JSON) →
   temp-file + rename.
6. "Tänään" computed a trailing 24 h, not the calendar day; no custom window →
   calendar-day today + custom date-range option (spec: day/week/custom).

## Verification

Build clean; 65/65 tests green (7 new for direction edge case, counter
separation, zero-budget, cache reuse/invalidation).
