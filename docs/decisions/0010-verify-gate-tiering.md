# ADR-0010 — Acceptance gate: hard gates vs. a trend warning tier

- **Status:** Accepted (2026-07-04)
- **Deciders:** Mikko

## Context

The Phase 4 acceptance test is mechanized as `verify --ground-truth <f>
--report <f>`: it checks a generated report against the ground-truth file by ID
matching ("the report's claim grounds to ≥ minGroundedIds of these specific IDs
within this window"), never by prose mention. A review found that comparing a
*story-level* trend expectation against the report's *department-aggregate*
direction would fail a correctly-planted story: same-department noise that the
LLM classifies into a story's department legitimately dilutes the aggregate
direction.

## Decision

Tier the checks:

- **Hard gates (story-owned, deterministic; exit 0 / 1):** ID grounding,
  expected-alert presence, and report-window coverage. Operator/data errors
  (e.g. empty ground truth, wrong file) exit **2** — never confusable with a
  gate failure.
- **Warning tier (does not fail acceptance):** the trend direction. A diluted or
  contradicted trend is reported **loudly** — it weakens the demo story and is
  worth knowing — but does not fail the gate.

## Consequences

- A correctly-planted, correctly-processed story passes acceptance even when
  same-department noise dilutes its aggregate trend.
- The dilution is surfaced, not hidden, and produced concrete corpus-composition
  guidance: keep untagged noise out of the three story departments where
  possible (recorded in [`../../data/corpus/README.md`](../../data/corpus/README.md)).
- CI can distinguish "gate failed" (exit 1) from "gate never ran" (exit 2),
  so a mis-pointed ground-truth file cannot masquerade as a passing run.
