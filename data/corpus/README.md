# Corpus pipeline (Phase 1)

Flow: `core.jsonl` (hand-written) → `variants` verb (offline LLM multiplication,
**announced GPU window**, output committed) → `variants.jsonl` (committed) →
`generate --seed N` (deterministic, **never calls the LLM**) →
`generated-<seed>.jsonl` + `ground-truth-<seed>.json`.

## `core.jsonl` — hand-written by Mikko, EVIDENTIAL

Realistic Finnish retail feedback: dialects, typos, rambling, terse desk
shorthand. Never LLM-generated, never scraped (the synthetic-but-expert-
calibrated GDPR decision, see CLAUDE.md). One JSON object per line:

| field    | value                                                            |
|----------|------------------------------------------------------------------|
| `id`     | unique string, e.g. `core-001`                                   |
| `source` | `google_review` \| `email` \| `web_form` \| `desk`               |
| `text`   | the feedback, exactly as a human would write it                  |
| `story`  | OPTIONAL — tags the item as raw material for a planted story. Must match a `Generator:Stories` id: `dairy-freshness-worsening`, `safety-no-keyword`, `availability-slow-burn`. Untagged items become base noise. |

The one line present is Mikko's own example from the project spec. Write ~5+
items per story tag and a broad untagged rest; the safety story needs texts
that contain NO alert keywords — detectable only by understanding.

## `dev-placeholder-*` files — SYNTHETIC, NON-EVIDENTIAL

LLM-session-generated stand-ins that exercise the generator machinery before
the real core corpus exists. Anything composed from them is auto-labeled
non-evidential (filename-detected, same discipline as Phase 0) and must never
appear in any demo or report.

## `ground-truth-<seed>.json` — machine-checkable, Phase 4's fixture

Per planted story: the exact `feedbackIds`, `expectedDepartment` (schema enum),
`expectedThemeKeywords` (keyword set, not prose), `windowFrom`/`windowTo`,
`trend`, `minGroundedIds`, `expectAlert`. Phase 4 verifies a report claim by
matching against these IDs — "the report's dairy claim grounds to >= N of these
specific IDs within this window", never "the report mentions dairy". The story
tag is never written into the generated corpus — the analyzer meets it cold.
