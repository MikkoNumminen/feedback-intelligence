# ADR-0003 — Poro-2-8B for both structuring and synthesis

- **Status:** Accepted (2026-07-03)
- **Deciders:** Mikko

## Context

Two LLM roles exist: **structuring** (messy free text → strict JSON) and
**synthesis** (Finnish narrative over grouped items). Synthesis quality for the
user-facing Finnish is the priority. Poro-2-8B won a published 30-round blind
test for Finnish naturalness (26/30 firsts, against qwen3:8b and llama3.1:8b).

The structuring model was originally left open, to be chosen by a Phase 0 eval
(Poro vs qwen3:8b) because structuring is instruction-following / JSON-discipline
work that the blind test did not measure. Placeholder-only pipeline runs proved
the harness but were declared non-evidential for model choice (clean LLM Finnish
does not predict discipline on messy dialect).

## Decision

**Poro-2-8B for both roles.** No further model eval; the planned real-corpus
comparison run is cancelled. Rationale: synthesis quality is the priority, Poro
already won it, and a single model for both roles keeps the pipeline simple. The
keyed-DI role split (ADR-0002) stays, so the two roles remain independently
swappable.

## Consequences

- **Known, deliberately-accepted tradeoff:** Poro's JSON discipline on *messy*
  Finnish is **unmeasured**. The mitigation is architectural, not up-front
  measurement:
  1. The **salvage layer** is a mandatory production component — see
     [ADR-0004](0004-salvage-layer-mandatory.md).
  2. **Correction telemetry** from the desk UI (model-assigned vs.
     human-corrected per field) is the ongoing quality measure that detects
     drift or underperformance on real input. The model stays swappable by
     config if that data ever says so.
- Poro's native-chat path was measured-correct, not assumed: a 2048-token
  verification run reproduced the 512-budget numbers exactly (same adherence,
  same p50; only the cold-load latency tail differs). No truncation at 512.
- The 20 hand-written texts, once meant as the structuring-eval instrument,
  changed role to (a) the Phase 1 core-corpus seed and (b) a salvage-layer /
  prompt smoke-test set.
