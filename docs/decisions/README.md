# Architecture Decision Records

Numbered records of the load-bearing decisions behind this project, so the
reasoning survives context resets — a future session (or another agent)
inherits the *why*, not just the state. Each record is immutable once
Accepted; a reversal is a new ADR that supersedes the old one, never an edit.

Format: **Context / Decision / Consequences / Status**. Most records here are
*retroactive* — the decisions were made and recorded in prose during the build;
these ADRs relocate that reasoning into durable records.

| ADR | Decision | Status |
|----|----------|--------|
| [0001](0001-dotnet-8-ecosystem-signal.md) | C# / .NET 8 (LTS) for ecosystem signal | Accepted |
| [0002](0002-llm-behind-one-abstraction.md) | LLM behind one abstraction; provider/model as config | Accepted |
| [0003](0003-poro-for-both-roles.md) | Poro-2-8B for both structuring and synthesis | Accepted |
| [0004](0004-salvage-layer-mandatory.md) | Salvage layer is a mandatory production component | Accepted |
| [0005](0005-synthetic-corpus-gdpr.md) | Synthetic, expert-calibrated corpus (GDPR) | Accepted |
| [0006](0006-ai-in-exactly-two-places.md) | AI in exactly two places (four-round elimination) | Accepted |
| [0007](0007-domain-agnostic-core.md) | Domain-agnostic core, retail as config | Accepted (realized by 0012) |
| [0008](0008-sqlite-over-postgres.md) | SQLite over PostgreSQL for storage | Accepted |
| [0009](0009-grounding-is-structural.md) | Grounding is structural, not prompt-wording | Accepted |
| [0010](0010-verify-gate-tiering.md) | Acceptance gate: hard gates vs. trend warning tier | Accepted |
| [0011](0011-sequence-preserving-arcs.md) | Sequence-preserving story arcs in the generator | Accepted |
| [0012](0012-pluggable-domain-modules.md) | Domain-neutral core with pluggable domain modules | Accepted |
