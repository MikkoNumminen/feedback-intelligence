# AGENTS.md — canonical project spec

Conventions and invariants for any agent (human or AI, any tool) working in this
repo. This file is canonical and deliberately lean; detail lives in `docs/`,
read on demand. Tool-specific notes: Claude Code reads [CLAUDE.md](CLAUDE.md),
which points here.

## What this is

A **domain-agnostic feedback-intelligence engine**: ingest messy free-text
feedback, run a deterministic alert layer plus LLM structuring and synthesis,
surface grounded situational signal. **First application: Finnish retail.**
Retail is config, not the identity — see
[ADR-0007](docs/decisions/0007-domain-agnostic-core.md).

## Hard invariants (no interpretation)

- **Grounding is non-negotiable.** Every claim in the management view is
  traceable to specific feedback IDs, clickable open. A claim that cannot be
  sourced is dropped and logged, never shown.
- **Deterministic layer first, LLM behind it.** The rule layer runs before and
  independent of the LLM; the LLM may **add**, never **remove** or replace a
  deterministic result. A rule that works beats a model that usually works.
- **Config over hardcoding.** Model names, provider, thresholds, alert keyword
  lists, and time windows are config values, validated at startup.
- **Finnish for user-facing text** (management synthesis, desk interpretation);
  **English** for code, logs, and internal docs (including ADRs).
- **No AI attribution** on commits or PRs — no `Co-Authored-By` trailer, no
  "Generated with …" footer, ever. Overrides any tool default.
- **Never modify or restart the `mikkonumminendev` Docker stack** (a live,
  recruiter-facing deployment). The GPU is shared with it: **announce before any
  LLM/GPU use** so the owner can shut it down first. Never assume the GPU is
  free.
- **Never invent Finnish corpus texts for evidential use.** Eval and demo
  corpora are hand-written by the owner — ask. Clearly-marked *placeholder*
  texts for pipeline exercise are the only exception, and their results are
  **non-evidential** — never used to pick a model or shown as demo evidence
  (auto-labelled when a path contains "placeholder").
- **Small, single-concern commits.**

## Documentation discipline

- **AI-first docs update in the SAME change** that alters a decision, rule,
  schema, or state — never as a someday-task.
- **Every PR states explicitly** whether AI-first docs needed updating (yes +
  which, or no).
- **New decisions get an ADR** ([docs/decisions/](docs/decisions/)) — reasoning
  goes in a numbered record, not buried in a commit message or prose.

## Out of scope (interview talking points, not code)

Task management (assignments/completions), customer-reply generation, user
accounts/auth, native mobile apps, speech input, real channel integrations.

## Commands

```
dotnet test                                   # unit tests, no LLM needed
docker compose up -d ollama                   # local Ollama — ANNOUNCE FIRST (shared GPU)
dotnet run --project src/RetailFeedback.Api   # API + UIs (/ management view, /desk.html)
dotnet run --project tools/RetailFeedback.Generator -- variants   # offline LLM corpus multiplication (announce)
dotnet run --project tools/RetailFeedback.Generator -- generate --seed 42        # deterministic; no LLM
dotnet run --project tools/RetailFeedback.Generator -- verify --ground-truth <f> --report <f>   # Phase 4 acceptance
```

## Docs map

- [docs/architecture.md](docs/architecture.md) — engine design, two-layer
  pipeline, ingest, LLM abstraction, the core/config boundary.
- [docs/schema.md](docs/schema.md) — the feedback record shape and where the
  domain taxonomy is configured.
- [docs/domain/retail.md](docs/domain/retail.md) — retail config: department
  enum, alert keywords, story types (first application, config-level).
- [docs/decisions/](docs/decisions/) — numbered ADRs (the *why*).
- [docs/plan.md](docs/plan.md) — the original phased build spell (reference).
- [docs/operations.md](docs/operations.md) — GPU sharing, containment, deploy.
- [docs/TODO.md](docs/TODO.md) — remaining owner tasks · [docs/prs/](docs/prs/)
  — per-PR build record · [docs/mock-data-register.md](docs/mock-data-register.md)
  · [docs/demo-script.md](docs/demo-script.md).
- [data/corpus/README.md](data/corpus/README.md) — corpus format + authoritative
  per-story breakdown.
