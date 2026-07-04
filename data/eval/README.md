# Structuring-eval input sets

> ROLE CHANGE (2026-07-03, Phase 0 closure): the structuring model was decided
> by Mikko (Poro-2-8B, synthesis-priority rationale — see CLAUDE.md "Phase 0
> CLOSED") and the real-corpus comparison run was cancelled. The ~20
> hand-written texts below are NO LONGER a model-selection instrument; they
> become (a) the Phase 1 core-corpus seed and (b) a smoke-test set for the
> salvage layer and structuring prompt.

~20 messy Finnish feedback texts, **hand-written by Mikko** (domain expert),
written when Phase 1 needs them.

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
non-evidential. (The structuring-model decision was made by Mikko on
synthesis-priority grounds, 2026-07-03 — placeholder metrics played no part.)

Run against placeholders:

    dotnet run --project tools/FeedbackIntelligence.StructuringEval -- eval --Eval:InputPath=data/eval/placeholder-inputs.jsonl
