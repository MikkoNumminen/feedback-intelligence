# ADR-0031 — Model-authored sentiment as an optional 6th structuring field (Poro ignores it today)

- **Status:** Accepted (2026-07-14)
- **Deciders:** Mikko
- **Follows:** [ADR-0030](0030-sentiment-indicator-deterministic-from-type.md)
  (deterministic type→sentiment; this was its named gated follow-up),
  [ADR-0022](0022-lock-poro-prompts-v0.md) (locked prompt — the gate run here),
  [ADR-0002](0002-llm-behind-one-abstraction.md) (model is swappable config)

## Context

ADR-0030 shipped a deterministic sentiment indicator (derived from `type`) and
named a model-authored sentiment field as the gated follow-up — a genuine
per-item polarity judgement that would catch an angrily-phrased "suggestion" or a
backhanded "praise" the type proxy misses. This ADR adds that field, behind the
ADR-0022 prompt-lock gate.

## Decision

`sentiment` becomes an **optional sixth structuring field**:

- `FeedbackStructure.Sentiment` is a nullable field, added last, so every existing
  five-argument construction and every stored `structure_json` written before it
  stay valid; serialization omits it when null (no migration).
- `StructuringSchema` keeps the five REQUIRED field names in `Fields`; `sentiment`
  lives in a new `OptionalFields`, with `KnownFields = Fields ∪ OptionalFields`.
  So its **absence is never a missing-field violation**, and its presence is not an
  extra field.
- The ingest parser **salvages**: absent → null; present-and-valid → taken
  (lowercased); present-but-invalid → null + a note. An optional field never fails
  an otherwise-valid item. The human-correction validator, by contrast, rejects an
  invalid sentiment (a person's edit must be legal).
- The locked structuring prompt gains a `sentiment` field asking for one of the
  domain's sentiment keys, rendered from a new `{{sentiments}}` placeholder.
- `ReportService.SentimentOf` becomes `structure.Sentiment ?? typeMap` — the model
  value wins when present, the ADR-0030 type map is the fallback.

## Gate (ADR-0022) — run 2026-07-14

1. **A4 red-team fixture (`RedTeamCoverageTests`)**: stayed green.
2. **Announced seed-42 live Poro check** (throwaway DB, isolated port, shared GPU
   announced; the running demo untouched): all 71 items ingested,
   `structure_failed = 0`, `droppedClaimCount = 0`, `actionDroppedCount = 0`, both
   alerts grounded to real ids. The prompt change is grounding-safe.
3. Pinned hash updated in the same commit, citing this check.

## Consequences

- **Poro-2-8B does NOT emit the field today.** The live check measured it
  directly: **0 of 71** items came back with a sentiment value — the model returns
  its trained five-field object and ignores the sixth. So the deterministic
  type→sentiment map (ADR-0030) remains the **active** source right now; the
  report's sentiment (negative 55 / neutral 4 / positive 12 on seed-42) came
  entirely from the fallback. This is exactly the "quality unproven until the live
  check" risk the gate exists to surface — and it surfaced.
- **The field is shipped as a forward-compatible seam, not a working model
  feature.** It is safe (grounding intact, nothing breaks) and inert with Poro. If
  the structuring model is ever swapped (ADR-0002 makes it config) to one that
  emits sentiment, the field activates automatically with no further code change —
  `SentimentOf` already prefers it. Until then it costs a few prompt tokens the
  model ignores.
- **The theme-format constraint in the same prompt edit DOES work** — the live
  check returned base-form, plain-space themes ("tuotteiden laatu", not
  "tuotteiden_laatu"), the ADR-0028 follow-up validated end to end.
- **Honest scope:** because the model contributes nothing here yet, this ADR does
  not claim improved sentiment accuracy. The indicator's real quality is still the
  deterministic proxy's (ADR-0030). Revisit if/when a model that emits sentiment is
  adopted, and re-measure against the type map before trusting it.
- The "exactly five fields" language in [schema.md](../schema.md) becomes "five
  required + one optional".
