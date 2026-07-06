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
| [0013](0013-ci-on-github-actions.md) | Continuous integration on GitHub Actions | Accepted |
| [0014](0014-domain-output-language.md) | Output language is a domain property (English default, retail Finnish) | Accepted |
| [0015](0015-poro-real-corpus-tuning.md) | Poro tuning after the first real-corpus run (keep the model, fix the usage) | Accepted |
| [0016](0016-zero-cost-static-web-apps-deploy.md) | Zero-cost Azure Static Web Apps (Free SKU) deploy | Accepted |
| [0017](0017-trend-significance-gate.md) | Trend direction requires statistical significance (no hallucinated trends) | Accepted |
| [0018](0018-llm-call-determinism.md) | LLM calls must be deterministic + prompt-byte-stable (CRLF flipped the safety alert) | Accepted |
| [0019](0019-story-variants-originals-only.md) | Story items ship as originals only (the ×2 variant fallback, taken) | Accepted |
| [0020](0020-alert-screen-escalation-intent.md) | Alert screen also flags intent to escalate to authorities/legal action | Accepted |
| [0021](0021-prompt-injection-defense-in-depth.md) | Prompt-injection defense-in-depth at the LLM boundary (A1 done, A2–A4 staged) | Accepted |
