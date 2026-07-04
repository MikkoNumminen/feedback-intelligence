# PR #2 — feat(api): Phase 2 ingest pipeline

Branch `feat/api-ingest` → `main` (local; no remote exists).

## What

`src/FeedbackIntelligence.Api` (ASP.NET Core minimal API): `POST /feedback` (one
endpoint, channels as source values), `POST /interpret` (desk preview),
`GET /feedback/{id}` + windowed archive query, `GET /health` (1-token real
completion). SQLite single-table store (structure as JSON column, corrections
audit field). Deterministic alert layer FIRST (config/alert-keywords.json,
substring contract) and independent of the LLM. Containment as config:
800-char input, 16 KB body, per-IP 30/60 s rate limit, LLM concurrency 2 with
500 ms acquire-then-shed. LLM down ≠ feedback lost: stores `structure_failed`
with raw text preserved. Live smoke test performed with the LLM deliberately
unreachable (archive, validation-reject, resilient store, health-503 paths).

## Review

Two subagent angles (correctness; conventions+spec) + inline reuse/
simplification pass by the primary agent (subagent capacity was limited this
session; noted for transparency).

## Findings → resolutions (all fixed)

1. Lexical timestamp comparison broke across mixed UTC offsets → all stored
   and queried instants normalize to one fixed-width UTC round-trip format
   (`TimestampNormalizer`), tests pin the wrong-lexical/right-instant case.
2. Duplicate client id → unhandled PK violation (500, GPU burnt on retries) →
   idempotency pre-check before any LLM work + race-safe 409 mapping.
3. Non-ASCII/control chars in id blew up the Location header after the row was
   stored → id charset whitelist `[A-Za-z0-9._-]`.
4. Committed smoke-test SQLite DB + root-anchored ignore rule → `git rm
   --cached`, pattern widened to `**/data/*.db`.
5. Per-IP rate limit collapsed to one bucket behind the tunnel (all loopback)
   → ForwardedHeaders middleware before the limiter.
6. Permissive timestamp parse stored unparseable-format originals → fixed by
   normalization (finding 1).
7. CLAUDE.md phase state not updated in the same change (hard-rule violation)
   → "Phase 2 status" section added.
8. Hardcoded health timeout, query limit caps, id length cap → IngestOptions
   config, validated at startup.
9. Correction audit `Field` keys were free-form, fragmenting the per-field
   telemetry that replaces the cancelled model eval → validated against
   schema v0 field names.
10. Inline reuse finding: the CWD-then-BaseDirectory path resolution existed
    in four copies → extracted `AppPathResolver`, all four call sites converge.

## Verification

Build clean; 55/55 tests green (9 new: normalization instant-ordering,
duplicate-id-no-LLM-burn, UTC storage, id charset, correction field names).
