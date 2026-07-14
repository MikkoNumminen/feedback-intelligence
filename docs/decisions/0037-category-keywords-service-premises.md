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
genuinely wrong on these (see the model-behavior findings doc). There is a clean way
to force these without stealing product complaints.

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
3. **Curation avoided the location-word trap.** A corpus false-positive scan caught
   that bare `kassa` over-forced premises-cleanliness complaints (which mention the
   checkout *area*) to service, so it was dropped; `myyjä`, `palvelu`, `jono`,
   `asiakaspalvelu`, `kassahenkilö`, `kassajono` remain for service, and `wc`,
   `vessa`, `pysäköinti`, `siivous`, `ostoskärry`, `roskat` for premises (`wc-paperi`
   excluded so toilet paper is not forced to premises).
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
