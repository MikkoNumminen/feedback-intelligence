# Audit suite — 2026-07-09

**Scope:** repo root (`D:\koodaamista\feedback-intelligence`), commit `0689d9de848bb481dea4acef483095ce07118ae0` on `master`
**Detected shape:** .NET 8 / C# — ASP.NET Core Minimal API + Core/LLM libs + CLI tools + xUnit tests (9 `.csproj`, 84 hand-written `.cs`, 0 `.cshtml`/`.razor`), Ollama LLM boundary, raw `Microsoft.Data.Sqlite` (no EF Core)
**Audits run:** 5 of 5 dispatched — 4 completed, 1 gated
**Matrix note:** the `.NET` shape routes the React slot to `dotnet-audit`; `llm-injection-audit` and `security-audit` were added beyond the canonical matrix at the owner's request for a comprehensive pass.

## Reports

| Audit | Status | Report | Findings |
| --- | --- | --- | ---: |
| dotnet-audit | ✅ ok | [dotnet-2026-07-09.md](./dotnet-2026-07-09.md) | 1 (low) |
| llm-injection-audit | ✅ ok | [llm-injection-2026-07-09.md](./llm-injection-2026-07-09.md) | 1 (low) — 6/6 layers present |
| ai-codegen-smell-audit | ✅ ok | [ai-smell-2026-07-09.md](./ai-smell-2026-07-09.md) | 3 (1 minor · 2 nit) |
| audit (robustness) | ✅ ok | [audit-2026-07-09.md](./audit-2026-07-09.md) | 18 (2 high · 6 med · 10 low) |
| security-audit | ⏸ gated | [../security/01-attack-surface.md](../security/01-attack-surface.md) | Phase 1 map complete; Phase 2 severity pending owner approval |

## Severity rollup (robustness `audit`)

| Severity | Count |
| --- | ---: |
| critical | 0 |
| high | 2 |
| medium | 6 |
| low | 10 |

## Headline

This is a **well-engineered, carefully-reviewed codebase.** Three of the four completed
audits came back nearly clean:

- **`.NET` anti-patterns:** 1 low. No DI-lifetime bugs, no async-blocking on request paths; raw-SQLite access is fully parameterized, cancellation-threaded, and `LIMIT`-bounded.
- **Prompt injection:** all **six** defense-in-depth layers present and wired at **every** LLM boundary (A separation · B symptom-flag · C authority-bound · D grounding · E deterministic anchor · F red-team fixture with a *named* homoglyph residual). 1 low durability note only. This repo *is* the reference implementation the injection checklist was extracted from.
- **AI-codegen smell:** 3 low-severity (1 real duplication, 2 nits). Zero phantom-TODOs, generic names, over-typing, or paraphrase comments — dense *why*-carrying comments throughout.

The substance is in the **robustness audit**, and it tells one coherent story worth acting on:

> **Unbounded model calls.** No server-side timeout bounds any Ollama request — every call
> carries only the client's cancellation token, and `LlmGate` holds a concurrency slot for
> the *whole* call. A hung generation ties up a request thread and, with the 2 default slots
> exhausted, returns **503 to every other client** — a DoS amplifier. The **security-audit**
> Phase 1 map independently reaches the same surface from the "unauthenticated LLM-work
> amplification" angle. This is the top fix.

## Recommended order of action

1. **`fix/audit-llm-timeouts` (high).** Add a server-side per-call timeout at `OllamaLlmClientFactory.cs:12` / wrap each `GetResponseAsync` in a `CancelAfter` linked CTS; frees the `LlmGate` slot and closes the DoS path (`LlmGate.cs:29`). Covers robustness findings ×4.
2. **Resume `security-audit` Phase 2** — reply **approved** (or run `/mikko-security-audit`). Six surfaces are pre-listed in the map §5; the no-auth posture needs an owner severity call against the intended demo deployment. **Gated — no security code was or will be changed without per-phase approval.**
3. **`fix/audit-robustness` (medium ×6)** — `/interpret` leaked-500, snapshot temp-path collision, hardcoded severity ranks, undisposed `Process` handle.
4. **`fix/audit-cleanup` (low, batchable)** — incl. `Board.cs:165` (cited by **three** audits — fix once) and `ReportService.cs:568` (cited by two).

Work in parallel across branches; the scopes are non-overlapping by construction. Two findings are cross-cited (`Board.cs:165`, `ReportService.cs:568`) — dedupe on fix.

## What this index is and isn't

A table of contents over five independent reports — it aggregates paths, statuses, and
counts; it does **not** synthesise findings into one list. Each report cites `file:line`
and stands on its own. Nothing here modifies code. **No merges without fresh per-PR owner
approval** (repo STRONGEST RULE).
