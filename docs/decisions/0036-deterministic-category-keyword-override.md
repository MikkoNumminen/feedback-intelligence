# ADR-0036 — Deterministic category-keyword override (grocery-core lexicon)

- **Status:** Accepted (2026-07-14)
- **Deciders:** Mikko
- **Follows:** [ADR-0027](0027-racism-recognition-alert-lexicon.md) (a lexicon forces a
  category), [ADR-0006](0006-ai-in-exactly-two-places.md) (deterministic layer first),
  [ADR-0012](0012-pluggable-domain-modules.md) (config domains),
  [ADR-0035](0035-categorization-discipline-muu-single-category-hints.md) (categorization
  discipline; the desk-correction backstop)

## Context

Poro-2-8B's recall on product nouns is incomplete: a moldy **nectarine** batch
("nektariinierä oli homeessa") was categorized `maito_kylma` (dairy) because
"nektariini" is outside the model's reliable vocabulary. `categoryHints` only
*nudge* (ADR-0028/0035) — they cannot force. But product departments are a
**finite, enumerable vocabulary**, exactly what the deterministic layer
(ADR-0006) is for. The alert lexicon already forces a category deterministically
(ADR-0027, `rasismi`); this applies the same idea to ordinary departments —
without raising an alert.

## Decision

1. **A new OPTIONAL domain lexicon** `category-keywords.json` maps a category to
   `{ terms, excludeIfContains }`. `CategoryKeywordMatcher` forces the category
   when a term matches the raw text (case-insensitive invariant substring, Finnish
   stems — the alert-lexicon contract) **and** no exclusion marker is present.
2. **Cross-category exclusions are the core of the design.** A derivative HEAD
   word (`mehu`, `jäätelö`, `kakku`, `suklaa`, `puikko`, `piirakka`) is an
   *exclusion* for the base-ingredient category **and** a *term* for the
   derivative category, so a compound routes to the product it actually is:
   `maitosuklaa` → makeiset, `juustokakku` → leipa, `kalapuikko` → pakasteet,
   `nektariinijogurtti` → maito_kylma, `omenamehu` → juomat. Because the exclusions
   do this routing, declaration order is **not** load-bearing for compounds —
   `maito_kylma` excludes `suklaa`, so `maitosuklaa` reaches `makeiset` whichever
   category comes first. The first declared category whose rule fires wins, so order
   is only a **tie-break** for the rare text that names two departments with no
   mutual exclusion (e.g. "banaania ja maitoa"); it comes from JSON → insertion
   order and is pinned by a test.
3. **Scope: the grocery-core product-noun departments** — `hevi`, `makeiset`,
   `maito_kylma`, `liha_kala`, `juomat`, `pakasteet`, `leipa`. Experience/process
   departments (`kassa_palvelu`, `tilat_siisteys`, `verkkokauppa_toimitus`,
   `varasto_nouto`, `muu`) are defined by *what happened*, not a noun, so keyword
   forcing would misfire — they stay with the model + hints + the desk-correction
   loop (ADR-0035). Non-food and home-improvement departments are a future
   extension: **pure data**, no code change, prioritized by what the correction
   telemetry shows the model getting wrong.
4. **Precedence + containment** (`CategoryOverrideResolver`, the ONE resolver used
   by ingest, `/interpret`, and restructure so a preview can never drift from what
   is stored): the alert override (ADR-0027 — safety/conduct) wins first; the
   category-keyword override applies only if the alert is silent **and** the
   current category is not demoted. Produce forcing is a categorization aid, never
   a conduct signal, and raises no alert (a nectarine is not a `Hälytys`).
5. The lexicon is **domain config** (ADR-0012): optional (a domain without the file
   forces nothing — the game domain is unaffected), validated at boot against the
   declared categories (a typo'd key fails the boot). **No prompt-file change** →
   no ADR-0022 gate. The enriched `hevi` hint stays the LLM **recall net** for
   names the list misses — wordlist = precision, LLM = recall, the ADR-0027 shape.

## Consequences

- Bare produce/grocery nouns land in the right department deterministically:
  "nektariini → hevi" is now a rule, not a model guess.
- Cross-category exclusions keep flavored/compound products in the department they
  belong to instead of over-forcing the base ingredient.
- The model and the desk-correction loop remain the recall net + backstop; the
  desk telemetry (ADR-0035) shows which departments to extend next, by data.
- Report contract unchanged — only the stored `category` value changes, exactly as
  the alert override already does.
- **Extending to more departments is pure data** — more entries in
  `category-keywords.json`, with cross-category exclusions curated the same way.
