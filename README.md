# Feedback Intelligence

[![CI](https://github.com/MikkoNumminen/feedback-intelligence/actions/workflows/ci.yml/badge.svg)](https://github.com/MikkoNumminen/feedback-intelligence/actions/workflows/ci.yml)

Domain-agnostic feedback intelligence: ingest messy free-text feedback, surface
grounded situational signal. **First application: Finnish retail.**

The engine ingests feedback from multiple channels, runs a **deterministic alert
layer** in front of an LLM that structures messy input and reads themes/trends
out of free text, and gives management a grounded, live situational view —
alerts on top, themes and trends below, every claim clickable down to the exact
feedback items behind it. Retail is the first application, carried in a data-only
domain module; it is not the engine's identity. A second `domains/game/` module
proves a new domain is a new folder with zero core edits — switch with
`--Domain:Active=game` (see [docs/domains.md](docs/domains.md),
[ADR-0007](docs/decisions/0007-domain-agnostic-core.md),
[ADR-0012](docs/decisions/0012-pluggable-domain-modules.md)).

Built as a demonstrable work sample: .NET 8 backend, local LLM serving (Ollama),
100% synthetic data, live-runnable in an interview with a snapshot fallback so
[the shared link](https://red-ground-0bacf9c03.7.azurestaticapps.net/) never
shows a dead page.

## Why AI is only in two places

This design survived four rounds of "why does this need AI at all" scrutiny.
Everything that CAN be rule-coded IS rule-coded: alert keywords are a
deterministic substring scan (the active domain's `alert-keywords.json`) that runs first,
never sleeps and never hallucinates; theme grouping, counts and trend
direction are computed arithmetic; grounding is enforced by validation, not by
prompt-wording. The LLM remains only where free-form language genuinely cannot
be rule-coded:

1. **Structuring messy human input** — "asiakas sano et maitokaapis oli vanhoi
   purkkei" into `{department, theme, severity, type, language}`. At the desk
   this runs *before* saving, so the human accepts or corrects the
   interpretation — AI at the input side, killing the logging friction that
   makes desk feedback die at shift's end.
2. **Reading themes out of free text at scale** — the Finnish narrative in the
   management view. The model must cite the feedback IDs it drew on; a
   narrative whose citations fail validation is dropped to a deterministic
   fallback and the drop is logged. The view never shows an ungrounded claim.

The full four-round elimination — the ideas rejected and why — is recorded in
[ADR-0006](docs/decisions/0006-ai-in-exactly-two-places.md).

## Synthetic data as a GDPR decision

No scraped reviews, no real personal data — deliberately. Real customer
feedback contains personal data; a demo has no lawful basis to process it.
Instead: a hand-written, expert-calibrated core corpus (Finnish dialects,
typos, desk shorthand), multiplied offline by the local LLM, composed by a
seeded generator into datasets with *planted, machine-checkable stories*
(`data/corpus/README.md`). Same seed → same rehearsable demo; new seed → a
fresh-looking dataset with the same findable stories. The ground-truth file
names the exact item IDs each story consists of — so "the report found the
dairy story" is verified by ID matching, not by prose vibes.
([ADR-0005](docs/decisions/0005-synthetic-corpus-gdpr.md).)

## Model choices are measurements

- **Synthesis**: Poro-2-8B, chosen by a published 30-round blind test (26/30
  firsts against qwen3:8b and llama3.1:8b for Finnish naturalness).
- **Structuring**: also Poro-2-8B — decided on synthesis-priority grounds
  (one model, simpler pipeline) with a **recorded tradeoff**: Poro's JSON
  discipline on messy Finnish is unmeasured. The mitigation is architectural:
  a mandatory salvage layer (fence-stripping → schema validation → safe
  normalization → one re-prompt → `structure_failed` with raw text preserved,
  unit-tested against measured failure shapes), and correction telemetry from
  the desk UI (model-assigned vs human-corrected per field) as the ongoing
  quality measure. The model stays swappable by config if the data says so.
  ([ADR-0003](docs/decisions/0003-poro-for-both-roles.md),
  [ADR-0004](docs/decisions/0004-salvage-layer-mandatory.md).)

## The provider abstraction, honestly

No code calls Ollama directly: everything goes through
`Microsoft.Extensions.AI.IChatClient` behind `ILlmClientFactory`, with
structuring and synthesis as independently configurable models. Switching to
Azure OpenAI is a config change *plus an eval run* — prompts are not perfectly
portable across models, quality must be re-measured, and moving from local to
hosted is a data-residency decision that belongs to the customer.
([ADR-0002](docs/decisions/0002-llm-behind-one-abstraction.md).)

## Prompt injection, honestly

Every feedback item is hostile-by-default free text fed to a model, so the LLM
boundary is hardened in depth: untrusted text is fenced and neutralized before
any prompt splice; injection *symptoms* raise a `needs_review` flag (the item is
kept, never dropped); the synthesis narrative is bounded to grounded
description; and a committed red-team fixture turns a reopened hole into a red
build. This is defense-in-depth and measurable coverage — **not** a proof of
safety: prompt injection is unsolved, and the deterministic layer stays the
trust anchor.
([ADR-0021](docs/decisions/0021-prompt-injection-defense-in-depth.md).)

## Running it

```
dotnet test                                   # unit tests, no LLM needed
docker compose up -d ollama                   # local Ollama (isolated volume)
dotnet run --project src/FeedbackIntelligence.Api   # API + UIs on localhost
```

- `/` — management view (Finnish; snapshot-first render, then the live report)
- `/desk.html` — desk entry: type one sentence, accept/correct, save
- `POST /feedback` — the one ingest endpoint all four channels share

Corpus pipeline: `tools/FeedbackIntelligence.Generator` (`variants` = offline LLM
multiplication; `generate --seed N` = deterministic composition, never calls
the LLM; `verify` = mechanized acceptance against ground truth). Structuring
eval harness: `tools/FeedbackIntelligence.StructuringEval`.

## Live demo

- [Management view](https://red-ground-0bacf9c03.7.azurestaticapps.net/) —
  renders the committed seed-42 snapshot instantly (badge: *Tallennettu
  tilannekuva*), then upgrades in place to the live report whenever the
  operator backend is reachable.
- [Desk entry](https://red-ground-0bacf9c03.7.azurestaticapps.net/desk.html) —
  live-only: interpretation needs the backend up; there is no snapshot
  stand-in for it.

The hosting is deliberately minimal: a static bundle on Azure Static Web Apps
Free, whose pages call a same-origin `/api` proxy (a Free-tier managed
function) that forwards server-side to the operator's local API + Ollama
through a Tailscale Funnel — no cloud inference, $0 hosting, and the browser
never makes a cross-origin or local-network request (Chrome's 2026 Local
Network Access permission made that mandatory —
[ADR-0025](docs/decisions/0025-same-origin-api-proxy.md); deploy shape:
[ADR-0016](docs/decisions/0016-zero-cost-static-web-apps-deploy.md),
[ADR-0023](docs/decisions/0023-deploy-hardening-snapshot-and-pna.md)).

## Design & docs

- Engine design and the two-layer pipeline: [docs/architecture.md](docs/architecture.md)
- The decisions behind it (numbered ADRs): [docs/decisions/](docs/decisions/)
- Feedback schema + the domain boundary: [docs/schema.md](docs/schema.md),
  [docs/domain/retail.md](docs/domain/retail.md)
- Conventions for agents working here: [AGENTS.md](AGENTS.md)

## Honest status

Built end-to-end and demoed on the real corpus: the hand-written core
(27 texts, evidential) expanded and composed into the seed-42 dataset, its
analysis captured as a provenance-verified snapshot committed at
`deploy/snapshot/` and always bundled by CI
([ADR-0023](docs/decisions/0023-deploy-hardening-snapshot-and-pna.md)). The
live loop — desk entry → structuring → re-synthesis — is verified end to end.
Placeholder artifacts still exist but are labeled non-evidential and never
appear as demo evidence (`docs/mock-data-register.md`). Remaining owner tasks
live in `docs/TODO.md`; PR history with review findings: `docs/prs/`.
