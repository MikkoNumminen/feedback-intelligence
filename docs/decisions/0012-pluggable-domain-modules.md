# ADR-0012 — Domain-neutral core with pluggable domain modules

- **Status:** Accepted (2026-07-04)
- **Deciders:** Mikko
- **Realizes:** [ADR-0007](0007-domain-agnostic-core.md) (which named the boundary and flagged the gap)

## Context

[ADR-0007](0007-domain-agnostic-core.md) established the principle — the engine
is domain-agnostic, retail is the first application — but flagged a live
violation: the retail taxonomy was hardcoded in the engine
(`StructuringSchema.cs`), re-hardcoded in the structuring prompt, and again in
the desk UI label map. The principle was documented; the code still married the
engine to retail.

The goal is a reusable engine whose purpose is condensing any large body of
free-text feedback into grounded situational output, for any domain that needs
it (retail, game-studio player feedback, public-sector, SaaS support). Applying
the engine to a new domain must mean **adding a domain module, never editing the
core**.

## Decision

Split the system into a **domain-neutral core** and **data-only domain modules**.

- **The core owns mechanism, never values.** The schema *shape* (the five field
  names `category`, `theme`, `severity`, `type`, `language`), the LLM
  abstraction, the salvage/validation layer, the deterministic-alert *matcher*,
  the ingest pipeline, the analysis engine, and the seeded generator. None of
  these reference a category, a keyword, a story, or a label.
- **A domain module is a folder of data** under `domains/<name>/`:
  - `domain.json` — taxonomy (`categories`, optional `severities`/`types` with
    display labels), the `categoryFieldLabel`, and a `prompts` map to the
    domain's voiced prompts.
  - `alert-keywords.json` — the deterministic alert lexicon.
  - `stories.json` — the generator's planted-story definitions.
  - `prompts/` — the domain-voiced synthesis and alert-nomination prompts
    (persona, language). The neutral structuring prompt stays in the core and is
    templated with the active domain's taxonomy at load time.
- **The core selects one active domain by configuration.** `Domain:Active`
  (default `retail`), switchable from the CLI: `--Domain:Active=game`. It is
  loaded once and validated at startup; an unknown or malformed domain fails the
  boot. `IActiveDomain` exposes the loaded descriptor and the resolved data-file
  and prompt paths; every mechanism reads its taxonomy from there.

The full authoring contract is in [../domains.md](../domains.md).

## Consequences

- **The ADR-0007 gap is closed.** No category value, keyword, story, or label
  lives in core code. `domains/retail/` is one module; `domains/game/` is a
  second, added as a folder with zero core edits — it is the proof, not just an
  example.
- **Switching is a config flag, not a build variant.** `--Domain:Active=game`
  reskins `/schema`, the desk UI labels, the alert lexicon, the stories, and the
  synthesis voice. Domain modules are copied beside the binary at build, so a new
  module is available after a rebuild (or immediately when running from a
  checkout whose working directory holds `domains/`).
- **Behavior preservation was proven, not asserted.** The retail seed-42 corpus
  is byte-identical before and after the extraction (SHA-256 reproduced from the
  pre-refactor commit and the refactored tree); all 81 tests stay green.
- **Cost:** a domain now spans several small files instead of one appsettings
  block, and the generator can only compose a domain whose variants pool carries
  matching story tags (a game corpus is required before `generate` runs with
  `Domain:Active=game`). The `/schema`, desk, ingest, and report paths need no
  corpus and switch freely.
- **`severities` and `types` are domain-overridable but default** (via
  `CoreDefaults`) to the universal low/medium/high/critical and
  complaint/praise/suggestion/question/other, so most domains only author
  `categories`.
