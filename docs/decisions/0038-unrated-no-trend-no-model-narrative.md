# ADR-0038 — Unrated content carries no trend either, and demoted categories get no model narrative

- **Status:** Accepted (2026-07-15)
- **Deciders:** Mikko
- **Completes:** [ADR-0032](0032-unrated-nonsubstantive-categories.md) (demoted =
  unrated: no severity/sentiment), [ADR-0033](0033-operational-alerts-moderation-view.md)
  (non-substantive content in a collapsed moderation view)

## Context

ADR-0032 made demoted categories (retail's `rasismi`, `asiaton`) **unrated** — no
severity badge, no sentiment badge, dropped from those aggregates. But two
severity-derived signals still leaked onto the moderation view:

1. **The standard report path** (`/report`, the management view and the persisted
   offline snapshot) ran a full LLM **synthesis** over demoted groups too. The
   synthesis data block feeds the model each item's severity (the distribution and
   the per-excerpt `(critical)` tag), so the model could editorialize a rating back
   into the moderation card's title/narrative — `"'<slur>' (korkea)"` — the exact
   thing ADR-0032 removed from the badges. (The live-summary/desk per-group
   narratives were already deterministic. The whole-window **Yhteenveto** — the
   live-summary `Overall` — is a *separate* surface, kept rated-only by its
   companion change, the ADR-0033 §3 amendment; **this** ADR covers the
   per-category moderation themes and the trend.)
2. **The trend/direction** (`kasvava`/`paheneva`/…) was computed and displayed for
   demoted themes on the desk, management, and snapshot cards. `paheneva`
   (worsening) is **severity-derived** (it requires a rising average severity), so a
   moderation card could show a severity judgment on hostile content.

Both contradict ADR-0032's meaning: on non-substantive content **the category is
the signal** — no good/bad/how-severe/which-way read.

## Decision

**Unrated (demoted) content carries no trend, and no model-authored narrative.**

- A demoted category is built as a **count-only moderation theme** (`ReportService.
  BuildModerationTheme`), in **both** the standard and live-summary paths: direction
  is the neutral `stable` key with an **empty** label, and the narrative is a
  deterministic count line (`ReportText.ModerationNarrative`) — the model is never
  run over a demoted category's **per-group narrative**, so it cannot editorialize a
  severity into the moderation card. This also returns the LLM budget the demoted
  synthesis used to spend. (The whole-window Yhteenveto is kept off demoted content
  by its companion rated-only `Overall` change, ADR-0033 §3.)
- **The views suppress the trend for unrated themes**, symmetric with how they
  already suppress severity/sentiment: desk, management (`index.html`), and the
  offline snapshot (`SnapshotHtml`) render only the count on a moderation card.
- Deterministic, no model dependency. Recognition is untouched (ADR-0027): the item
  stays classified, keeps its `⚑` tag, and is counted in "Moderoitava sisältö (N)".

## Consequences

- A moderation card now shows the category, its count, and its content behind a
  click — no severity, no sentiment, **no trend**, no model prose. The minimal
  moderation view ADR-0033 intended.
- The standard `/report` path makes **one fewer LLM call per demoted category**, and
  can no longer produce an ungrounded/action-bearing narrative over hostile content.
- `stable` + empty label is a deliberate "no trend," not a measured "stable": demoted
  volume changes are not surfaced as a trend, because a trend is an editorial read we
  do not put on non-substantive content. If a demoted-volume signal is ever wanted
  (e.g. a brigading spike), it would be a separate, explicitly volume-only indicator,
  not the severity-capable `direction`.
- Scope: unrated now means **no severity, no sentiment, no trend, no model
  narrative** — the full "the category is the signal" contract.
