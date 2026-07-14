# ADR-0030 — Sentiment (positive/negative) indicator: deterministic from type now, model-authored later

- **Status:** Accepted (2026-07-14)
- **Deciders:** Mikko
- **Relates to:** [ADR-0022](0022-lock-poro-prompts-v0.md) (locked structuring
  prompt — why the model-authored field is gated), [ADR-0006](0006-ai-in-exactly-two-places.md)
  (deterministic layer first), [ADR-0012](0012-pluggable-domain-modules.md)
  (domain-neutral core, taxonomy is config)

## Context

The owner wants an at-a-glance indicator of whether a piece of feedback is
positive or negative. The structuring schema already carries a `type` field the
model produces — `complaint`, `praise`, `suggestion`, `question`, `other` — which
already encodes most of the polarity: praise is positive, a complaint is
negative. So a positive/negative indicator can be **derived deterministically
from an existing field**, with no model change and — decisively — without editing
the **locked** structuring prompt (ADR-0022), whose change is gated on an
announced live-GPU re-check.

A true model-authored sentiment field would be more nuanced (it would catch an
angrily-phrased "suggestion" or backhanded "praise" that the type map misses),
but it requires that gated prompt change plus a stored-field migration. Offered
the choice, the owner chose **both**: ship the deterministic indicator now, and
queue the model-authored field as a gated follow-up that reuses the same
plumbing.

## Decision

1. **Sentiment is a three-value polarity** — `positive` / `negative` / `neutral`
   — with an explicit neutral, because a question or a constructive suggestion is
   not a polarity signal and forcing it into positive/negative would mislabel it.
   Labels are domain config (retail → *Myönteinen* / *Kielteinen* / *Neutraali*).

2. **Derived deterministically from `type`** via a domain `typeSentiment` map
   (retail: complaint→negative, praise→positive, suggestion/question/other→
   neutral). `CoreDefaults` supplies the same mapping and labels so every domain
   gets sentiment for free; a domain may relabel or remap. No new stored field,
   no DB migration, no model or prompt change, no ADR-0022 gate — the indicator
   is computed at report time from data already stored.

3. **One seam for the future swap:** `DomainDescriptor.SentimentOf(type)`.
   Callers ask the domain, never the type map directly. When the model-authored
   field lands, only this method changes (prefer the model's `sentiment`, fall
   back to the type map); the report shape, `/schema`, and the frontends are
   untouched.

4. **Surfaced end to end:** the report attaches `sentiment` per source item,
   `sentimentCounts` per theme, and a whole-window `sentimentCounts`; `/schema`
   exposes `sentiments` + `sentimentLabels` + the `typeSentiment` map; the desk
   and management pages render a colored badge per item and a count-pill mix per
   theme and overall; the offline snapshot renders the same (works with the
   backend down).

5. **Config + validation** (ActiveDomain, mirroring the `categoryHints` rules): an
   explicit `typeSentiment` is typo-checked (keys ⊆ `types`, values ⊆
   `sentiments`); when omitted, the core default is intersected with the types the
   domain actually declares, so a domain that renamed its types still loads with a
   consistent, non-empty map.

## Deferred — gated by ADR-0022

A **model-authored `sentiment` field** on the structuring output (a genuine
per-item polarity judgement, not a type proxy). It requires editing
`prompts/structuring-v0.txt` (the ADR-0022 gate: A4 red-team fixture green + an
announced seed-42 live Poro check + a hash update in the same commit) and a
stored-field migration. It is **not** in this change. When done, it swaps in
behind `SentimentOf` and this deterministic map becomes the fallback for
structure-failed / legacy items.

## Consequences

- **Immediate, gate-free positive/negative signal**, consistent across the desk,
  management, and offline-snapshot views, and aggregatable ("61% negative").
- **It is exactly as good as the `type` classification.** A message the model
  types as a neutral "suggestion" but that is really an angry complaint reads
  neutral; a mistyped item is mis-sentimented. This is the honest limit of a
  deterministic proxy and the reason the model-authored field is queued — stated
  so no one reads the badge as a true sentiment model.
- **No migration, no new stored data**; the indicator is derived, so it also
  applies retroactively to every already-stored item with a type.
- **Sentiment is on by default** for every domain (core default map); a domain
  opts out only by declaring empty maps. Retail declares its own so the mapping
  is auditable in `domain.json`.
- **The committed demo snapshot** gains sentiment only when regenerated (same
  tradeoff as ADR-0026/0027/0028/0029).
- A dedicated sentiment-distribution **chart** (parallel to the severity bar) is
  a deliberate follow-up; the badges and count-pills carry the indicator now.
