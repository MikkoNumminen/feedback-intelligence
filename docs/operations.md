# Operations & environment

Operational facts and measured lessons that are neither invariants (those live
in [AGENTS.md](../AGENTS.md)) nor decisions (those are
[ADRs](decisions/README.md)). Read on demand.

## Local LLM serving — shared GPU, isolated store

LLM serving is local Ollama on the developer's machine (RTX 3080 Ti), reachable
publicly via Tailscale Funnel.

- **The GPU is shared** with the developer's other, live RAG stack (Docker
  compose project `mikkonumminendev`). That stack must **never** be modified or
  restarted by this project, and **before any LLM/GPU use you must announce it**
  so the owner can shut the RAG down first (this is a hard rule — see
  [AGENTS.md](../AGENTS.md)). Never assume the GPU or Ollama is free.
- **This project's Ollama is fully isolated:** its own compose service
  (`docker compose up -d ollama`), its own model volume, no restart policy (must
  never auto-start and contend for the GPU). A shared volume was rejected so
  that no pull or manifest update from this side could touch the live RAG's
  model store (same arm-isolation principle as the measurement work). The two
  models (`Llama-Poro-2-8B-Instruct-GGUF:Q4_K_M`, `qwen3:8b`) were seeded into
  the isolated volume via a one-time **read-only** mount of the RAG volume after
  a direct `ollama pull` hit repeated `tls: bad record MAC` network errors — a
  read-once copy, no ongoing coupling.

## Reuse measured in the sibling RAG (`mikkonumminen.dev`)

Ported rather than reinvented; each was measured there.

- **Reasoning suppression.** The RAG's `/no_think` soft switch was validated on
  Ollama's OpenAI-compat endpoint. **Measured correction (placeholder run):** on
  Ollama's *native* chat path with current qwen3 templates the soft switch is
  NOT honored — thinking stays on and silently consumes the `num_predict`
  budget (truncated/empty answers). Use the API-level **`think: false`**
  instead (`ChatRequest.Think` seeded via `ChatOptions.RawRepresentationFactory`
  inside the Llm project; verified against OllamaSharp 5.4.25 source).
- **Containment defaults**, config-validated at startup:

  | Setting | Value |
  |---|---|
  | Input length cap | 800 chars |
  | Request-body cap | 16 KB |
  | LLM concurrency | 2, with 0.5 s acquire-then-**shed** (never queue behind a busy GPU) |
  | Per-IP rate limit | 30 requests / 60 s |
  | Output-token cap | `num_predict` / `MaxOutputTokens` |

  Loopback callers are exempt from the rate limit
  (`Ingest:RateLimitExemptLoopback`, default on) — a corpus push of dozens of
  items cannot fit a 30/60 s window, and "never accept throttled data as
  variance" is the RAG's lesson. Tunnel traffic keeps its real client IP via
  forwarded headers and stays limited.
- **`OLLAMA_CONTEXT_LENGTH`** is a server-side env var on the ollama container
  (default 4096), not a per-request knob; the backend reads the same value from
  config when it needs to reason about the window.
- **Health checks prove a 1-token real completion**, not merely that the server
  answers — "server up" does not mean "model loaded and generating".

## feedctl — operator CLI

`tools/FeedbackIntelligence.Ctl` (`dotnet run --project tools/FeedbackIntelligence.Ctl --
<cmd>`, or no args for an interactive REPL) is the operator surface for the
demo, modelled on the sibling RAG's `ragctl`. It orchestrates docker, the
`dotnet` API process, and the local HTTP API — BCL-only, no dependencies.

- **`status` / `watch`** — a colour-coded live board: docker · **shared-RAG
  guard** · isolated ollama · model loaded · GPU (nvidia-smi) · API process ·
  `/health` · demo data count · **public link (Funnel)** · snapshot. Headline
  verdict: "demo is LIVE".
