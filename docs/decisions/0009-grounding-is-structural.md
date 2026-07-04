# ADR-0009 — Grounding is structural, not prompt-wording

- **Status:** Accepted (2026-07-03)
- **Deciders:** Mikko

## Context

Grounding is non-negotiable: every claim in the management view must be
traceable to specific feedback items, clickable open in the UI. A system must
never present a theme or trend it cannot source. Asking the model nicely to cite
its sources is not a guarantee.

## Decision

Make grounding **structural**, enforced by validation rather than prompt-wording:

- The **deterministic layer** computes the load-bearing facts — theme grouping,
  counts, trend direction (kasvava / laskeva / vakaa / paheneva, by window-half
  volume and severity shift), and the feedback IDs behind each group.
- The **LLM** only writes the Finnish title/narrative per group, and **must cite
  provided feedback IDs**. A narrative whose citations are empty or reference an
  ID not in the provided set is **dropped to a deterministic fallback, logged and
  counted** (`droppedClaimCount`) — never shown.
- Alert **nominations** from the LLM are accepted only for IDs from the provided
  batch; it may **add** alerts, never remove a deterministic one.

## Consequences

- The view can never display an ungroundable claim: the failure mode is a
  deterministic fallback narrative, not a fabricated one.
- The report **generates even with the LLM entirely down** — layer 1 carries it;
  LLM unavailability is counted separately (`llmFallbackCount`) and never
  reported as model misbehaviour.
- "Grounded" is machine-verifiable end to end: the generator's ground truth
  names the exact IDs per story, and the acceptance gate
  ([ADR-0010](0010-verify-gate-tiering.md)) checks them.
