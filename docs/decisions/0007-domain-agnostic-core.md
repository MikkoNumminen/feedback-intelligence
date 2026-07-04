# ADR-0007 — Domain-agnostic core, retail as configuration

- **Status:** Accepted (2026-07-04); the flagged boundary gap is now **realized by
  [ADR-0012](0012-pluggable-domain-modules.md)** (pluggable domain modules).
- **Deciders:** Mikko

> **Update (2026-07-04):** This ADR named the principle and honestly flagged that
> the taxonomy was still hardcoded in the engine. [ADR-0012](0012-pluggable-domain-modules.md)
> performs the extraction: the taxonomy, alert lexicon, stories, and domain-voiced
> prompts now live in data-only modules under `domains/<name>/`, selected by
> `Domain:Active`. The "known boundary violation" below is closed; the schema
> field it calls `department` has since been renamed to the neutral `category`.

## Context

The system is, at its core, a domain-agnostic feedback-analysis engine: messy
free-text feedback in → a deterministic alert layer plus LLM structuring and
synthesis → grounded situational signal out. Retail is the **first application**
of that engine, not its identity. The founding brief was written retail-first,
so retail specifics accreted into the naming and, in one case, into the engine
itself.

## Decision

Treat the engine as **domain-agnostic core** and the retail specifics as
**configuration / a domain module**:

- The **engine** owns: the ingest pipeline (one endpoint, source-valued
  channels), the deterministic-then-LLM two-layer design, the LLM abstraction,
  the salvage layer, the grounded analysis, and the feedback schema *shape*
  (see [../schema.md](../schema.md)).
- The **retail domain** owns its taxonomy values: the `department` enum, the
  alert-keyword list, and the generator's story types (see
  [domain/retail.md](../domain/retail.md)).
- Documentation reflects the boundary: core docs describe the engine; retail
  lives in a clearly-labelled domain doc as "first application, config-level, not
  core."

## Consequences

- The README's first line states the framing plainly: a domain-agnostic feedback
  intelligence engine, with Finnish retail as the first application.
- **Known boundary violation, since CLOSED by [ADR-0012](0012-pluggable-domain-modules.md):**
  at the time of this ADR the retail `department` taxonomy was **hardcoded in the
  engine** — `StructuringSchema.cs`, re-hardcoded in `prompts/structuring-v0.txt`
  and the desk UI label map. ADR-0012 extracted all of it into
  `domains/retail/domain.json` (with the field renamed `department` → `category`);
  the neutral structuring prompt is now templated from the active domain and the
  desk renders labels from `/schema`. The engine carries no retail taxonomy.
- The schema fields `theme`, `severity`, `type`, `language` are domain-agnostic
  feedback dimensions; `department` is the one domain-specific field, and its
  value set is domain configuration.
