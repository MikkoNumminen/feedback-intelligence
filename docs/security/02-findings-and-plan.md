# Phase 2 — Findings & remediation plan — 2026-07-09

Severity vocabulary per the skill (critical / high / medium / low / informational).
This phase is **read-only planning**: it assigns severity and a remediation approach.
Several surfaces were remediated in the same pass as the robustness-audit fixes
(branch `fix/audit-suite-2026-07-09`) and are marked **fixed (this branch)** — those
fixes are ordinary robustness hardening, not auth/crypto/secrets changes, so they did
not require the per-finding security gate. The **owner-decision** item (no-auth posture)
is NOT fixed and is surfaced for your call.

## Findings

| # | Finding | Severity | Status | Location |
|---|---|---|---|---|
| S1 | **No authentication/authorization on any endpoint** — all stored feedback is publicly readable (`GET /feedback`, `/feedback/{id}`, `/report`), and any caller can overwrite the shared snapshot (`?snapshot=true`) or trigger LLM work. | informational (accepted) | ✅ **accepted 2026-07-09** — owner confirmed synthetic-demo data; documented in `SECURITY.md` | `Program.cs` (all routes) |
| S2 | **Unauthenticated LLM-work DoS amplification** — `/feedback`, `/interpret`, `/report`, `/health` each trigger model work; a hung/slow generation held a `LlmGate` slot with no deadline → 503 for everyone. | high | ✅ **fixed (this branch)** | `LlmGate.cs`, `OllamaLlmClientFactory` timeout |
| S3 | **Unconditional Private-Network-Access grant** — PNA preflight answered `true` to any Origin. | low | ✅ **fixed (this branch)** — now gated on the CORS allowlist | `Program.cs` PNA middleware |
| S4 | **Snapshot write integrity** — concurrent `?snapshot=true` writers collided on a shared temp path, silently failing the atomic write. | medium | ✅ **fixed (this branch)** — per-writer unique temp + cleanup | `ReportService.WriteAtomicAsync` |
| S5 | **`X-Forwarded-For` trust vs rate-limit loopback exemption** — confirm a client can't spoof `XFF` to a loopback address and win `RateLimitExemptLoopback`, bypassing the limiter. | low (accepted) | ✅ **accepted 2026-07-09** — owner confirmed tunnel-only ingress; no direct-reach spoof path | `Program.cs` ForwardedHeaders + rate limiter |
| S6 | **`GET /feedback` data exposure without ownership model** — subset of S1; severity tracks data sensitivity. | (folds into S1) | ⛔ tied to S1 | `Program.cs` |

## Analysis & recommended remediation order

### S1 — no-auth posture (**owner decision — do not auto-fix**)
The map shows the API has **no auth layer at all**; confidentiality rests entirely on the
tunnel (Tailscale Funnel) + per-IP rate limit. This is consistent with a **synthetic-data
demo** deployment (per project memory, the corpora are generated Finnish demo feedback,
not real PII), in which case S1 is *informational / by-design* and the correct action is
to **document the posture** (a `SECURITY.md` "threat model & non-goals" note: "this is a
public demo over synthetic data; no auth by design; do not ingest real customer PII").

If the deployment will ever hold **real** feedback, S1 is **high** (mass data exposure +
snapshot-integrity write) and needs a real control: an API key / bearer token on
mutating + data-read routes, or network-level restriction. **I did not implement auth** —
adding an auth layer to a demo changes its shape and is exactly the kind of decision the
skill reserves for you (rules 8–9). **Which is it — synthetic-demo-by-design, or does
real data flow here?**

### S5 — forwarded-header / rate-limit interaction (verify, then maybe fix)
`app.UseForwardedHeaders(XForwardedFor|XForwardedProto)` runs with **default known-proxy
trust** (loopback only). If the backend is reachable ONLY via the loopback tunnel daemon,
a remote client cannot inject a trusted `XFF` and the real client IP is honoured — safe.
The residual risk is a deployment where the backend port is *also* directly reachable: a
direct caller could send `X-Forwarded-For: 127.0.0.1` and, if that resolves to a loopback
`RemoteIpAddress`, win `RateLimitExemptLoopback` and bypass the limiter. **Recommended:**
set `ForwardedHeadersOptions.KnownProxies`/`KnownNetworks` to the tunnel daemon's address
explicitly (don't rely on the default), OR bind Kestrel to loopback only so the tunnel is
the sole ingress. Low-risk hardening; needs a one-line confirmation of the bind/ingress
model before choosing.

### S2 / S3 / S4 — already remediated this branch
No further action; verify in Phase 5 that the fixes hold (timeout frees the gate slot,
PNA gated on Origin, unique snapshot temp names).

## Proposed next steps
1. **You decide S1** (synthetic-demo-by-design → document; real-data → plan an auth control). Nothing auto-applied.
2. **Confirm the ingress model for S5** (tunnel-only vs also-direct), then apply the `KnownProxies`/loopback-bind hardening if warranted.
3. **Phase 3 (remediation)** only proceeds per-finding on your approval; S2/S3/S4 are already done as robustness fixes in this PR.
4. **Phase 4 (docs)** — regardless of S1's answer, add `SECURITY.md` stating the deployment model, the no-auth posture, and "no real PII" as an explicit non-goal, plus the injection-defense invariants (cross-reference `docs/audits/llm-injection-2026-07-09.md`).

---

**Gate:** this is a plan, not a change. Tell me the S1 answer (demo vs real-data) and the
S5 ingress model, and I'll take the security audit forward from there. No security code was
modified in this phase.
