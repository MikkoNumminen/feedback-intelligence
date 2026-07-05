# Architecture

The engine is **domain-agnostic**: messy free-text feedback in → a deterministic
alert layer plus LLM structuring and synthesis → grounded situational signal
out. Retail is the first application ([ADR-0007](decisions/0007-domain-agnostic-core.md)),
carried entirely in configuration and a domain module. This document describes
the engine; the retail taxonomy lives in [domain/retail.md](domain/retail.md)
and the feedback schema in [schema.md](schema.md).

## The two-layer principle

Everything that CAN be rule-coded IS rule-coded; the LLM is used only where
free-form language genuinely cannot be ([ADR-0006](decisions/0006-ai-in-exactly-two-places.md)).
Concretely, two layers, in this order:

1. **Deterministic layer — first, always.** Alert keyword/pattern matching,
   theme grouping, counts, trend direction, time windows, archival. Cheap, never
   sleeps, never hallucinates. It runs before and independent of the LLM.
2. **LLM layer — behind it.** Only two jobs: structuring messy input into the
   schema, and writing grounded Finnish synthesis over the structured items. The
   LLM may **add** (e.g. nominate an alert the keywords missed); it can never
   **remove** a deterministic result. The LLM (Poro-2-8B) is used to its
   strengths, measured on the real corpus: structuring gets each category as
   `"key" (Finnish label)` (not a bare key), and the LLM alert layer screens
   each keyword-less complaint **individually** as a strict yes/no (Poro floods
   *and* misses when selecting from a list, but discriminates flawlessly on one
   item). See [ADR-0015](decisions/0015-poro-real-corpus-tuning.md) for the knobs.

## Ingest pipeline — one endpoint, source-valued channels

There is **one** ingest endpoint, `POST /feedback` with `{ source, text,
timestamp }`. "Channels" are `source` *values* (`google_review`, `email`,
`web_form`, `desk`) — not four integrations. On ingest:

1. The deterministic alert layer runs first; its hits are stored regardless of
   what the LLM does.
2. The structuring model produces the JSON structure, through the salvage layer
   ([ADR-0004](decisions/0004-salvage-layer-mandatory.md)) — **except** on the
   desk path, where the request carries an already-accepted structure (the human
   accepted or corrected the `/interpret` preview): that structure is stored
   as-is, **without a second LLM pass**, so no redundant GPU call is made and the
   stored structure cannot diverge from what the human approved. Corrected values
   are schema-validated too.
3. The row is stored: raw text + structure (JSON column) + source + timestamp +
   a correction-audit field. Storage is SQLite
   ([ADR-0008](decisions/0008-sqlite-over-postgres.md)); there is deliberately
   **no** normalized category hierarchy — structure is the AI's *output*, not an
   input form's requirement.

If the LLM is unavailable, the item is stored `structure_failed` with the raw
text preserved: **LLM down ≠ feedback lost**. A busy GPU sheds with 503 rather
than queueing behind a slow generation.

Other endpoints: `POST /interpret` (desk preview, stores nothing),
`GET /feedback/{id}`, `GET /feedback?from&to&limit`, `GET /schema` (enum sets
for UIs, single source is the schema), `GET /report`, `GET /report/snapshot(.html)`,
`GET /telemetry/corrections`, `GET /health` (a 1-token *real* completion, not a
liveness ping).

## The mandatory LLM abstraction

No code calls Ollama directly ([ADR-0002](decisions/0002-llm-behind-one-abstraction.md)).
Everything goes through Microsoft.Extensions.AI's `IChatClient` behind
`ILlmClientFactory`, with structuring and synthesis as independently
configurable models (keyed DI: `Llm:Models:Structuring` / `Llm:Models:Synthesis`).
Provider-specific behaviour — e.g. the API-level `think: false` seeded via
`ChatOptions.RawRepresentationFactory` — lives inside the Llm project; callers
never see an OllamaSharp type. Both roles run Poro-2-8B today
([ADR-0003](decisions/0003-poro-for-both-roles.md)) but stay independently
swappable.

## The salvage layer

A mandatory production component behind the abstraction: strip fences → parse →
validate every field against the schema enums → normalize where safe → re-prompt
once → else store `structure_failed` with raw text preserved. Full rationale and
the tested failure shapes: [ADR-0004](decisions/0004-salvage-layer-mandatory.md).

## Grounded analysis + the management view

Two-layer analysis over a selectable window. **Alerts** on top (deterministic
hits + LLM nominations, each one click from its source item). **Themes &
trends** below. Grounding is **structural**, not prompt-wording
([ADR-0009](decisions/0009-grounding-is-structural.md)): grouping, counts, trend
direction and the feedback IDs are computed deterministically; the LLM only
writes the cited Finnish narrative, and an ungroundable claim is dropped to a
deterministic fallback, logged and counted. The report generates even with the
LLM fully down.

To keep one report refresh from starving the desk `/interpret` path on the
shared GPU, a report carries a per-generation **LLM call budget** and is served
from a one-entry cache that ingest invalidates — so a new desk entry appears on
the very next refresh (the live-demo centerpiece).

**Snapshot mode:** every generated report persists (JSON + a self-contained HTML
render); the frontend falls back to the latest snapshot when the backend is
unreachable, so a shared link never shows a dead page.

## Containment

Input and load limits are config, validated at startup: input length cap,
request-body byte cap, per-IP rate limit, LLM concurrency with acquire-then-shed
(never queue behind a busy GPU), and an output-token cap. Values and their
provenance (measured in a sibling project) are in [operations.md](operations.md).

## The domain-agnostic boundary

The engine owns the pipeline, the two-layer design, the abstraction, the salvage
layer, the grounded analysis, and the schema *shape*. A domain owns its taxonomy
values (for retail: the `category` enum, the alert-keyword list, the stories, and
the domain-voiced prompts) — all in a data-only module under `domains/<name>/`,
selected by `Domain:Active`. The boundary is clean in code (no domain values in
the engine) as of [ADR-0012](decisions/0012-pluggable-domain-modules.md); see
[domains.md](domains.md) and [domain/retail.md](domain/retail.md).
