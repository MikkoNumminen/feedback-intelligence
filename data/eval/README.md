# Phase 0 structuring-eval input set

~20 messy Finnish feedback texts, **hand-written by Mikko** (domain expert).
These are the fixture for the Phase 0 structuring-model eval (Poro-2-8B vs
qwen3:8b): valid-JSON rate, schema adherence, classification sensibility.

Texts must NOT be generated or scraped — hand-written synthetic data is a
documented design decision (see CLAUDE.md). Keep typos, dialect, rambling,
misplaced anger. Cover all four sources.

## Format — `structuring-inputs.jsonl`, one JSON object per line

| field       | value                                                    |
|-------------|----------------------------------------------------------|
| `id`        | unique string, e.g. `ex-001`                             |
| `source`    | `google_review` \| `email` \| `web_form` \| `desk`       |
| `text`      | raw Finnish feedback, exactly as a human would write it  |
| `timestamp` | ISO-8601 with offset                                     |

The one line already present is the example from the project spec — keep it or
replace it. Same shape as the Phase 1 corpus format, so this file doubles as an
early fixture.

## `placeholder-inputs.jsonl` — SYNTHETIC, NON-EVIDENTIAL

LLM-generated placeholder texts (ids `ph-*`) whose ONLY job is to exercise the
pipeline end to end before the real corpus exists. HARD RULE (see CLAUDE.md):
results from this file prove the pipeline, never the model choice — clean LLM
Finnish does not predict JSON discipline on messy dialect and desk shorthand.
The eval report auto-labels any run whose input path contains "placeholder" as
non-evidential. The structuring-model decision comes exclusively from a run on
the hand-written `structuring-inputs.jsonl`.

Run against placeholders:

    dotnet run --project tools/RetailFeedback.StructuringEval -- eval --Eval:InputPath=data/eval/placeholder-inputs.jsonl
