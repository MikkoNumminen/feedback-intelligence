# ADR-0029 — Complete the retail department taxonomy to a full hybrid hypermarket

- **Status:** Accepted (2026-07-14)
- **Deciders:** Mikko
- **Follows:** [ADR-0028](0028-categorization-accuracy-makeiset-theme-normalization.md)
  (which added the first missing department, `makeiset`, and established the
  `categoryHints` disambiguation pattern)

## Context

ADR-0028 fixed one taxonomy gap (candy → `kuiva_elintarvike`) by adding a single
department. But the same failure mode — the structuring model forced to pick the
*closest* department when the right one does not exist — applies to every basic
retail department the taxonomy was still missing: beverages, frozen food, ready
meals, household goods, toiletries, appliances, clothing, and so on all had to
overflow into an ill-fitting department or into the `muu` catch-all. A demo whose
category chart is credible to a retail employer needs the department set to look
like a real store, not a partial list.

The store is already modelled as a **hybrid hardware-store / grocery**
(rautakauppa + ruokakauppa). The decision was how *complete* to make it; the
owner chose the **full hypermarket** scope (the widest of the offered options).

## Decision

Expand the retail `categories` from 17 to **30** — 27 real departments plus the
three special buckets (`rasismi`, `asiaton`, `muu`). The 13 added departments:

- **Groceries:** `juomat` (Juomat), `pakasteet` (Pakasteet), `einekset`
  (Einekset), `oluet_siiderit` (Oluet & siiderit).
- **Non-food consumer goods:** `koti_taloustavara` (Koti & taloustavara),
  `hygienia_kauneus` (Hygienia & kauneus), `lastentarvikkeet` (Lastentarvikkeet),
  `lemmikki` (Lemmikkitarvikkeet), `kodinkoneet` (Kodinkoneet),
  `vaatteet_jalkineet` (Vaatteet & jalkineet), `vapaa_aika` (Vapaa-aika &
  urheilu), `autotarvikkeet` (Autotarvikkeet).
- **Store service & experience:** `tilat_siisteys` (Tilat & siisteys) — the one
  non-product "department", for feedback about premises, cleanliness, toilets,
  parking, and carts, which otherwise lands in `muu`.

The `categories` map is reordered into display groups (groceries → non-food
consumer goods → home improvement → store service → special) so the desk/schema
category list reads like a store layout. Ordering is display-only: report
sections still sort by item count then ordinal key, and `demotedCategories`
(`rasismi`, `asiaton`) still sort last.

`categoryHints` are added **only** for genuinely confusable boundaries —
`juomat`/`oluet_siiderit`/`maito_kylma`, `einekset`/`liha_kala`,
`pakasteet`/`maito_kylma`, `kodinkoneet`/`sahko_lvi`, `koti_taloustavara`, and
`tilat_siisteys` (flagged as not-a-product-department). `kuiva_elintarvike`'s
hint is widened to also exclude the now-existent `juomat` and `pakasteet`.
Self-explanatory labels (`lastentarvikkeet`, `vaatteet_jalkineet`, `lemmikki`,
`hygienia_kauneus`, `vapaa_aika`, `autotarvikkeet`) get no hint, keeping the
rendered prompt lean (ADR-0028's hint-only-where-needed rule).

## Consequences

- **Config only, no engine code.** Categories and labels render from the domain
  descriptor via `/schema`; the frontends, the report engine, and the generator
  are all domain-neutral (ADR-0012), so none change. The generator stories
  (`maito_kylma`, `rakennustarvike`, `leipa`) and their ground-truth are a subset
  of the taxonomy and are unaffected — adding departments never invalidates them.
- **Empty departments do not clutter the report.** Report sections are built from
  grouped items, so a department with no feedback simply has no section. The full
  list appears only where a full list belongs: the `/schema`-driven desk category
  picker and legend.
- **The rendered structuring prompt grows** by ~13 label lines and a handful of
  hints. That is a real token/latency cost and a change to the model's input, but
  it is the intended trade for accurate routing; `prompts/structuring-v0.txt`
  itself (the locked template, ADR-0022) is untouched — only the domain data it
  renders changed.
- **`oluet_siiderit` scopes alcohol to the Finnish grocery reality** (mild beer
  and cider ≤5.5 %), keeping it distinct from non-alcoholic `juomat`.
- **The committed demo snapshot predates the expanded taxonomy** and
  re-categorizes only if regenerated (same tradeoff as ADR-0026/0027/0028). A
  regeneration needs the shared GPU (announce first) and, for evidential accuracy
  claims, hand-written boundary corpus from the owner (AGENTS.md).
- **Not yet measured on real Poro.** Whether the model routes cleanly across 27
  departments (vs. confusing, say, `koti_taloustavara` and `kuiva_elintarvike`)
  is an open question for the next announced live eval; the hints encode the
  current best guess at the confusable boundaries, to be tuned against results.
