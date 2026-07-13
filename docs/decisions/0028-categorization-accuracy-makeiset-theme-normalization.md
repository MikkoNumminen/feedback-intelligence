# ADR-0028 — Categorization accuracy: a `makeiset` department, disambiguating hints, and normalized emergent-topic keys

- **Status:** Accepted (2026-07-14)
- **Deciders:** Mikko

## Context

Two accuracy defects surfaced while reading the live desk output.

1. **A taxonomy gap, not a model error.** Candy (`irtokarkki`, karkkipussit,
   suklaa) was landing in `kuiva_elintarvike` ("Kuivat elintarvikkeet"). The
   structuring prompt forces the model to return the *closest* department key
   from the domain list, and the retail taxonomy had no sweets department — so
   `kuiva_elintarvike` genuinely was the closest fit. The model could not do
   better than the vocabulary allowed.

2. **Emergent-topic fragmentation.** The desk live-summary splits the catch-all
   category into emergent topics keyed on the model's own free-text `theme`
   (ADR-0026). The `theme` field had no format contract, so the same topic
   arrived spelled several ways — `tuotteiden_laatua`, `tuotteiden laatu`,
   `tuotteiden laatua` — and the grouping key (`Theme.Trim().ToLowerInvariant()`)
   treated each variant as a separate topic, fragmenting one signal into several
   thin ones.

Both are precision problems in the *config and the plumbing*, not the model
weights, so both are fixable without touching the (owner-decided) structuring
model.

## Decision

1. **Add `makeiset` ("Makeiset") as a 17th retail department**
   (`domains/retail/domain.json`), positioned after `leipa`. Sweets are a
   distinct, high-frequency retail department; giving the model the key it was
   missing is the direct fix for the misfiling.

2. **Use `categoryHints` to disambiguate the boundary that caused the error.**
   `makeiset` gets a hint listing its contents (karkkipussit, irtokarkit,
   suklaa, purukumi, pastillit); `kuiva_elintarvike` gets a hint that names its
   own contents and explicitly excludes sweets (`… — EI makeisia eikä
   irtokarkkeja`). The hint mechanism already existed (ADR-0026/0027) and renders
   into the prompt parenthetical; this is zero new code, and it targets exactly
   the confusable pair rather than bloating every category.

3. **Normalize the emergent-topic key deterministically**
   (`ReportService.ThemeGroupKey`): replace `_` with spaces, collapse whitespace
   runs, trim, lowercase. This is the deterministic-layer-first fix for the
   fragmentation (AGENTS.md: "a rule that works beats a model that usually
   works") — it merges the exact `tuotteiden_laatua` / `tuotteiden laatu`
   variants that were observed, independent of the model. The display title is
   cleaned the same way (separators normalized, **casing preserved**) so the
   shown topic name matches the key it was grouped under.

### Deferred — gated by ADR-0022

A fourth mechanism was considered and is **recommended but not applied in this
change**: constraining the `theme` field format in `prompts/structuring-v0.txt`
(a 2–4 word noun phrase, lowercase, base/dictionary form, plain spaces — never
underscores, hyphen-separators, or camelCase). That would attack the drift at
its source and reduce reliance on the deterministic backstop above. But
`structuring-v0.txt` is a **locked prompt (ADR-0022)**: editing it is gated on
re-running the A4 red-team fixture *and* an announced seed-42 live Poro check on
the shared GPU, then updating the pinned hash in the same commit. Because that
live check requires the owner to free the GPU, the prompt edit is left for a
follow-up that can complete the gate honestly. The deterministic normalization
(item 3) already fixes the observed symptom without it.

## Consequences

- **Finnish morphology is deliberately NOT merged.** `laatu` vs `laatua` vs
  `laatukysymys` remain distinct topic keys. True lemma-merging needs a Finnish
  lemmatizer, which is disproportionate here; an occasional near-duplicate topic
  is an accepted cost. Over-merging two genuinely distinct topics would be worse
  than showing two near-duplicates, so the normalization stays conservative
  (separators and case only, never stemming). The deferred prompt "base form"
  constraint (above) would further reduce inflection variants at the source once
  its ADR-0022 gate is run.
- **A theme that is only separators** (e.g. `"___"`) now normalizes to an empty
  key and routes the item to its own category bucket instead of creating a
  nameless emergent topic — a small robustness win over the previous code, which
  would have shown an underscore-titled topic.
- **The change is scoped to the live-summary (desk) grouping branch.** The
  standard management-report path (which groups by category, not by theme) is
  untouched.
- **The demo's committed snapshot predates these changes** and re-categorizes
  only if regenerated — consistent with how ADR-0026/0027 handled the same
  snapshot-staleness tradeoff. Any regeneration needs the shared GPU (announce
  first) and, for an *evidential* accuracy claim, hand-written boundary corpus
  from the owner (AGENTS.md forbids invented evidential Finnish corpus).
- **Downstream sync cost:** adding a category means docs that enumerate the set
  (`docs/domain/retail.md`) and any test fixtures that hardcode it are updated in
  the same change; the frontends render categories from the domain descriptor
  (`/schema`), so they need no code change.
