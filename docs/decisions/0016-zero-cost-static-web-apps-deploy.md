# ADR-0016 — Zero-cost frontend deploy: Azure Static Web Apps Free, static-only

- **Status:** Accepted (2026-07-06)
- **Deciders:** Mikko
- **Relates to:** [ADR-0009](0009-grounding-is-structural.md) (snapshot fallback),
  PR-0006 (SWA/Funnel topology prep)

## Context

Phase 5 needs the frontend publicly reachable (a shared interview link). Hard
owner constraint: **no Azure charges, ever** — not a trial credit, not "cheap",
zero. The split-origin topology is already decided: the .NET API + Ollama stay
on the dev machine and are reached from the browser via a **Tailscale Funnel**
URL; only the **static** frontend (`index.html`, `desk.html`, a generated
`config.js`, `staticwebapp.config.json`, and optionally the persisted report
snapshot) is hosted in Azure.

The sibling project `Readlog-csharp` also deploys to Azure "at $0", but by a
different route: a **containerized .NET backend** on **App Service Free F1**
(image from ghcr.io, OIDC login, `azure/webapps-deploy`). That apparatus exists
to host a *server* in Azure. **We host no server in Azure** — copying its F1
plan, container build, or OIDC federation would provision compute we do not
need and is the wrong pattern here.

## Decision

Deploy the static frontend to **one Azure Static Web App on the `Free` SKU**,
static-only, via a **bring-your-own-token GitHub Actions workflow**
(`.github/workflows/azure-static-web-apps.yml`):

- The workflow builds `dist/` with the existing `tools/publish-frontend.ps1`
  (Funnel URL from repo variable `FUNNEL_API_BASE`) and uploads it with
  `Azure/static-web-apps-deploy@v1`, `skip_app_build: true`, `api_location: ""`.
- Credential: a single **deployment-token** secret
  (`AZURE_STATIC_WEB_APPS_API_TOKEN`) — not Readlog's OIDC (acceptable: the blast
  radius is a $0 static app, and the token is masked in logs, rotatable).
- The SWA is created **without linking the GitHub repo** in the portal, so Azure
  never injects its own competing workflow or auto-scaffolds a managed API.

The one-time `az` setup, CORS round-trip, and $0 verification live in
[operations.md](../operations.md).

## Consequences

- **$0, and why:** Static hosting on the Free SKU has no compute meter — no
  always-on process, no plan, no owned storage account. SWA's only paid
  dimensions are the **Standard SKU** and a **managed API (linked Functions)**;
  both are explicitly avoided (`--sku Free`, `api_location: ""`,
  `skip_app_build: true`). Free tier is 100 GB/mo bandwidth + free SSL on the
  `*.azurestaticapps.net` host — no overage billing (it throttles, never
  charges).
- **The one real cost trap** is an accidental managed API: if `skip_app_build`/
  `api_location` are ever dropped and Oryx detects a buildable app, SWA can
  provision a billable Functions app. Those knobs are load-bearing; keep them.
- **CORS is a separate manual step on the LOCAL machine:** the deploy succeeding
  does not make the demo work until the SWA origin is added to the API's
  `Ingest:AllowedCorsOrigins` (absolute https, no trailing slash) and the API is
  restarted. The origin regenerates if the SWA is recreated.
- **Snapshot bundling stays opt-in** (`workflow_dispatch` input, default off):
  CI can't tell placeholder-derived snapshots from real, and the hard rule bans
  placeholder data in a demo — see `docs/mock-data-register.md`.
- **Tripwire:** a $1 budget alert on the resource group. If it ever fires,
  something paid was added.
- The backend's public reachability (Tailscale Funnel) is a separate owner task;
  this ADR covers only the static frontend's Azure hosting.
