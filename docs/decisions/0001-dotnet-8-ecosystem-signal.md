# ADR-0001 — C# / .NET 8 (LTS) for ecosystem signal

- **Status:** Accepted (2026-07-03)
- **Deciders:** Mikko

## Context

The project is a work-sample demo aimed at Finnish enterprise employers
(S-ryhmä / Kesko class) — Microsoft-shop environments. The stack choice is a
signal to that audience as much as a technical decision. The developer is newer
to .NET AI integration than to other ecosystems, which is why the build order
was made RISK-FIRST rather than familiarity-first.

## Decision

Backend in **C# / .NET (ASP.NET Core minimal API)**, targeting **.NET 8 (LTS),
not .NET 10**.

## Consequences

- Matching the target audience's stack IS the signal — the same reasoning that
  picked C# in the first place. Enterprise shops run the established LTS, not
  the newest major.
- .NET 8 is supported through Nov 2026; the demo's life as a work sample is
  weeks-to-months, so the support window is not a concern, and an 8→10 bump is
  trivial if ever needed. The machine already has the 8.0 runtime.
- Accepted cost: the developer being newer to .NET AI integration — retired by
  proving .NET↔Ollama integration first (see [ADR-0002](0002-llm-behind-one-abstraction.md)
  and the Phase 0 spike in [../plan.md](../plan.md)).
