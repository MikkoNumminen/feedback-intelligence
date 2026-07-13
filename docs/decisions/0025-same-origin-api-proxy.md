# ADR-0025 — Same-origin /api proxy on the static host (amends 0016, 0023)

- **Status:** Accepted (2026-07-13)
- **Deciders:** Mikko

## Context

Chrome's 2026 **Local Network Access** permission broke the deployed
architecture on the one machine that matters most: the operator's. The pages
on the Azure Static Web App fetched the backend cross-origin at the Tailscale
Funnel hostname — which MagicDNS resolves to a *private* tailnet address on
any machine running Tailscale. Chrome now gates public-page→local-address
fetches behind a **user permission**, and the server-side
`Access-Control-Allow-Private-Network` grant (ADR-0023's PNA handshake) no
longer suffices. Symptom: every server-side check green (CORS, PNA preflight,
Funnel, health) while the operator's browser showed "Palvelin ei ollut
tavoitettavissa" — reproduced in headless Edge with
`Permission was denied for this request to access the 'local' address space`.
External visitors were never affected (for them the Funnel hostname resolves
to Tailscale's public edge), but the demo must work on the presenter's own
screen without browser or registry tweaks.

The owner's sibling project solved this class of problem already:
mikkonumminen.dev's frontend calls a **same-origin** `/api/*` path and the
static host's edge forwards server-side to the Funnel (its ADR-0012). The
browser sees one public origin; no cross-origin fetch, no local address, no
permission.

## Decision

Port that pattern to Azure Static Web Apps using its Free-tier **managed
function** as the edge proxy:

- `api/` holds one anonymous HTTP function on route `{*path}` that forwards
  GET/POST to the Funnel and relays status, content-type, and body. The
  Funnel URL is hardcoded there (it is already public in the CORS allowlist;
  no portal-managed configuration).
- `config.js` is published with `window.API_BASE = "/api"` — the pages are
  unchanged; they already prefix every call with `API_BASE`.
- The SWA fallback excludes `/api/*`; the deploy workflow sets
  `api_location: "api"` (the action builds the function; the app stays
  pre-built). Managed functions are included in the Free tier — still $0.
- Local dev is untouched: config.js 404s and the pages stay same-origin
  against the local API.

## Consequences

- The demo works identically on every machine, including the operator's —
  no browser permission, no registry policy, no Tailscale DNS games.
- CORS and the ADR-0023 PNA middleware become **defense for direct-Funnel
  access only** (ops tooling, back-compat); the browser path no longer
  exercises them. They stay: removing working hardening to save lines would
  be false economy.
- **Per-visitor rate limiting is lost on the proxied path**: requests reach
  the API from Azure egress IPs (and the Funnel's proxy rewrites
  X-Forwarded-For anyway — the sibling repo hit the same). The GPU remains
  protected by the LlmGate concurrency bound, the input caps, and the report
  cache; accepted, as in mikkonumminen.dev's ADR-0012.
- A cold managed function adds seconds to the first API call after idle.
  The snapshot-first render absorbs this on the management view; the desk
  page's first interpret after a long idle may feel slow once.
- The "one moving part" purity of ADR-0016 (static files only) is traded for
  the demo actually working in 2026 browsers. The alternative — a
  registry-deployed browser policy weakening Local Network Access for the
  origin — was rejected: it fixes one machine, silently, and touches a
  security control.
