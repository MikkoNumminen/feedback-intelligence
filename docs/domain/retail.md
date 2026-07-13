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

The `category` field ([../schema.md](../schema.md)). Fifteen values, each with a
Finnish display label; `categoryFieldLabel` is `osasto`:

```
maito_kylma | hevi | kuiva_elintarvike | liha_kala | leipa | kassa_palvelu |
piha_puutarha | rakennustarvike | tyokalut | sisustus_maalit | sahko_lvi |
varasto_nouto | verkkokauppa_toimitus | asiaton | muu
```

`asiaton` (Asiaton palaute) holds abusive/racist/harassing content with no
feedback substance; a `categoryHints` entry explains it to the structuring
model without lengthening the display label. `muu` is declared the
`catchAllCategory`: the desk's live summary splits it into emergent topics
named by the model's free-text theme
([ADR-0026](../decisions/0026-categories-emergent-topics-live-summary.md)).

Severities and types are not overridden — retail inherits the core defaults
(`low/medium/high/critical`, `complaint/praise/suggestion/question/other`).

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
