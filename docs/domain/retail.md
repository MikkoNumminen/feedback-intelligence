# Domain: Finnish retail (first application)

> **First application, a data-only module, not core.** This is the retail
> taxonomy that parameterizes the domain-neutral engine
> ([ADR-0007](../decisions/0007-domain-agnostic-core.md),
> [ADR-0012](../decisions/0012-pluggable-domain-modules.md)). It lives entirely
> under **`domains/retail/`** — the engine hardcodes none of it. The authoring
> contract common to all domains is in [../domains.md](../domains.md).

The retail application is a hybrid hardware-store / grocery, matching the
hand-written corpus.

## Category taxonomy (`domains/retail/domain.json`)

The `category` field ([../schema.md](../schema.md)). Thirty values (27 real
departments + the three special buckets `rasismi`/`asiaton`/`muu`), each with a
Finnish display label; `categoryFieldLabel` is `osasto`. The set models a full
Finnish hybrid hypermarket — groceries, non-food consumer goods, home
improvement, and store-service departments — so ordinary feedback has a real
department to land in rather than overflowing into `muu`
([ADR-0029](../decisions/0029-complete-retail-taxonomy-hypermarket.md)). Listed
in display order:

```
# groceries
maito_kylma | hevi | kuiva_elintarvike | liha_kala | leipa | makeiset |
juomat | pakasteet | einekset | oluet_siiderit |
# non-food consumer goods
koti_taloustavara | hygienia_kauneus | lastentarvikkeet | lemmikki |
kodinkoneet | vaatteet_jalkineet | vapaa_aika | autotarvikkeet |
# home improvement
piha_puutarha | rakennustarvike | tyokalut | sisustus_maalit | sahko_lvi |
# store service & experience
kassa_palvelu | tilat_siisteys | varasto_nouto | verkkokauppa_toimitus |
# special
rasismi | asiaton | muu
```

The demoted buckets (`rasismi`, `asiaton`) are **unrated**
([ADR-0032](../decisions/0032-unrated-nonsubstantive-categories.md)): the views
suppress their severity and sentiment and drop them from those aggregates — the
category is the signal, a good/bad/how-severe rating on hostile content is
misleading. A dedicated `ei_palautetta` bucket for nonsense was investigated and
**rejected** — an announced live check showed Poro does not route garbage there
(automatic garbage detection is not achievable with this model); see ADR-0032.
The unrated content also lives in a **collapsed "Moderoitava sisältö" disclosure
at the bottom** of the views, and **racism is no longer an alert**: the alert
channel (`Hälytykset`) is retail-operational only, so a racist comment is
recognized (category + `⚑` + count) but never leads the page
([ADR-0033](../decisions/0033-operational-alerts-moderation-view.md)).

`makeiset` (Makeiset) is the sweets department; it and `kuiva_elintarvike` carry
`categoryHints` that draw the boundary between them explicitly, so candy no
longer falls into "Kuivat elintarvikkeet"
([ADR-0028](../decisions/0028-categorization-accuracy-makeiset-theme-normalization.md)).
Hints are added only for the genuinely confusable boundaries — `juomat` vs
`oluet_siiderit` vs `maito_kylma`, `einekset` vs `liha_kala`, `pakasteet` vs
`maito_kylma`, `kodinkoneet` vs `sahko_lvi` — plus `tilat_siisteys`, the one
non-product "department" for premises/cleanliness/parking feedback; a
self-explanatory label (`lastentarvikkeet`, `vaatteet_jalkineet`) gets none, to
keep the rendered prompt lean. Live use surfaced two more misses: `hevi` now
carries a hint explicitly naming avokado/soijapapu, which were landing in
`muu`, and `kassa_palvelu` carries a hint for staff/service conduct, which was
landing in product departments; the `muu` hint itself is tightened from
"feedback that fits no department" to "pick a real department first"
([ADR-0035](../decisions/0035-categorization-discipline-muu-single-category-hints.md)).

