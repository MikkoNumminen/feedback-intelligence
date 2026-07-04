# ADR-0004 — The salvage layer is a mandatory production component

- **Status:** Accepted (2026-07-03)
- **Deciders:** Mikko

## Context

Choosing Poro for structuring ([ADR-0003](0003-poro-for-both-roles.md)) accepted
an unmeasured risk: its JSON discipline on messy Finnish. Rather than measure it
up front, the risk is absorbed architecturally. During placeholder runs the
model produced real, repeatable malformations: JSON wrapped in markdown fences,
`department` returned as an array, and invented enum values.

## Decision

A **salvage layer** sits behind the LLM abstraction (in the Llm project) and is
a **mandatory production component, not an eval-time nicety**. On every
structuring output it:

1. strips markdown fences → parses,
2. validates every field against the schema enums,
3. normalizes where safe (e.g. `department` as an array → first element, with
   the discard logged),
4. re-prompts **once** on anything still invalid,
5. if the retry still violates, stores `structure_failed` with the raw text
   preserved — **no feedback is ever lost**, only its structuring.

## Consequences

- Feedback capture is decoupled from model correctness: a malformed or
  unavailable model degrades the *structure*, never the *record*.
- The exact failure shapes the placeholder run caught (fenced JSON,
  department-as-array, invented enum values) are pinned by unit tests, so a
  regression in the salvage path is caught in CI, not in a demo.
- Salvage provenance (`salvaged` / normalized / retried) is carried on the
  result and feeds the honest telemetry — a salvaged success is still a success,
  but the distance from strict discipline is observable.
