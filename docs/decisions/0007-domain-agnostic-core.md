# ADR-0007 — Domain-agnostic core, retail as configuration

- **Status:** Accepted (2026-07-04)
- **Deciders:** Mikko

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
- **Known boundary violation, flagged not fixed here:** the retail `department`
  taxonomy is currently **hardcoded in the engine** — `StructuringSchema.cs`
  (the Domain project), and re-hardcoded in `prompts/structuring-v0.txt` and the
  desk UI label map. Alert keywords and story types are already externalized to
  config. Extracting the `department` enum to config is a separate code change,
  recorded in [domain/retail.md](../domain/retail.md). This ADR does not perform
  that extraction — it names the boundary and the current gap.
- The schema fields `theme`, `severity`, `type`, `language` are domain-agnostic
  feedback dimensions; `department` is the one domain-specific field, and its
  value set is domain configuration.
