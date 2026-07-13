# Security

This document describes the security posture of Feedback-Intelligence for the
**operator and any reviewer**. It is written to be read top-to-bottom before
touching the request path. It states plainly what is defended, what is *not*, and
the one operating assumption the whole posture rests on.

> **Prompt injection is an unsolved problem.** The LLM boundary here is defended
> in depth (six layers, see [Invariants](docs/security/invariants.md) and
> [ADR-0021](docs/decisions/)), but no layer *proves* safety. Read "defended" as
> "the concrete breakout mechanics are closed and monitored", never as "cannot be
> injected".

## Operating model (owner-confirmed 2026-07-09)

**This service is a public *demo* over *synthetic* feedback corpora, reachable
only through a local tunnel daemon** — both confirmed by the owner on 2026-07-09.
Under that model there is deliberately **no authentication**: confidentiality of the
stored feedback and integrity of the published snapshot rest on the tunnel plus a
per-IP rate limiter, not on app auth. This is a documented, accepted posture, not an
oversight.

- ✅ **If the data is synthetic demo content** (the current assumption — the corpora
  are generated, not real customer PII), the no-auth posture is *by design*. Do
  **not** ingest real customer feedback into this deployment.
- ⛔ **If real feedback will ever flow here**, the no-auth posture becomes a
  high-severity exposure (mass data read via `GET /feedback`, snapshot-integrity
  write via `?snapshot=true`). Add a control first — an API key / bearer token on
  the data-read and mutating routes, or network-level restriction — see
  [`docs/security/02-findings-and-plan.md`](docs/security/02-findings-and-plan.md)
  finding **S1**.

Everything below is written for the demo assumption. If you change it, revisit S1.

## What is defended

| Surface | Control | Where |
|---|---|---|
| Request volume | Fixed-window rate limit (loopback exemptible). On the browser path (same-origin `/api` proxy, ADR-0025) every visitor arrives as the proxy's egress IP, so the limit is a **shared audience ceiling**, not per-visitor; direct-Funnel callers still carry their real IP via forwarded headers. GPU work is independently bounded by `LlmGate` + input caps | `Program.cs` rate limiter |
| Request size | Kestrel body cap (`Ingest:MaxBodyBytes`) + text length cap (`Ingest:InputMaxChars`) enforced **before** any LLM work | `Program.cs`, `RequestValidator` |
| Model-call exhaustion | Concurrency gate (`LlmGate`, shed-not-queue) **and** a server-side per-call timeout (`Ingest:LlmCallTimeoutMs`) so a hung generation can't hold a slot | `LlmGate` |
| Prompt injection | Six-layer defense-in-depth at every prompt splice (neutralize · symptom-flag · authority-bound · citation-ground · deterministic-anchor · red-team fixture) | `Core/Security/*`, `Alerts/AlertMatcher`, `ReportService`, ADR-0021 |
| SQL | Every statement fully parameterized (`SqliteParameter`); no string-built SQL from input | `Storage/FeedbackStore` |
| Cross-origin | The browser path is **same-origin** via the `/api` proxy (ADR-0025) and exercises no CORS. The explicit CORS Origin allowlist (`Ingest:AllowedCorsOrigins`, `GET`/`POST` + `Content-Type` only, **no** credentials) and the Private-Network-Access preflight grant remain as defense for DIRECT Funnel access only | `Program.cs`, `api/src/index.js` |
| Path handling | File paths derive only from config/domain descriptors, never from request input; `/feedback/{id}` uses the id only as a SQL parameter | `AppPathResolver`, `FeedbackStore` |
| Secrets | None in source or config — local Ollama (no API key), SQLite file (no credentials) | — |

## What is NOT defended (known residuals)

- **No authentication** — see the operating assumption above (S1).
- **Prompt injection is not solved** — the six layers close breakout mechanics and
  monitor symptoms; a determined adversary against a local 8B model is a residual.
  Named residuals (homoglyph fence markers, paraphrased directives) are pinned in
  the red-team fixture and documented in `Core/Security/*`.
- **Forwarded-header trust** — the rate-limit loopback exemption trusts the
  processed client IP. The backend is **tunnel-only (loopback ingress)**
  (owner-confirmed 2026-07-09), so a remote client cannot inject a trusted
  `X-Forwarded-For` and this is **safe** as deployed (finding **S5**, accepted). If
  the backend is ever also bound to a directly-reachable interface, pin
  `KnownProxies` / keep Kestrel loopback-only before doing so.

## Threat model & invariants

- [`docs/security/threat-model.md`](docs/security/threat-model.md) — assets,
  actors, trust boundaries, and threat → mitigation table.
- [`docs/security/invariants.md`](docs/security/invariants.md) — the load-bearing
  security invariants that must not regress (with the tests that guard them).
- [`docs/security/01-attack-surface.md`](docs/security/01-attack-surface.md) — the
  endpoint/surface inventory.
- [`docs/audits/llm-injection-2026-07-09.md`](docs/audits/llm-injection-2026-07-09.md)
  — the injection-boundary audit (all six layers present).

## Reporting a vulnerability

This is a personal demo/portfolio project. Report issues privately to the
repository owner via a GitHub issue marked security-sensitive (no exploit detail
in the public issue) or direct contact. Do not include working exploit payloads in
public issues — exploit detail stays in `docs/security/`.
