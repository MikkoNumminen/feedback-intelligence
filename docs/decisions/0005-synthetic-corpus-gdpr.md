# ADR-0005 — Synthetic, expert-calibrated corpus (a GDPR decision)

- **Status:** Accepted (2026-07-03)
- **Deciders:** Mikko

## Context

The demo needs believable feedback data with findable stories in it. The obvious
shortcut — scraping real Google reviews or other public feedback — would pull in
personal data. A demo has no lawful basis to process real customer personal
data.

## Decision

**100% synthetic data, no scraped reviews, no real personal data.** The corpus
is a **hand-written, expert-calibrated core** (Finnish dialects, typos, desk
shorthand), multiplied *offline* by the local LLM and composed by a seeded
generator into datasets with planted, machine-checkable stories. The corpus is
committed to the repo. This is a deliberate design decision the demo
*showcases*, not merely a constraint it obeys.

## Consequences

- "Synthetic but expert-calibrated, because real data would be a GDPR problem"
  is a talking point in its own right — the data strategy demonstrates judgment.
- The core corpus is authored by the domain expert (never invented by an agent
  for evidential use); the offline multiplication keeps the analyzer from
  grading its own just-generated text (arm isolation).
- Same seed → the same rehearsable scenario; a new seed → a fresh-looking but
  equally story-bearing set. Ground truth names the exact item IDs per story, so
  "the report found the story" is verified by ID matching — see
  [ADR-0009](0009-grounding-is-structural.md) and
  [ADR-0010](0010-verify-gate-tiering.md).
- Corpus format and the authoritative per-story breakdown live next to the data
  in [`../../data/corpus/README.md`](../../data/corpus/README.md).
