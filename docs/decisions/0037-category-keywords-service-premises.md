# ADR-0037 — Extend the category-keyword override to service/premises (products win, service is the fallback)

- **Status:** Accepted (2026-07-15)
- **Deciders:** Mikko
- **Extends:** [ADR-0036](0036-deterministic-category-keyword-override.md) (the
  category-keyword override, originally scoped to grocery-core product departments)
- **Follows:** [ADR-0006](0006-ai-in-exactly-two-places.md) (deterministic layer first),
  [ADR-0035](0035-categorization-discipline-muu-single-category-hints.md) (the
  desk-correction backstop)

## Context

ADR-0036 deliberately kept the deterministic override to **product-noun** grocery
departments, leaving experience/process departments (`kassa_palvelu`,
`tilat_siisteys`, …) to the model + hints + desk correction because they are
defined by *what happened*, not a product noun. Live use showed that boundary
leaking badly: Poro-2-8B has a strong attractor toward `maito_kylma` and defaulted
pure **service/premises** comments there — a helpful salesperson ("myyjä oli
energinen … neuvoi missä wc-tilat sijaitsevat"), fish-counter service, parking-lot
cleanliness. A measurement with the override off confirmed Poro's raw output is
genuinely wrong on these (the "myyjä oli energinen" case landing in `maito_kylma` is
recorded in [ADR-0035](0035-categorization-discipline-muu-single-category-hints.md);
the measured model behavior is in [docs/poro-findings.md](../poro-findings.md)).
There is a clean way to force these without stealing product complaints.

## Decision

1. **Add `kassa_palvelu` and `tilat_siisteys` to the category-keyword lexicon,
   declared LAST** (after the seven product departments).
2. **Products always win; service is the fallback.** The matcher returns the
   first-declared category whose rule fires, so with service declared last, a comment
   containing ANY product term routes to that product department, and a service rule
   fires only when no product noun is present:
   - `"myyjä sanoi että maito oli vanhaa"` → `maito_kylma` (the milk wins)
   - `"myyjä oli energinen … wc-tilat"` → `kassa_palvelu` (no product noun)

   This makes declaration order **load-bearing for product-vs-service precedence** —
   a stronger role than ADR-0036's within-product tie-break — and it is pinned by the
   order test.
3. **Curation, validated by a corpus false-positive scan.** The scan flagged and
   dropped several stems: bare `kassa` (it pulled premises-cleanliness complaints,
   which name the checkout *area*, into service), `neuvo` (it matched `ajoneuvo`, a
   vehicle), and `käytävä` (it matched the necessive verb `on käytävä`, "must visit").
   To keep a cleanliness complaint in premises even when it names staff,
   `kassa_palvelu` excludes the cleanliness stems `siivo`/`likai`, so
   `"henkilökunta ei siivonnut vessoja"` routes to `tilat_siisteys`. Premises terms
   are stems (`vess`, `parkki`, `pysäköin`, `siist`, `roska`) so inflected forms match
   (`vessoja`, `parkkipaikan`).
4. **Still excluded:** `verkkokauppa_toimitus`, `varasto_nouto`, `muu` — no reliable
   distinctive noun; they stay with the model + hints + desk.

## Consequences

- Pure service/premises comments land in the right department deterministically
  instead of Poro's `maito_kylma` default; the recurring "service → dairy" class is
  fixed.
- Product complaints keep their product department — a service word never steals a
  comment that names a product.
- **Accepted residual:** a service gripe that names a product *location*
  ("myyjä maitohyllyllä oli töykeä") routes to the product department, and a
  non-grocery product complaint that mentions "palvelu" may route to service. Both are
  rarer than pure service comments; the desk-correction loop (ADR-0035) is the backstop.
- No model or prompt-file change → no ADR-0022 gate. Report contract unchanged (only
  the stored `category` value changes, as with every override).
