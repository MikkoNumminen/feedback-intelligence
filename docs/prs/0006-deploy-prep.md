# PR #6 — chore(deploy): SWA/Funnel topology prep

Branch `chore/deploy-prep` → `main` (local; no remote exists).

## What

The frontend and backend live on different origins in the Phase 5 topology
(Azure SWA + Tailscale Funnel). This PR makes that work without code changes
at deploy time: `window.API_BASE` from a publish-time `config.js` (same-origin
locally), a config CORS allowlist in the API, `deploy/staticwebapp.config.json`,
and `tools/publish-frontend.ps1` which assembles `dist/` and (opt-in) bundles
the latest snapshot — the genuinely-backend-down fallback for shared links.

## Findings → resolutions (all fixed)

1. A network error (backend truly down) threw past the static snapshot
   fallback — the exact scenario it exists for — because `fetch` rejects
   rather than returning `!ok` → per-step `tryFetchJson` chain; a dead page
   is now impossible whenever a published snapshot exists.
2. `data/snapshots/` ignore rule was root-anchored while the API writes under
   the project dir — and placeholder-derived snapshots were already tracked →
   `**/data/snapshots/` + `git rm --cached`.
3. The publish script would silently ship whatever snapshot exists, including
   a placeholder-derived one, to a public URL → snapshot publication is now
   OPT-IN (`-PublishSnapshot`) with a provenance warning pointing at the
   mock-data register.
4. CORS origins were the only unvalidated config; a portal-pasted trailing
   slash would silently never match the browser's Origin header → startup
   validation (absolute http(s), no path, no trailing slash).
5. Snapshot source selection was first-match and CWD-relative → repo-root
   anchored via `$PSScriptRoot`, newest-wins across both snapshot locations.

Plus the PS 5.1 encoding trap found while smoke-testing (em dashes in a
BOM-less .ps1 decode into CP1252 smart quotes that PS parses as real quotes) —
the script is ASCII-only with a note, and the trap is recorded in memory.

## Verification

Build clean, 65/65 tests green (CORS validation covered by options validator
pattern), bundle assembly smoke-tested in both modes, ignore rules verified
with `git check-ignore`.
