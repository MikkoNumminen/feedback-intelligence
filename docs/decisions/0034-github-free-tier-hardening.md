# ADR-0034 — GitHub free-tier hardening for the public repo

- **Status:** Accepted (2026-07-14)
- **Deciders:** Mikko
- **Follows:** [ADR-0013](0013-ci-on-github-actions.md) (CI on GitHub Actions)

## Context

The repository is public and doubles as a portfolio artifact — a reviewer or
recruiter reads it as a work sample, not just runs it. GitHub's free tier for
**public** repos is unusually generous (much of what private repos pay for is
free here), and several of those features were already in place: hermetic
build/test CI (ADR-0013), secret scanning + push protection, a protected
`master`, `SECURITY.md`, and `LICENSE`. What was missing were the free features
that (a) produce the kind of *verifiable* evidence the whole project is built on
and (b) complete GitHub's "community standards" checklist for a public example.

## Decision

Adopt four free-for-public-repo features. No engine, model, or prompt change —
this is repo infrastructure and docs.

1. **CodeQL code scanning** (`.github/workflows/codeql.yml`) — static analysis
   of the C# sources on push, PR, and a weekly schedule, with the
   `security-and-quality` query suite. It runs in **build-mode `none`** (CodeQL
   analyzes source without compiling), so it needs no .NET toolchain and, like
   the build CI, is fully hermetic — it never touches the shared Ollama/RAG
   stack. A status badge sits beside the CI badge in the README. Static analysis
   as verifiable, always-on evidence is on-brand with the project's
   "measured, not asserted" posture.
2. **Dependabot** (`.github/dependabot.yml`) — weekly security alerts and
   version-update PRs for the two ecosystems the repo actually has: NuGet (the
   solution) and github-actions (the pinned workflow action versions). Updates
   are **grouped one PR per ecosystem** to keep review noise low on a solo repo,
   and carry the repo's conventional-commit prefixes.
3. **Community-health files** — `CONTRIBUTING.md` (build/test, the PR + ADR
   discipline, and the load-bearing invariants, all pointing at the canonical
   `AGENTS.md`) and `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1, enforcement
   routed to the same private channel as `SECURITY.md`). With the pre-existing
   `SECURITY.md` + `LICENSE`, this completes GitHub's community-standards set.
4. **Codespaces devcontainer** (`.devcontainer/devcontainer.json`, .NET 8 image
   + an "Open in Codespaces" button in the README) — a reviewer can open the
   repo in-browser and run `dotnet test` with no local .NET install. Personal
   accounts get 120 free core-hours/month.

Repo **topics** are also set (via the API, not a committed file) for
discoverability and to shape how the repo reads as a link.

## Consequences

- Every PR now gets both a build/test gate and a static-analysis gate, for free,
  without adding any non-hermetic dependency.
- Dependency drift and known CVEs surface as grouped weekly PRs rather than going
  unnoticed; the `github-actions` ecosystem keeps the pinned action SHAs current.
- The repo satisfies GitHub's "community standards" checklist — a small but real
  credibility signal for a public product example.
- A reviewer with only a browser can reproduce the test suite.
- Scope is deliberately bounded: GHCR, GitHub Pages, Releases, and artifact
  attestations were considered and **skipped** — the live demo already deploys
  via Azure Static Web Apps + the tunnel (ADR-0016/0023/0025), so those add
  surface without adding evidence today. `CODEOWNERS` was skipped (solo repo).
