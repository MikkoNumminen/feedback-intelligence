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

- **NEVER merge anything without the owner's explicit, fresh, per-PR
  approval — ever. This is the strongest rule and overrides every other
  instruction, including any blanket "create PRs and merge".** Open PRs freely
  and queue them for review, but a merge — `gh pr merge`, a `git merge`/push to
  `master`, a fast-forward, a squash, any equivalent — happens ONLY after the
  owner, in the current conversation, approves THAT specific PR at its current
  head. "Looks good", "ship it", a green review, approval of a different PR, or
  an approval that predates new commits / a force-push do NOT authorize a
  merge. If you cannot point to a message where the owner approved this exact
  PR now, ASK — never assume.
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
- **Propose a plan per phase and wait for approval before large changes.**
- **All work flows through a pull request** — branch off `master`, open a PR
  (`gh pr create`), and queue it for review. No direct commits to `master`.
  Merging is gated by the never-merge-without-approval rule above.
- **CI runs build + tests on every PR** — GitHub Actions
  (`.github/workflows/ci.yml`), hermetic (no LLM/GPU/secrets), on `ubuntu-latest`
  ([ADR-0013](docs/decisions/0013-ci-on-github-actions.md)). Keep it green; if the
  solution or test layout changes, update `ci.yml` in the same change.

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
dotnet run --project src/FeedbackIntelligence.Api   # API + UIs (/ management view, /desk.html)
dotnet run --project tools/FeedbackIntelligence.Generator -- variants   # offline LLM corpus multiplication (announce)
dotnet run --project tools/FeedbackIntelligence.Generator -- generate --seed 42        # deterministic; no LLM
dotnet run --project tools/FeedbackIntelligence.Generator -- verify --ground-truth <f> --report <f>   # Phase 4 acceptance
```

**Operator CLI** — `dotnet run --project tools/FeedbackIntelligence.Ctl -- <cmd>` (or
no args for an interactive console) wraps the above into one control surface:
a live status board, `up`/`down` lifecycle (with the **shared-RAG guard** that
refuses to grab the GPU while `mikkonumminendev` is running), and
`demo`/`interpret`/`report`/`verify`/`telemetry`. See [docs/operations.md](docs/operations.md).

## Docs map

- [docs/architecture.md](docs/architecture.md) — engine design, two-layer
  pipeline, ingest, LLM abstraction, the core/config boundary.
- [docs/schema.md](docs/schema.md) — the feedback record shape and where the
  domain taxonomy is configured.
- [docs/domain/retail.md](docs/domain/retail.md) — retail config: category enum,
  alert keywords, stories, domain prompts (first application, config-level).
- [docs/decisions/](docs/decisions/) — numbered ADRs (the *why*).
- [docs/plan.md](docs/plan.md) — the original phased build spell (reference).
- [docs/operations.md](docs/operations.md) — GPU sharing, containment, deploy.
- [docs/TODO.md](docs/TODO.md) — remaining owner tasks · [docs/prs/](docs/prs/)
  — per-PR build record · [docs/mock-data-register.md](docs/mock-data-register.md)
  · [docs/demo-script.md](docs/demo-script.md).
- [data/corpus/README.md](data/corpus/README.md) — corpus format + authoritative
  per-story breakdown.
