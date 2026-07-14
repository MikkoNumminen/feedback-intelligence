# Feedback schema

The schema is the structuring model's output contract — what a piece of feedback
becomes after the LLM structures it. It is the **shape** of a feedback record;
the shape is domain-neutral, and the *values* of the domain-shaped fields come
from the active domain module.

**Single source of truth for the shape:** `src/FeedbackIntelligence.Core/Structuring/StructuringSchema.cs`
holds only the five field **names**. The enum **values** (category / severity /
type) come from the active domain module (`domains/<name>/domain.json`, loaded
via `IActiveDomain` — see [domains.md](domains.md),
[ADR-0012](decisions/0012-pluggable-domain-modules.md)). The eval runner and the
ingest pipeline validate against the active domain's taxonomy, and `GET /schema`
serves the active domain's enum sets **and display labels** to UIs so no frontend
keeps its own copy.

## Fields (schema v0)

Five **required** fields + one **optional** (`sentiment`, ADR-0031).

| Field | Kind | Notes |
|---|---|---|
| `category` | **domain enum** | The domain-shaped field. Values + display label come from the active domain module (`osasto` for retail, `area` for game). |
| `theme` | free text | Short noun phrase in the feedback's own language; deliberately not an enum. The prompt asks for base form / lowercase / plain spaces (ADR-0028), but any non-empty string is schema-valid. |
| `severity` | enum | Domain-overridable; defaults to `low \| medium \| high \| critical` ([`CoreDefaults`](../src/FeedbackIntelligence.Core/Domain/CoreDefaults.cs)). |
| `type` | enum | Domain-overridable; defaults to `complaint \| praise \| suggestion \| question \| other`. |
| `language` | string | Each item's **detected** language, kept as-is, never translated. Distinct from the *domain's* output language (`DomainDescriptor.Language` / [domains.md](domains.md)), which is the presentation language of the whole domain. |
| `sentiment` | **optional** enum | Model-authored polarity (positive/negative/neutral), ADR-0031. Absence is never a violation; a present-but-invalid value is salvaged to null at ingest. **Poro-2-8B does not emit it today** — the report falls back to the deterministic type→sentiment map (ADR-0030). A model swap activates it with no code change (`StructuringSchema.OptionalFields`). |

There is deliberately **no alert field**. Alert decisions belong to the
deterministic layer and the separate analysis pass, never to the structuring
model ([ADR-0006](decisions/0006-ai-in-exactly-two-places.md),
[ADR-0009](decisions/0009-grounding-is-structural.md)).

## Sentiment (two sources, one seam)

The sentiment indicator has two possible sources behind a single seam
(`ReportService.SentimentOf` = `structure.Sentiment ?? domain.SentimentOf(type)`):

1. **Model-authored** — the optional `sentiment` field above (ADR-0031). Preferred
   when present. **Poro-2-8B does not emit it today** (measured in the ADR-0031 live
   check: 0 of 71), so in practice source 2 is active.
2. **Deterministic** — derived from `type` via the domain's `typeSentiment` map
   (retail: complaint→negative, praise→positive, suggestion/question/other→neutral),
   no model or prompt dependency
   ([ADR-0030](decisions/0030-sentiment-indicator-deterministic-from-type.md)).

`GET /schema` serves `sentiments` + `sentimentLabels` + the `typeSentiment` map
alongside the enum sets; the report attaches a per-item `sentiment`, a per-theme
`sentimentCounts`, and a whole-window `sentimentCounts`. Swapping the structuring
model to one that emits sentiment activates source 1 automatically — no code change.

`category` is a **fixed per-domain enum** rather than free text on purpose: free
text returns the same category as "maito" / "maitotuotteet" / "kylmä", which can
neither be scored for schema adherence nor grouped for trends.

## Where the domain taxonomy is configured

The schema *shape* above is engine-level. The `category` (and, when overridden,
`severity`/`type`) **value sets** are domain taxonomy and live in the active
domain module's `domain.json` — see [domains.md](domains.md) for the authoring
contract and [domain/retail.md](domain/retail.md) for the retail values. The
engine hardcodes none of them; the boundary is clean in code as of
[ADR-0012](decisions/0012-pluggable-domain-modules.md) (which closed the gap
[ADR-0007](decisions/0007-domain-agnostic-core.md) had flagged).

## Validation and salvage

The structuring model's raw output is validated against the active domain's
taxonomy and salvaged where safe before storage; an output that cannot be made
schema-valid is stored `structure_failed` with the raw text preserved. See
[ADR-0004](decisions/0004-salvage-layer-mandatory.md).
