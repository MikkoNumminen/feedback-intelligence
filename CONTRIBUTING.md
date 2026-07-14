# Contributing

Thanks for looking. This is a personal work-sample / demo project, but it is
built to a real engineering bar — and the bar is written down. **[AGENTS.md](AGENTS.md)
is the canonical spec**: what this is, the hard invariants, the commands, the
docs map. Read it first; everything below is the short version.

## Build and test

The unit tests are hermetic — no LLM, no GPU, no network, no secrets:

```
dotnet test        # builds the solution and runs every test project
```

`FeedbackIntelligence.Api.Tests`, `FeedbackIntelligence.Llm.Tests`, and
`FeedbackIntelligence.Generator.Tests` all run under that one command. A
domain-config or prompt change can break a test in a project you did not touch,
so run the whole solution, not a single project. CI runs the same build + tests
on every PR ([ADR-0013](docs/decisions/0013-ci-on-github-actions.md)); keep it
green.

Running the app or the corpus generator locally uses a **shared GPU** with a
live deployment — read the invariants below before you start Ollama.

## How changes land

- **Branch off `master`, open a PR.** No direct commits to `master`.
- **Small, single-concern commits**, conventional-commit style
  (`feat(...)`, `fix(...)`, `chore(...)`, `docs(...)`).
- **A new decision gets an ADR** under [docs/decisions/](docs/decisions/) — the
  *why* lives in a numbered record, not in a commit message.
- **AI-first docs update in the same change** that alters a rule, schema, or
  decision. Each PR states whether docs needed updating.

## Non-negotiable invariants

These are load-bearing — a PR that breaks one does not land. Full text in
[AGENTS.md](AGENTS.md):

- **Grounding is structural.** Every claim in the management view traces to
  specific feedback IDs; an unsourceable claim is dropped, never shown.
- **Deterministic layer first, LLM behind it.** The rule layer runs before and
  independent of the model; the model may *add*, never *remove* or replace a
  deterministic result.
- **Config over hardcoding** — model, thresholds, keyword lists, and windows are
  validated config, not literals in engine code.
- **No AI attribution** on commits or PRs — no `Co-Authored-By` trailer, no
  "Generated with …" footer.
- **Never touch the `mikkonumminendev` stack, and announce before any LLM/GPU
  use** — the GPU is shared with a live, recruiter-facing deployment; never
  assume it is free.
- **Never invent Finnish corpus texts for evidential use** — the eval and demo
  corpora are hand-written by the owner.

## Reporting security issues

See [SECURITY.md](SECURITY.md). Do not put working exploit payloads in public
issues.

## Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md).
