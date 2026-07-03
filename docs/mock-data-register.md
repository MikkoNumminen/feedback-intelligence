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
| `data/demo-placeholder.db` | 2026-07-03 | run-through database (gitignored): 32 placeholder corpus items pushed through POST /feedback + 3 mock desk entries (`desk-runthrough-*`, texts marked MOCK) | wiped; real corpus ingested live in the announced GPU window (TODO #12) |
| mock `acceptedStructure` values (push-corpus.ps1 `-MockStructuresFromGroundTruth`) | 2026-07-03 | story items got structures derived from ground truth because the LLM window was unavailable (RAG up); noise items were honestly stored `structure_failed` | real run omits the switch — Poro structures every item live at ingest |
| eval placeholder reports `docs/evals/structuring-eval-20260703-*` | 2026-07-03 | Phase 0 pipeline-proof eval runs, auto-labeled non-evidential in the report header | permanent receipts; never evidence for model choice |
| `data/demo-live-placeholder.db` (under the API project dir) | 2026-07-04 | LIVE-LLM run-through database: 32 placeholder items structured by real Poro + desk-live-001 (text marked MOCK LIVE) | wiped; real corpus ingested live after TODO #1 |
