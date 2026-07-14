# ADR-0032 — Non-substantive categories are "unrated" (no severity/sentiment)

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
dressed on hostile content makes it read as a serious complaint. The owner also
asked whether the model could recognize outright **garbage** (nonsense that isn't
feedback at all) and stop presenting it as serious.

## Decision

**Demoted categories are treated as UNRATED.** For any category in
`demotedCategories` (retail: `rasismi`, `asiaton`), the report and all views
suppress **both** the severity badge and the sentiment badge, and exclude those
items from the severity and sentiment aggregates:

- `ReportService.SentimentOf` returns null for demoted categories → no sentiment
  badge, dropped from every mix.
- `ReportTheme.Unrated` (server-set from `demotedCategories`) drives the views:
  desk, management, and the offline snapshot skip the severity badge and drop the
  theme's items from the severity chart; feedctl inherits it through the same
  report shape.
- Deterministic, no model dependency. `demotedCategories` now carries a
  strengthened meaning: **not substantive → sort last AND unrated.**
- **Alerts are unaffected** — they run off the deterministic lexicon/⚑ and the LLM
  nomination pass, not the severity badge.

## Considered and rejected: an `ei_palautetta` (not-feedback) category

To catch genuine nonsense, a demoted `ei_palautetta` bucket was tried (domain
config, so no locked-prompt gate). An **announced live check** (real Poro,
isolated instance) probed three nonsense inputs and Poro routed **none** of them
there: the pork/timber text → `liha_kala`, keyboard mash → `muu`, and "singing
carrots" nonsense → **`rasismi`** (a false positive on innocent gibberish). The
real-feedback control classified correctly.

So — exactly like the model-authored sentiment field (ADR-0031) — **the model
ignores the affordance.** With no automatic routing, the category's only use was
manual desk tagging, which did not justify an otherwise-unused category in the
demo taxonomy. **`ei_palautetta` was dropped.** The takeaway stands: **automatic
garbage recognition is not achievable with Poro-2-8B**, and grammatical nonsense
cannot be flagged deterministically either. The desk clerk remains the check for
junk (they can re-categorize or discard it).

## Consequences

- `rasismi` / `asiaton` rows now show only the category (+ its ⚑ tag and count) —
  no "Korkea", no "Kielteinen" — and drop out of the severity chart and the
  sentiment mix. The overall positive/negative numbers reflect *real* feedback only.
- **Garbage in a NORMAL category is not fixed by this.** The pork/timber text the
  model files under `liha_kala` still shows as rated, because that category isn't
  demoted; the deterministic suppression only helps categories that *are* demoted.
  A clerk re-tagging or discarding junk at the desk is the remedy.
- Broadening `demotedCategories` to also mean "unrated" is a semantic change: any
  future domain that demotes a *substantive* category purely for ordering would
  also lose its severity/sentiment. Acceptable — demotion already means
  "not-substantive content that must not lead."
