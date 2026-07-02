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
