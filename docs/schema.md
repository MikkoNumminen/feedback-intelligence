# Feedback schema

The schema is the structuring model's output contract — what a piece of feedback
becomes after the LLM structures it. It is the **shape** of a feedback record;
it is domain-agnostic except for one field whose *values* are domain
configuration.

**Single source of truth:** `src/RetailFeedback.Domain/Structuring/StructuringSchema.cs`.
The eval runner and the ingest pipeline both validate against it, and
`GET /schema` serves the enum sets to UIs so no frontend keeps its own copy.

## Fields (schema v0)

Exactly five fields — no more, no fewer.

| Field | Kind | Notes |
|---|---|---|
| `department` | **domain enum** | The one domain-specific field. Values are retail configuration — see [domain/retail.md](domain/retail.md). |
| `theme` | free text | Short Finnish noun phrase; deliberately not an enum. |
| `severity` | enum | `low \| medium \| high \| critical` — a generic feedback dimension. |
| `type` | enum | `complaint \| praise \| suggestion \| question \| other` — a generic feedback dimension. |
| `language` | string | Kept **as detected**, never translated. |

There is deliberately **no alert field**. Alert decisions belong to the
deterministic layer and the separate analysis pass, never to the structuring
model ([ADR-0006](decisions/0006-ai-in-exactly-two-places.md),
[ADR-0009](decisions/0009-grounding-is-structural.md)).

`department` was made a **fixed enum** rather than free text on purpose: free
text returns the same department as "maito" / "maitotuotteet" / "kylmä", which
can neither be scored for schema adherence nor grouped for trends.

## Where the domain taxonomy is configured

The schema *shape* above is engine-level. The `department` **value set** is
domain taxonomy and belongs in the retail domain module —
[domain/retail.md](domain/retail.md). Note the boundary is not yet clean in
code: the `department` enum is currently defined in `StructuringSchema.cs` (the
engine) rather than in config. That gap is recorded and flagged in
[domain/retail.md](domain/retail.md) and
[ADR-0007](decisions/0007-domain-agnostic-core.md); it is a separate code
change, not part of this documentation.

## Validation and salvage

The structuring model's raw output is validated against this schema and salvaged
where safe before storage; an output that cannot be made schema-valid is stored
`structure_failed` with the raw text preserved. See
[ADR-0004](decisions/0004-salvage-layer-mandatory.md).
