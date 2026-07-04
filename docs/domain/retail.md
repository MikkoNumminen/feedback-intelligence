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

The `category` field ([../schema.md](../schema.md)). Fourteen values, each with a
Finnish display label; `categoryFieldLabel` is `osasto`:

```
maito_kylma | hevi | kuiva_elintarvike | liha_kala | leipa | kassa_palvelu |
piha_puutarha | rakennustarvike | tyokalut | sisustus_maalit | sahko_lvi |
varasto_nouto | verkkokauppa_toimitus | muu
```

Severities and types are not overridden — retail inherits the core defaults
(`low/medium/high/critical`, `complaint/praise/suggestion/question/other`).

The structuring model receives these values through the neutral core prompt
(`prompts/structuring-v0.txt`), templated from the active domain at load time;
the desk UI renders the labels from `/schema`. **No retail value is written into
engine code** — the boundary gap ADR-0007 flagged is closed (ADR-0012).

## Alert keywords (`domains/retail/alert-keywords.json`)

Loaded and validated at startup. Case-insensitive substring match over the raw
feedback text, Finnish stems, in three categories: injury/safety, payment,
legal-threat. The file also records **deliberate exclusions** — structural-failure
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
