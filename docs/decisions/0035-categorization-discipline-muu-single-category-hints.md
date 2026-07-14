# ADR-0035 — Categorization discipline: the catch-all is one category, tighter hints, the correction loop as backstop

- **Status:** Accepted (2026-07-14)
- **Deciders:** Mikko
- **Amends:** [ADR-0026](0026-categories-emergent-topics-live-summary.md) (retires the
  catch-all's emergent-topic split) and, in part, the emergent-topic-key
  normalization of [ADR-0028](0028-categorization-accuracy-makeiset-theme-normalization.md)
- **Extends:** [ADR-0028](0028-categorization-accuracy-makeiset-theme-normalization.md)
  (disambiguating `categoryHints`)
- **Follows:** [ADR-0006](0006-ai-in-exactly-two-places.md),
  [ADR-0009](0009-grounding-is-structural.md) (the desk correction loop),
  [ADR-0003](0003-poro-for-both-roles.md)/[ADR-0015](0015-poro-real-corpus-tuning.md)
  (Poro is the fixed model; fix the usage, not the model)

## Context

Live use surfaced Poro-2-8B mis-classifying grossly and inconsistently: produce
("avokado", "soijapapu") landing in `muu`; a customer-service compliment
("myyjä oli energinen") landing in `maito_kylma`; detergent in `muu`; sports
banter ("Norja olisi voinut voittaa britit") rated a negative complaint; two
football comments split across categories. This is the model-quality ceiling
already recorded (ADR-0003/0015) — an 8B model on messy Finnish. The owner chose
three discipline levers. None of them pretends to lift the ceiling; they make the
ceiling's failures cheaper and more visible.

## Decision

1. **The catch-all is a single category** — retires ADR-0026's emergent-topic
   split. The live-summary view had grouped the catch-all (`muu`) into emergent
   topics keyed on the structuring model's free-text theme. In practice that
   fragmented `muu` into thin, tag-like sections, and mis-classified items
   surfaced as nonsense standalone topics ("avokado" as its own section). `muu`
   now renders as ONE department, exactly like every other category; the
   whole-window synthesis (ADR-0026, kept) still carries the qualitative
   narrative. Removed with it: the `ReportTheme.IsEmergentTopic` contract field
   (no committed snapshot depended on it), the `CleanTheme`/`ThemeGroupKey`
   theme normalizers (ADR-0028's key-normalization part), and the frontend
   topic-title / sub-tag rendering. `CatchAllCategory` stays — it still marks the
   bucket that operator maintenance re-structures when the vocabulary grows
   (`IngestService`).
2. **Tighter disambiguating hints** — extends ADR-0028. Added `categoryHints`
   for the two boundaries live use showed the model getting wrong: `hevi`
   (produce — explicitly naming avokado/soijapapu, which were landing in `muu`)
   and `kassa_palvelu` (staff/service conduct — which was landing in product
   departments). The `muu` hint is tightened from "feedback that fits no
   department" to "pick a real department first." Hints stay the LLM-recall
   precision layer: they nudge, they never force (only the alert lexicon forces a
   category — ADR-0027). Hints are domain data rendered into the locked prompt
   template at load time, so this is **not** a prompt-file change and does not
   trip the ADR-0022 lock. Self-explanatory departments stay hint-less to keep
   the rendered prompt lean.
3. **The desk correction loop is the backstop** — existing (ADR-0006/0009), made
   explicit here as the discipline mechanism. Hints only nudge a model with a
   ceiling; the load-bearing correction is the desk clerk accepting or
   overriding the model's category (a `<select>`) *before* save, with the
   override captured as correction telemetry. The model-vs-human category
   correction rate is already exposed (`GET /telemetry/corrections`, feedctl
   `telemetry`) and is the ongoing quality measure. The decision is to lean on
   this loop as the answer to the ceiling, honestly — not to claim hints fix it.

## Consequences

- `muu` reads as one department; the desk and management views no longer spray
  nonsense emergent topics. Numbers are unchanged (same items, one group).
- Two concrete mis-classification boundaries now carry explicit hints; the model
  still misses, but the desk correction + telemetry loop measures and fixes what
  it misses.
- This is a real reversal of ADR-0026's catch-all feature. The replacement is the
  single `muu` section plus the whole-window synthesis; the ADR-0026 emergent
  mechanism and its ADR-0028 key normalization are gone.
- **Report-contract change:** `ReportTheme` loses `IsEmergentTopic`. `/live/report`
  and both frontends are updated in the same change; the offline snapshot did not
  carry the field.
- No model or prompt-file change → no ADR-0022 gate, no live-Poro re-check
  required for the mechanism. (Category-quality gains from the new hints are a
  model-behavior question, measured by the correction telemetry over time, not
  asserted here.)
