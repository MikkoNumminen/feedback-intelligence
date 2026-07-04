# Domain: Finnish retail (first application)

> **First application, config-level, not core.** This document holds the
> retail-specific taxonomy that parameterizes the domain-agnostic engine
> ([ADR-0007](../decisions/0007-domain-agnostic-core.md)). The engine must not
> hardcode any of these values; where it currently does, that is flagged below
> as a gap to close, not as intended design.

The retail application is a hybrid hardware-store / grocery, matching the
hand-written corpus.

## Department taxonomy (the `department` enum)

The one domain-specific schema field ([../schema.md](../schema.md)). Fourteen
values:

```
maito_kylma | hevi | kuiva_elintarvike | liha_kala | leipa | kassa_palvelu |
piha_puutarha | rakennustarvike | tyokalut | sisustus_maalit | sahko_lvi |
varasto_nouto | verkkokauppa_toimitus | muu
```

**⚠ Boundary gap — hardcoded in the engine, flagged for a separate change.**
These values are retail configuration in principle, but today they live in
engine code in **three** places, none of them config:

- `src/RetailFeedback.Domain/Structuring/StructuringSchema.cs` — the canonical
  `HashSet` (the Domain project = the engine).
- `prompts/structuring-v0.txt` — the 14 values written inline into the Finnish
  structuring prompt.
- `src/RetailFeedback.Api/wwwroot/desk.html` — the Finnish department **label**
  map.

Extracting the enum (and the labels) to configuration so the engine carries no
retail taxonomy is a separate code change. This doc names the gap; it does not
perform the extraction.

## Alert keywords

Config, loaded and validated at startup: **`config/alert-keywords.json`**.
Case-insensitive substring match over the raw feedback text, Finnish stems, in
three categories: injury/safety, payment, legal-threat. The file also records
**deliberate exclusions** — structural-failure verbs (pettää, sortua, irrota,
antaa periksi, romahtaa) are *non-keywords on purpose*: they are the no-keyword
safety story's vocabulary, which must be detectable only by understanding
([ADR-0006](../decisions/0006-ai-in-exactly-two-places.md)). Safety-story corpus
texts are verified against this list, not a guess of it.

## Generator story types

Config: `Generator:Stories` in `tools/RetailFeedback.Generator/appsettings.json`.
Three planted-story archetypes (the demo's ground truth):

1. **dairy-freshness-worsening** — a repeating freshness/dairy signal across
   channels, worsening (a sequenced arc — [ADR-0011](../decisions/0011-sequence-preserving-arcs.md)).
2. **safety-no-keyword** — a safety complaint containing no alert keywords,
   detectable only by understanding.
3. **availability-slow-burn** — a slow-burn availability trend on one department.

Story definitions (department, theme keywords, sources, window, trend,
`minGroundedIds`, `expectAlert`) are config values; the authoritative corpus
composition guidance is in
[../../data/corpus/README.md](../../data/corpus/README.md).