- **`up` / `down`** — bring the demo live (start ollama → start the API
  detached with a tracked PID → warm Poro → **expose the public Tailscale
  Funnel**) / take the Funnel down, stop both, free the GPU. `up` **refuses if
  the shared `mikkonumminendev` RAG is running** — the announce-before-GPU hard
  rule, enforced by the tool. Optional **`up --supervise`** stays in the
  foreground and restarts the API if it dies mid-demo, backing off the instant
  the shared RAG reappears (a re-checked guard, so supervision never contends for
  the GPU). There is deliberately **no boot-autostart**: an auto-started backend
  would warm Poro and grab the Funnel's port 443 unattended, contending for the
  two shared resources — exactly what the isolation discipline (and `up`'s own
  RAG guard) forbid. Robustness is operator-invoked, never automatic.
- **`data <mock|demo|clean>`** — explicitly choose the DB's starting data:
  `mock` (AI-generated placeholder corpus, non-evidential), `demo` (the real
  seeded corpus), or `clean` (empty). Each wipes the DB and restarts the API on
  it (ollama stays up); the board then shows the loaded dataset's provenance. A
  direct `load` clears that provenance marker (bare count on the board).
- **`demo [--seed N]`** — the full run-through: `generate → up → load →
  report → verify` in one command.
- **`interpret "…"`** (live desk structuring, timed) · **`load` / `report` /
  `verify`** (ingest a corpus / generate + summarize a report / acceptance vs
  ground truth) · **`telemetry`** (per-field desk correction rates) ·
  **`restructure`** (re-run structuring over the live channel with the current
  categories — ADR-0026, after a vocabulary change) · **`logs`** · **`open`**.

Runtime state (PID file, API log) lives in a gitignored `.feedctl/`.

## Deploy topology (Phase 5)

- **Frontend → Azure Static Web Apps** (free tier). The pages read
  `window.API_BASE` from a publish-time `config.js` (same-origin locally; on
  the static host it is `/api` — the same-origin managed-function proxy that
  forwards server-side to the Funnel,
  [ADR-0025](decisions/0025-same-origin-api-proxy.md); the browser never makes
  a cross-origin or local-network request). `tools/publish-frontend.ps1
  -ApiBase '/api'` assembles `dist/` with the pages (index, desk, and the
  demo.html alias), `deploy/staticwebapp.config.json`, and the
  report snapshot (JSON + HTML) so a shared link renders a situational view even
  with the backend down. **CI always bundles the committed, provenance-verified
  seed-42 snapshot** (`deploy/snapshot/`), so the shared link is never a 404; the
  `-PublishSnapshot` switch stays opt-in only for a *locally generated* runtime
  snapshot, whose provenance must be verified against
  [mock-data-register.md](mock-data-register.md) before deploying.
