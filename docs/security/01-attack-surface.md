# Phase 1 — Attack-surface map — 2026-07-09

- Commit: `0689d9de848bb481dea4acef483095ce07118ae0` on branch `master`
- Scope: whole codebase, security lens. **Inventory only — no findings/proposals** (Phase 2's job).
- Deployment model (from code + comments): the API is a demo backend published via a **local tunnel daemon** (Tailscale Funnel); an Azure Static Web App frontend is a separate origin. Confidentiality rests on the tunnel + a per-IP rate limiter, **not** on application auth.

## 1. Trust boundaries

| Boundary | Where | Untrusted input | Existing controls |
|---|---|---|---|
| HTTP ingress | `Program.cs` Minimal-API endpoints | request body, query params, headers | body-size cap (Kestrel `MaxRequestBodySize`), `RequestValidator`, per-IP rate limiter, CORS allowlist |
| LLM boundary | `LlmStructuringService`, `ReportService` → Ollama | feedback text spliced into prompts | full 6-layer injection defense (see `docs/audits/llm-injection-2026-07-09.md`) |
| Persistence | `FeedbackStore` → SQLite file | feedback fields | parameterized SQL throughout |
| Filesystem | `AppPathResolver`, snapshot read/write, prompt/domain files | config/domain-derived paths only | paths never derived from request input |
| Process spawn | `tools/FeedbackIntelligence.Ctl` (operator CLI, not web-reachable) | operator args | `ProcessStartInfo.ArgumentList` (no shell string) |

## 2. Endpoint inventory — **all unauthenticated**

There is **no authentication or authorization middleware anywhere.** This is the central
attack-surface fact for Phase 2 to assess (it is an apparent *by-design* posture for a
synthetic-data demo, not necessarily a defect — severity is Phase 2's call).

| Method / route | Reads | Writes | LLM work | Notes |
|---|---|---|---|---|
| `POST /feedback` | — | SQLite insert | yes (structuring) | validated + rate-limited; untrusted text → model + store |
| `POST /interpret` | — | none | yes | preview; no persistence |
| `GET /schema` | domain taxonomy | — | no | public config echo |
| `GET /feedback/{id}` | any stored item by id | — | no | **no ownership check** — anyone who knows/guesses an id reads it (ids are GUIDs or caller-supplied) |
| `GET /feedback` | list of stored items | — | no | bounded by `QueryMaxLimit`; **all stored feedback publicly listable** |
| `GET /report` | aggregate + narratives | snapshot files if `?snapshot=true` | yes (≤N calls) | `?snapshot=true` lets **any caller overwrite** the shared snapshot; ~40 s synthesis per uncached window |
| `GET /telemetry/corrections` | correction-rate aggregate | — | no | window-validated |
| `GET /report/snapshot(.html)` | persisted snapshot | — | no | serves last snapshot |
| `GET /health` | — | — | **yes — a real 1-token completion** | unauthenticated; each call occupies the model briefly |
| static files | `wwwroot` via `UseDefaultFiles`/`UseStaticFiles` | — | no | frontend assets |

## 3. Cross-cutting controls (present)

- **Rate limiting** — per-IP fixed window (`RateLimitRequests`/`RateLimitWindowSeconds`), `QueueLimit=0`, loopback exemptible via `RateLimitExemptLoopback`.
- **ForwardedHeaders** — `X-Forwarded-For`/`-Proto` processed so tunneled clients carry real IPs into the limiter; **default known-proxy trust** (loopback).
- **CORS** — explicit origin allowlist (config; empty ⇒ same-origin), `GET,POST` + `Content-Type` only, **no** `AllowCredentials`, **no** `AllowAnyOrigin`.
- **Private-Network-Access preflight** (`Program.cs:119`) — answers `Access-Control-Allow-Private-Network: true` to any OPTIONS carrying the request header (unconditional; response reads still gated by CORS).
- **Input validation** — `RequestValidator` (text length caps via `IngestOptions`, id shape, source allowlist); timestamps normalized before use; query `limit` clamped.
- **Config validation at boot** — options validated with `ValidateOnStart`; missing domain prompts/languages fail fast.

## 4. Secrets & crypto

- **No secrets in source or config.** `appsettings*.json` hold only Logging + feature/config values; grep for key/secret/password/token matched only `CancellationToken` params. Ollama is local (no API key); SQLite is a local file (no credentials). No JWT/cookie/session crypto exists (no auth).
- No custom cryptography. `PromptLockTests` uses SHA-256 only to pin prompt content (integrity check, not a secret).

## 5. Surfaces flagged for Phase 2 severity assessment (NOT yet rated)

These are surfaces the map exposes; Phase 2 assigns severity and a remediation plan:

1. **No auth on any endpoint** → confidentiality of all stored feedback + integrity of the shared snapshot (`?snapshot=true`) depend solely on the tunnel + rate limit. Assess against the intended deployment (demo, synthetic corpora) vs any path to real data.
2. **Unauthenticated LLM-work amplification** — `/feedback`, `/interpret`, `/report`, `/health` each trigger model work; combined with the **unbounded model-call timeout** and `LlmGate` slot exhaustion already filed as **high** in `docs/audits/audit-2026-07-09.md` (`OllamaLlmClientFactory.cs:12`, `LlmGate.cs:29`), this is a denial-of-service surface. Cross-reference, don't re-derive.
3. **`X-Forwarded-For` trust vs rate-limit / loopback exemption** — confirm the known-proxy configuration cannot let a client spoof `XFF` to a loopback address and win `RateLimitExemptLoopback`, bypassing the limiter.
4. **`GET /feedback` / `/feedback/{id}` data exposure** — no ownership model; whether that matters depends on data sensitivity (Phase 2).
5. **Unconditional PNA grant** (`Program.cs:119`) — already **low** in the robustness audit; include for completeness.
6. **Snapshot write contention** (`ReportService.cs:568`, shared `.tmp`) — integrity of the published snapshot; already **medium** in the robustness audit.

## Coverage / limits
Static, read-only inventory built from a full read of `Program.cs`, the storage/LLM/report
layers, and config. Dynamic testing (live request fuzzing, tunnel config, deployed CORS
behaviour) was **not** performed — out of scope for a read-only pass. `tools/…/Ctl` is an
operator CLI, not web-reachable, so its process-spawn surface is local-trust only.

---

**Phase 1 complete. Gate:** reply **approved** to authorise Phase 2 (findings + severity +
remediation plan), or `redo` with scope changes. No code has been or will be modified
without that approval.