`rasismi` (Rasistinen palaute) names racist content per comment — flagged and
KEPT, never dropped. Blunt racist vocabulary forces the category
deterministically via the alert lexicon (the `rasismi` lexicon category is
also a declared structuring category, so a hit overrides the model and desk
acceptance — [ADR-0027](../decisions/0027-racism-recognition-alert-lexicon.md));
novel or contextual racism reaches the same category through its
`categoryHints` entry — the wordlist is the precision layer, the LLM the
recall net. `asiaton` (Asiaton palaute) holds other abusive/harassing content
with no feedback substance. `demotedCategories` (`rasismi`, then `asiaton`)
sorts both LAST in that declared order, so hostile content never leads the
page. `muu` is declared the `catchAllCategory`, but it no longer splits into
emergent topics in the live summary: it renders as a single department,
exactly like every other category. The whole-window synthesis
([ADR-0026](../decisions/0026-categories-emergent-topics-live-summary.md))
still carries the qualitative narrative; `catchAllCategory` itself is kept
because it still marks the bucket operator maintenance re-structures as the
vocabulary grows
([ADR-0035](../decisions/0035-categorization-discipline-muu-single-category-hints.md)
retires the emergent-topic split).

Severities and types are not overridden — retail inherits the core defaults
(`low/medium/high/critical`, `complaint/praise/suggestion/question/other`).

**Sentiment** (`sentiments` + `typeSentiment`, [ADR-0030](../decisions/0030-sentiment-indicator-deterministic-from-type.md)):
retail relabels the polarity set to Finnish (`positive`→Myönteinen,
`negative`→Kielteinen, `neutral`→Neutraali) and declares the type→sentiment map
explicitly (Kehu→positive, Valitus→negative, Ehdotus/Kysymys/Muu→neutral). The
positive/negative indicator is derived from each item's `type` via this map —
deterministic, no model call — and rendered as a badge per item plus a mix per
theme and overall. An optional model-authored `sentiment` field also exists
([ADR-0031](../decisions/0031-model-authored-sentiment-field-optional.md)) and
would take precedence, but Poro-2-8B does not emit it, so the deterministic map
is the active source.

Output language (`language`): **`fi`** — retail's audience is Finnish only, so the
report prose, direction labels, snapshot, and the desk/management frontends all
render in Finnish ([ADR-0014](../decisions/0014-domain-output-language.md)). It is
the one domain that overrides the engine's `en` default.

Ingest channels (`sources`): `google_review | email | web_form | desk` — the
`source` values `POST /feedback` accepts. `desk` is the human-in-the-loop console
channel; the others are external feedback channels.

The structuring model receives these values through the neutral core prompt
(`prompts/structuring-v0.txt`), templated from the active domain at load time;
the desk UI renders the labels from `/schema`. **No retail value is written into
engine code** — the boundary gap ADR-0007 flagged is closed (ADR-0012).

## Alert keywords (`domains/retail/alert-keywords.json`)

Loaded and validated at startup. Case-insensitive substring match over the raw
feedback text, Finnish stems, in four categories: injury/safety, payment,
racism, legal-threat. The `racism` category still forces the `rasismi`
structuring category and tags the item `⚑` (ADR-0027), but it no longer surfaces
as an operational `Hälytys` — alerts are retail-operational, and racist content
is shown in the moderation view instead
([ADR-0033](../decisions/0033-operational-alerts-moderation-view.md)). The file
also records **deliberate exclusions** — structural-failure
verbs (pettää, sortua, irrota, antaa periksi, romahtaa) are *non-keywords on
purpose*: they are the no-keyword safety story's vocabulary, which must be
detectable only by understanding
([ADR-0006](../decisions/0006-ai-in-exactly-two-places.md)). Safety-story corpus
texts are verified against this list, not a guess of it.

## Generator stories (`domains/retail/stories.json`)

Three planted-story archetypes (the demo's ground truth), validated against the
retail taxonomy by `StoryLibrary` at generate time:

1. **dairy-freshness-worsening** — a repeating freshness/dairy signal across
   channels, worsening (a sequenced arc — [ADR-0011](../decisions/0011-sequence-preserving-arcs.md)).
2. **safety-no-keyword** — a safety complaint containing no alert keywords,
   detectable only by understanding.
3. **availability-slow-burn** — a slow-burn availability trend on one category.

Story definitions (category, theme keywords, sources, window, trend,
`minGroundedIds`, `expectAlert`) are domain data; the authoritative corpus
composition guidance is in
[../../data/corpus/README.md](../../data/corpus/README.md).

## Domain-voiced prompts (`domains/retail/prompts/`)

`synthesis-v0.txt` (management narrative, Finnish, retail-analyst persona) and
`alert-nomination-v0.txt` (safety/urgency screen) carry the retail voice and
language and live in the module, not the core.
