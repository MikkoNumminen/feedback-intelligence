# AGENTS.md

Conventions for AI agents working in this repo. The full project spec, phase
plan, and environment notes live in [CLAUDE.md](CLAUDE.md) — read that first.

## Hard rules

- **No Anthropic/AI attribution on commits or PRs.** Never add a
  `Co-Authored-By: Claude` (or any AI) trailer, or a "Generated with Claude
  Code" footer. Adopted from mikkonumminen.dev; this repo's history was
  rewritten on 2026-07-03 to strip earlier trailers.
- **Never modify or restart the `mikkonumminendev` Docker stack.** It is a
  live, recruiter-facing deployment. The GPU is shared with it: announce
  before any LLM/GPU use so the owner can shut the stack down first.
- **Never invent Finnish corpus texts** for evidential use. Eval and demo
  corpora are hand-written by the project owner — ask for them (see CLAUDE.md,
  Phase 1). Clearly-marked placeholder texts for pipeline exercise are the
  only exception, and their results are never evidence.
- **Keep AI-first docs current.** Changes to decisions, rules, environment
  facts, schema, or phase state update CLAUDE.md / AGENTS.md in the same
  change. On every future PR, explicitly consider whether AI-first docs need
  an update and state the outcome in the description.
