# Threat model

Scope: the Feedback-Intelligence API request path and its LLM + storage
boundaries, under the [demo operating assumption](../../SECURITY.md) (public demo,
synthetic corpora, tunnel-only ingress, no auth by design). Written 2026-07-09 from
the audit-suite attack-surface map; revisit when the operating assumption changes.

## Assets

| Asset | Why it matters |
|---|---|
| Stored feedback (SQLite) | The data the demo showcases; synthetic today, so exposure is low-impact **under the operating assumption** |
| Published snapshot (`report-latest.json`/`.html`) | The offline/shared-link fallback; integrity matters (a clobbered snapshot misleads viewers) |
| The local GPU / model capacity | Single shared resource; the scarce thing an attacker can exhaust |
| Report/trend correctness | The product's credibility — a manipulated item skewing an aggregate is the injection payoff |

## Actors

- **Anonymous remote client** (via the tunnel) — can call every endpoint; the primary actor.
- **Loopback/local tooling** (corpus push, `ctl`) — trusted; rate-limit exempt.
- **The model (Ollama)** — a *hijackable component*: any untrusted feedback text can attempt to steer its output. Treated as untrusted for authority purposes.

## Trust boundaries

1. **HTTP ingress** — untrusted body/query/headers enter here. Guards: rate limit, body-size cap, `RequestValidator`, CORS.
2. **LLM boundary** — untrusted feedback text is spliced into prompts. Guards: the six-layer injection defense.
3. **Persistence** — parameterized SQLite; no injection surface.
4. **Filesystem** — paths from config/domain only; no request-derived paths.

## Threats → mitigations

| # | Threat | Vector | Mitigation | Residual |
|---|---|---|---|---|
| T1 | Mass data read | `GET /feedback`, `/feedback/{id}`, `/report` unauthenticated | Tunnel + **owner-confirmed synthetic-data model** (2026-07-09) | **S1 accepted** — informational under demo model; becomes high if real data is ever introduced |
| T2 | Snapshot integrity write | `GET /report?snapshot=true` unauthenticated overwrite | Tunnel; **per-writer unique temp + atomic replace** (this PR) stops write-collision corruption | S1 (who may write) |
| T3 | Model-capacity DoS | Unauthenticated `/feedback` `/interpret` `/report` `/live/report` `/live/restructure` `/health` each trigger model work (`/live/restructure` amplifies: one call re-runs structuring over the bounded live-channel scope, ADR-0026; not reachable via the public /api proxy) | **`LlmGate` concurrency shedding + per-call timeout** (this PR) + input caps. The rate limit is a shared-audience ceiling on the proxied browser path (ADR-0025 — all visitors share the proxy egress IP); per-IP only for direct-Funnel callers | Bounded, not eliminated; per-visitor throttling absent behind the proxy |
| T4 | Prompt injection | Untrusted feedback text → prompt | **Six-layer defense** (ADR-0021): neutralize, symptom-flag, authority-bound, citation-ground, deterministic-anchor, red-team fixture | Injection is **unsolved**; named residuals pinned in fixture |
| T5 | Rate-limit / loopback-exemption bypass | Spoofed `X-Forwarded-For` → loopback exemption | **Tunnel-only ingress (owner-confirmed 2026-07-09)** — no direct reach, so no spoof path | **S5 accepted** — safe as deployed; pin `KnownProxies`/loopback-bind only if a direct interface is ever added |
| T6 | SQL injection | Feedback fields → SQL | All statements parameterized | none identified |
| T7 | Path traversal | Request input → filesystem path | No request-derived paths | none identified |
| T8 | SSRF | Request input → outbound URL | Ollama `BaseUrl` is config-sourced, not request-derived | none identified |
| T9 | Secret leakage | Secrets in source/config/logs | No secrets exist (local Ollama, SQLite file) | none identified |

## Out of scope

Tunnel/daemon configuration, host hardening, and the Azure Static Web App
frontend's own headers are deployment concerns outside this codebase. Dynamic
testing (live fuzzing, deployed CORS/PNA behaviour) was not performed — this is a
static, read-only model.
