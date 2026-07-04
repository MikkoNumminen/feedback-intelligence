# ADR-0013 — Continuous integration on GitHub Actions

- **Status:** Accepted (2026-07-05)
- **Deciders:** Mikko

## Context

Every change in this repo flows through a pull request ([AGENTS.md](../../AGENTS.md)),
and merges are gated by the never-merge-without-approval rule. But nothing
**automatically** verified a PR: "81 tests green" depended on a human (or agent)
remembering to run `dotnet test` locally. For a repo whose whole workflow is
"an agent opens a PR", the missing automated build+test on that PR was the single
largest gap in an otherwise agent-friendly setup — an agent could push a red
branch and nothing would flag it before review.

The unit tests are already **hermetic**: they need no LLM, no GPU, no network,
and no secrets (`dotnet test # no LLM needed`). That makes them a clean fit for
CI — the shared Ollama/RAG stack (which must never be touched, and whose GPU use
must be announced) is never involved.

## Decision

Add a **GitHub Actions** workflow (`.github/workflows/ci.yml`) that runs on every
push to `master` and every PR targeting `master`:

`dotnet restore` → `dotnet build --configuration Release` → `dotnet test --configuration Release`.

- **Runner: `ubuntu-latest`**, not `windows-latest`. The code is deliberately
  culture-invariant and cross-platform (InvariantCulture for machine formats,
  `Path.Combine`, `SQLitePCLRaw.bundle_e_sqlite3`); running CI on Linux both
  follows the conventional, cheaper default *and* continuously proves that
  portability. (Verified: the full Release build + all 81 tests pass locally
  before this workflow landed.)
- **Release configuration** so a Debug/Release divergence would be caught, not
  hidden.
- **Hermetic:** no secrets, no Ollama, no GPU — CI can never contend for the
  shared RAG's GPU, so the announce-before-GPU-use rule is untouched.
- **Least privilege** (`permissions: contents: read`) and `concurrency` to cancel
  superseded runs on the same ref.

## Consequences

- Every PR gets an automatic green/red build+test signal before review; a red
  branch is visible immediately instead of at merge time.
- CI is **advisory by default** — it does not itself gate merges. Merging stays
  governed by the never-merge-without-approval rule. Making CI a **required
  status check** (branch protection on `master`) is a complementary repo-settings
  change the owner can enable; it is recommended but intentionally left to the
  owner, as it is governance, not code.
- Cost is minimal: offline unit tests complete in well under a minute of runner
  time; concurrency-cancellation avoids redundant runs.
- The workflow is the one place that must track the test entry point — if the
  solution or test layout changes, `ci.yml` updates in the same change (the
  AI-first-docs-current discipline applies to it too).