- **Backend → Tailscale Funnel**, owned by feedctl (`up` exposes it on port 443
  → the API's `:5088`, `down` frees 443). Browser traffic reaches the Funnel
  only via the SWA's `/api` proxy (ADR-0025); the proxied path involves no
  CORS. The API's CORS allowlist (`Ingest:AllowedCorsOrigins`) and the
  `Access-Control-Allow-Private-Network` preflight answer
  ([ADR-0023](decisions/0023-deploy-hardening-snapshot-and-pna.md)) remain as
  defense for DIRECT Funnel access only — Chrome's 2026 Local Network Access
  permission made the direct browser→Funnel path unusable on Tailscale-running
  machines regardless of those headers. ForwardedHeaders runs before the rate
  limiter; note the proxied path arrives from Azure egress IPs, so per-visitor
  rate limiting is coarse there (the LlmGate concurrency bound and input caps
  are the real GPU protection, per ADR-0025).
- **Coexistence with the sibling RAG (`ragctl`).** Both projects are
  single-tenant on TWO shared resources: the one GPU, and the Funnel's public
  port 443 (ragctl funnels its `:8000` backend; feedctl funnels the API on
  `:5088`). **Run one at a time.** The GPU is guarded (`up` refuses while the RAG
  container is up); the Funnel is feedctl-owned (up exposes it, down frees 443)
  and the board's `public link` row warns if 443 points at the RAG. To switch:
  `feedctl down` (or `ragctl down`) first, then bring the other up.
- Snapshot mode is verified with the backend deliberately stopped.

### Deploying the frontend to Azure ($0, one-time setup)

**Cost model:** one Static Web App on the **Free** SKU: the static bundle plus
ONE Free-tier managed function — the same-origin `/api` proxy
([ADR-0025](decisions/0025-same-origin-api-proxy.md)); no App Service, no
separate Functions plan. Still $0 with no time limit (managed functions are
included in the Free SKU). Guardrails and the "why not Readlog's App Service"
reconciliation are in
[ADR-0016](decisions/0016-zero-cost-static-web-apps-deploy.md).

CI does the deploy: `.github/workflows/azure-static-web-apps.yml` builds `dist/`
via `publish-frontend.ps1` and uploads it to SWA Free on every frontend change to
`master` (or `workflow_dispatch`). It needs a one-time Azure resource plus two
repo settings:

1. **Create the Free SWA** — bring-your-own-CI, so do NOT link the repo in the
   portal (that would inject a competing workflow / a billable managed API):
   ```bash
   az group create -n rg-feedback-intelligence -l westeurope
   az staticwebapp create -n feedback-intelligence -g rg-feedback-intelligence -l westeurope --sku Free
   ```
2. **Deployment token → GitHub secret; deploy-enable flag → GitHub variable.**
   Pipe the token straight into `gh` so it never lands in shell history. The
   `FUNNEL_API_BASE` variable is now ONLY the deploy on/off gate (job-level `if`
   cannot read secrets) — its **value is no longer consumed**: the pages call
   same-origin `/api`, and the Funnel hostname lives in `api/src/index.js`
   (`BACKEND`, with the board-probe origin in feedctl's `Config.PublicSiteUrl`).
   To rotate the Funnel host, edit those two constants — NOT this variable:
   ```bash
   az staticwebapp secrets list -n feedback-intelligence -g rg-feedback-intelligence --query "properties.apiKey" -o tsv \
     | gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN -R <owner>/feedback-intelligence --body-file -
   gh variable set FUNNEL_API_BASE -R <owner>/feedback-intelligence -b "enabled"
   ```
3. **CORS allowlist (LOCAL machine, optional under ADR-0025)** — the browser
   path is same-origin `/api` and needs NO CORS; this allowlist only matters
   for DIRECT cross-origin calls against the Funnel (ops tooling, back-compat).
   If setting it, get the SWA host:
   ```bash
   az staticwebapp show -n feedback-intelligence -g rg-feedback-intelligence --query defaultHostname -o tsv
   ```
   Set `Ingest:AllowedCorsOrigins` to `["https://<that-host>"]` (absolute https,
   no trailing slash — startup validation rejects otherwise) and restart the API.
4. **Deploy + verify $0:**
   ```bash
   gh workflow run azure-static-web-apps.yml -R <owner>/feedback-intelligence
   az staticwebapp show -n feedback-intelligence -g rg-feedback-intelligence --query sku -o json   # -> { "name": "Free", ... }
   ```
   Confirm $0 in Cost Management (scope: `rg-feedback-intelligence`). Optional
   tripwire: a $1 budget alert on the group.

The committed `deploy/snapshot/` (real seed-42, provenance-verified) is bundled
on **every** deploy, so a shared link always has an offline fallback
([ADR-0023](decisions/0023-deploy-hardening-snapshot-and-pna.md)). To refresh it,
regenerate the snapshot (`feedctl report`, which sets `?snapshot=true`), copy the
new `report-latest.{json,html}` into `deploy/snapshot/`, and push — after
re-verifying provenance against [mock-data-register.md](mock-data-register.md).
The `-PublishSnapshot` switch remains only for a *locally generated* runtime
snapshot.
