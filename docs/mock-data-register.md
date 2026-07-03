# Mock-data register

Every mock/placeholder artifact created during the autonomous run-through.
Hard rule: all of these are NON-EVIDENTIAL — pipeline/machinery exercise only,
never demo content, never a basis for decisions. Each entry names its
replacement.

| artifact | created | what it is | replaced by |
|---|---|---|---|
| `data/eval/placeholder-inputs.jsonl` | 2026-07-03 | 9 session-generated Finnish texts for the Phase 0 pipeline eval | permanent pipeline fixture; never evidential |
| `data/corpus/dev-placeholder-core.jsonl` | 2026-07-03 | 15 session-generated texts standing in for the hand-written core corpus | `data/corpus/core.jsonl` (TODO #1) |
| `data/corpus/dev-placeholder-variants.jsonl` | 2026-07-03 | 39 session-authored rewordings substituting for a real `variants` LLM run | `data/corpus/variants.jsonl` from the announced run after TODO #1 |
| `data/corpus/generated-placeholder-*.jsonl`, `ground-truth-placeholder-*` | 2026-07-03 | deterministic compositions from the dev pool (gitignored, regenerable) | `generated-42.jsonl` + `ground-truth-42.json` from the real pool |
