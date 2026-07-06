# ADR-0023 — Deploy hardening: always-bundle the committed snapshot + Private Network Access

- **Status:** Accepted (2026-07-07)
- **Deciders:** Mikko
- **Amends:** [ADR-0016](0016-zero-cost-static-web-apps-deploy.md) (SWA Free deploy —
  supersedes its "snapshot bundling stays opt-in" consequence)
- **Relates to:** [ADR-0009](0009-grounding-is-structural.md) (snapshot fallback)

## Context

After ADR-0016 shipped, the live shared link went blank — "0 feedback items."
Root cause was two independent faults that lined up:

1. **Snapshot bundling was opt-in.** A routine frontend push retriggers the SWA
   deploy, which rebuilt `dist/` **without** a snapshot, so the offline fallback
   404'd. The link went blank exactly when the backend was unreachable — the one
   moment the fallback exists for.
2. **Chrome Private Network Access (PNA)** blocks a **public** origin (the SWA)
   from fetching a **private-range** target (the tailnet Funnel via MagicDNS)
   unless the server acknowledges it on the CORS preflight. So even with a
   backend up, the live fetch was denied.

The deterministic layer, grounding, and snapshot pipeline were all fine; the gap
was purely in *deploy shape* and a browser security boundary ADR-0016 predated.

## Decision

1. **Commit a real, provenance-verified seed-42 snapshot** at
   `deploy/snapshot/report-latest.{json,html}` and **always bundle it** in CI.
   `publish-frontend.ps1` bundles the newest `report-latest.json` among its
   candidate dirs, and in a fresh CI checkout the committed `deploy/snapshot/` is
   the only candidate, so it is always the one shipped; the workflow hardcodes
   `-PublishSnapshot`. This **supersedes
   ADR-0016's "snapshot bundling stays opt-in"**: the `-PublishSnapshot` switch
   now governs only a *locally generated runtime* snapshot; the committed demo
   snapshot ships on **every** deploy so the link is never a 404. The placeholder
   ban ([mock-data-register.md](../mock-data-register.md)) is honored
   **structurally** — the one committed snapshot is real seed-42, and CI only
   ever bundles that committed file, never a runtime-generated one.
2. **PNA acknowledgement.** The API answers the CORS preflight with
   `Access-Control-Allow-Private-Network: true` (a small middleware before
   `UseCors`) so the public SWA origin may reach the private Funnel target. The
   CORS allowlist still gates the actual request — this ACK widens *nothing*
   about who may call, only satisfies the browser's private-network preflight.
3. **Snapshot-first render.** The frontend paints the committed snapshot before
   attempting the live fetch, so a PNA/live failure degrades to the full
   situational view rather than a blank page.

## Consequences

- The shared link renders the full corpus **same-origin**, independent of
  backend or PNA state; "live" (the desk-entry beat) is an enhancement, not a
  precondition for a non-blank page.
- **Tradeoff, accepted:** a generated data artifact now lives in the repo
  (`deploy/snapshot/`). It is the only way a static host can *guarantee* a
  non-blank page with the backend down. Kept honest by the mock-data register
  and the always-real-seed-42 rule; refreshing it is a deliberate step
  (regenerate via `feedctl report`, copy in, re-verify provenance, push).
- **PNA enforcement differs between headless and real Chrome.** The
  snapshot-first render makes that difference cosmetic — worst case the badge
  reads "snapshot" instead of "live," never blank.
- The vestigial `publish_snapshot` `workflow_dispatch` input from ADR-0016's
  workflow is removed: it no longer gates anything (the build step always
  bundles).
