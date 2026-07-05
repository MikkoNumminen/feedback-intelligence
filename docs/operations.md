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
  `/health` · demo data count · snapshot. Headline verdict: "demo is LIVE".
- **`up` / `down`** — bring the demo live (start ollama → start the API
  detached with a tracked PID → warm Poro) / stop both and free the GPU. `up`
  **refuses if the shared `mikkonumminendev` RAG is running** — the
  announce-before-GPU hard rule, enforced by the tool.
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
  **`logs`** · **`open`**.

Runtime state (PID file, API log) lives in a gitignored `.feedctl/`.

## Deploy topology (Phase 5)

- **Frontend → Azure Static Web Apps** (free tier). The two pages read
  `window.API_BASE` from a publish-time `config.js` (same-origin locally; the
  Funnel URL on the static host). `tools/publish-frontend.ps1 -ApiBase <url>`
  assembles `dist/` with both pages, `deploy/staticwebapp.config.json`, and —
  with `-PublishSnapshot` — the latest report snapshot (JSON + HTML), so a
  shared link renders a situational view even with the backend down. Snapshot
  publication is opt-in on purpose: a placeholder-derived snapshot must never be
  deployed (verify provenance against
  [mock-data-register.md](mock-data-register.md)).
- **Backend → Tailscale Funnel.** The API's CORS allowlist
  (`Ingest:AllowedCorsOrigins`, empty = same-origin only) must include the SWA
  origin (no trailing slash — it is validated at startup). ForwardedHeaders runs
  before the rate limiter so tunneled clients carry their real IP.
- Snapshot mode is verified with the backend deliberately stopped.

### Deploying the frontend to Azure ($0, one-time setup)

**Cost model:** one Static Web App on the **Free** SKU, static-only — no managed
API, no Functions, no App Service. $0 with no time limit. Guardrails and the
"why not Readlog's App Service" reconciliation are in
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
2. **Deployment token → GitHub secret; Funnel URL → GitHub variable.** Pipe the
   token straight into `gh` so it never lands in shell history. Set **both** — the
   workflow gates on the variable, but the deploy step needs the token too:
   ```bash
   az staticwebapp secrets list -n feedback-intelligence -g rg-feedback-intelligence --query "properties.apiKey" -o tsv \
     | gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN -R <owner>/feedback-intelligence --body-file -
   gh variable set FUNNEL_API_BASE -R <owner>/feedback-intelligence -b "https://<machine>.<tailnet>.ts.net"
   ```
3. **CORS round-trip (LOCAL machine)** — the deploy succeeds but every API call is
   blocked until this is done. Get the SWA host, add it to the API, restart:
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

Publishing the snapshot is opt-in — `gh workflow run azure-static-web-apps.yml -f
publish_snapshot=true` — and only after verifying provenance against
[mock-data-register.md](mock-data-register.md).
