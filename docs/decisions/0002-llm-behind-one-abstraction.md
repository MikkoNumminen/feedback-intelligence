# ADR-0002 — LLM behind one abstraction; provider/model as config

- **Status:** Accepted (2026-07-03)
- **Deciders:** Mikko

## Context

The demo must sell a production story: that moving off local Ollama to a hosted
model is a controlled, cheap change rather than a rewrite. That story is only
credible if no application code is coupled to the LLM provider.

## Decision

No code calls Ollama directly. Everything goes through a single interface —
Microsoft.Extensions.AI's **`IChatClient`**, with **OllamaSharp** as the
provider (the deprecated `Microsoft.Extensions.AI.Ollama` package was rejected
in its favour). Provider, base URL and model names are **config values,
validated at startup**. Structuring and synthesis are **independently
configurable** to different models, via keyed DI (`Llm:Models:Structuring` /
`Llm:Models:Synthesis`).

## Consequences

- The production story this enables: **"switching to Azure OpenAI is a config
  change plus an eval run."** That eval run is not optional — prompts are not
  perfectly portable across models, quality must be re-measured, and moving
  from local to hosted is a data-residency decision that belongs to the
  customer.
- The keyed structuring/synthesis split is retained even though one model
  (Poro-2-8B) currently serves both roles — see
  [ADR-0003](0003-poro-for-both-roles.md) — so the roles stay independently
  swappable tomorrow.
- The abstraction is where provider-specific escape hatches live (e.g. the
  API-level `think: false` seeded via `ChatOptions.RawRepresentationFactory`);
  callers never touch OllamaSharp types. See [../operations.md](../operations.md).
