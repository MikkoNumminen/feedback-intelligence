# ADR-0032 — Non-substantive categories are "unrated"; add `ei_palautetta` for junk (manual)

- **Status:** Accepted (2026-07-14)
- **Deciders:** Mikko
- **Follows:** [ADR-0027](0027-racism-recognition-alert-lexicon.md) (rasismi),
  [ADR-0026](0026-categories-emergent-topics-live-summary.md) (asiaton, demotion),
  [ADR-0030](0030-sentiment-indicator-deterministic-from-type.md) /
  [ADR-0031](0031-model-authored-sentiment-field-optional.md) (sentiment)

## Context

Every item gets a severity and (via ADR-0030) a positive/negative read. On the
categories that are **not substantive feedback** — `rasismi`, `asiaton` — those
signals are misleading: a slur is not "high-severity negative feedback," it is
racist content, flagged. The category is the signal; a good/bad/how-severe rating
dressed on hostile or junk content makes it read as a serious complaint. The
owner also asked whether the model can recognize outright **garbage** (nonsense
that isn't feedback at all) and stop presenting it as serious.

## Decision

1. **Demoted categories are treated as UNRATED.** For any category in
   `demotedCategories`, the report and all views suppress **both** the severity
   badge and the sentiment badge, and exclude those items from the severity and
   sentiment aggregates. Implementation:
   - `ReportService.SentimentOf` returns null for demoted categories → no
     sentiment badge, dropped from every mix.
   - `ReportTheme.Unrated` (server-set from `demotedCategories`) drives the
     views: desk, management, and the offline snapshot skip the severity badge and
     drop the theme's items from the severity chart; feedctl inherits it through
     the same report shape.
   - Deterministic, no model dependency. `demotedCategories` now carries a
     strengthened meaning: **not substantive → sort last AND unrated.** For the
     retail set (rasismi, asiaton, ei_palautetta) that is exactly right.
   - **Alerts are unaffected** — they run off the deterministic lexicon/⚑ and the
     LLM nomination pass, not the severity badge.

2. **Add `ei_palautetta` ("Ei palautetta") as a demoted category** for genuine
   non-feedback / nonsense. It is **domain config** (a `domain.json` category +
   `categoryHint`), so it renders into the structuring prompt at runtime with no
   edit to the locked `structuring-v0.txt` — no ADR-0022 gate (same as the
   `makeiset`/taxonomy additions).

## The honest result on automatic garbage detection

An announced live check (real Poro, isolated instance) probed three nonsense
inputs. **Poro routed none of them to `ei_palautetta`:** the pork/timber text →
`liha_kala/praise/high`, keyboard mash → `muu/other`, and "singing carrots"
nonsense → **`rasismi`** (a false positive on innocent gibberish). The
real-feedback control classified correctly. So — exactly like the model-authored
sentiment field (ADR-0031) — **the model ignores the affordance.**

Therefore:

- **`ei_palautetta` is a MANUAL category, not automatic detection.** A desk clerk
  who sees junk selects "Ei palautetta"; it is then unrated (no severity/sentiment)
  and sorts last. This is the human-in-the-loop correction the desk exists for.
- **Automatic garbage recognition is not achievable with Poro-2-8B.** Grammatical
  nonsense can't be flagged deterministically either. Documented, not pretended.

## Consequences

- rasismi / asiaton / ei_palautetta rows now show only the category (+ its ⚑ tag
  and count) — no "Korkea", no "Kielteinen" — and drop out of the severity chart
  and the sentiment mix. The overall/positive-negative numbers reflect *real*
  feedback only.
- **Garbage in a NORMAL category is not fixed by this.** The pork/timber text the
  model files under `liha_kala` still shows as rated, because that category isn't
  demoted. The deterministic suppression only helps categories that *are* demoted;
  a clerk re-tagging junk to `ei_palautetta` is the remedy.
- `ei_palautetta` ships **model-inert** (a forward-compat + manual affordance),
  like the sentiment field — kept because a clerk can use it today and a future
  model might route to it. Easy to drop if the empty-ish category isn't wanted.
- Broadening `demotedCategories` to also mean "unrated" is a semantic change; any
  future domain that demotes a *substantive* category purely for ordering would
  also lose its severity/sentiment. Acceptable — demotion already means
  "not-substantive content that must not lead."
