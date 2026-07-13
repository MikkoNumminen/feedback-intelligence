# ADR-0027 — Racist-comment recognition: a category, forced by a deterministic lexicon

- **Status:** Accepted (2026-07-13)
- **Deciders:** Mikko

## Context

The owner needs racist comments recognized **as their own category** — flagged,
never cancelled: the comment stays stored, stays counted, stays visible; it is
named for what it is. The broad `asiaton` bucket (ADR-0026) files hostile
content away but does not name the offense, and an LLM-only categorization can
miss or drift. Live testing showed both faces of the problem: blunt slurs
("Onko olemassa neekereitä?") and novel phrasings a wordlist can never
anticipate (racism smuggled mid-sentence into a fabric request, which only the
LLM caught).

## Decision

1. **`rasismi` is a declared structuring category** in the retail domain
   ("Rasistinen palaute"), demoted below every normal section but ABOVE
   `asiaton` — `demotedCategories` is now ordered, and views sort demoted
   sections by their declared position (server orders, desk phase A mirrors
   with `indexOf`).
2. **A `rasismi` alert-lexicon category forces the categorization.** The
   general rule (implemented in `AlertMatcher.CategoryOverride`): an
   alert-lexicon category whose name IS a declared structuring category
   categorizes the item deterministically. The forced category outranks the
   model AND desk acceptance, because the lexicon is precision-tuned rule data
   no human edits — recognition must not depend on either. `/interpret`
   previews the same override (and returns `alertCategories` so the clerk sees
   the ⚑ before saving), so ingest re-asserting it only matters when the
   preview was edited away.
3. **The lexicon is the precision layer** (`domains/retail/alert-keywords.json`):
   case-insensitive Finnish stems, matched at ingest — never sleeps, never
   hallucinates. Ambiguous stems are documented exclusions in the lexicon
   itself (`matu` false-positives on "maturiteetti", `ryss` on "Bryssel",
   `mamu`/`manne`/`hurri` contextual). **The LLM layer is the recall net**:
   contextual/novel racism routes to the `rasismi` category via
   `categoryHints`, and the A2 injection scan still flags manipulation.
4. **Per-item visibility**: `ReportSourceItem` carries the item's alert
   categories, and both views tag the row (`⚑ rasismi`) — the desk segment's
   item lists, the demo view's message dialog, the desk alert block, and the
   interpret preview. Recognition is visible on the comment itself, not only
   in an aggregate.
5. **Existing data adapts**: the restructure pass re-stamps deterministic
   alerts for EVERY stored item and applies the category override without an
   LLM call — even to items the ADR-0026 bounded scope would skip. The forced
   category outranks a human category-audit for the same reason it outranks
   desk acceptance; the store update clears stored corrections like any
   restructure (they audited a categorization that no longer stands).
   `feedctl restructure` after a lexicon change re-recognizes the whole live
   channel with zero LLM cost.

## Consequences

- A slur-bearing comment lands in "Rasistinen palaute" the moment it is saved,
  deterministically, carries the `⚑ rasismi` tag, and appears in Hälytykset —
  the alert channel now carries conduct as well as safety, a deliberate
  widening of its meaning. Nothing is dropped or hidden.
- A clerk cannot file a lexicon-hit comment elsewhere: the override wins at
  save time even if the previewed category was edited. This is the one place
  a deterministic rule outranks a human in this system, and it is scoped to
  alert categories that are also declared categories.
- Wordlists under-recognize by design; the documented split (lexicon =
  precision, `categoryHints` = recall) sets that expectation. The lexicon's
  `deliberateExclusions` block records every precision call so future edits
  don't relitigate them blindly.
- **The LLM recall layer over-recognizes one edge**: live probing showed
  Poro-2-8B assigns `rasismi` to substance-free pure profanity ("Möivät
  paskaa...") deterministically (temp 0 — hint rephrasing and list reordering
  were both tried and changed nothing), while profanity WITH feedback
  substance classifies correctly. Accepted: the misfiled item lands in a
  demoted hostile-content section next to `asiaton`, the missing `⚑ rasismi`
  tag distinguishes model-judged from lexicon-confirmed racism per comment,
  and the desk clerk can correct the category at /interpret.
- The demo's committed snapshot predates the category; it re-recognizes only
  if the snapshot is ever regenerated — acceptable, the snapshot corpus
  contains no racist content.
