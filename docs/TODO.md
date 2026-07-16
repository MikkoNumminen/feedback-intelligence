# TODO — items requiring Mikko's input

Kept current during the autonomous run-through (2026-07-03). Mirrors the
session task list; this file is the durable copy.

| # | Item | Blocked on | Unblocks |
|---|------|-----------|----------|
| 1 | Write core corpus into `data/corpus/core.jsonl` — format and the authoritative per-story breakdown: `data/corpus/README.md`; safety texts verified against `domains/retail/alert-keywords.json` | Mikko writing | announced variants run → real generated corpus → salvage smoke test |
| 2 | Judge story-variant intensity stability after the first real variants run (keep ×2 or drop to ×0) | #1 | final variants.jsonl commit |
| ~~3~~ | ~~Azure Static Web Apps deployment~~ **DONE** — deployed on SWA Free; CI always bundles the committed seed-42 snapshot (`deploy/snapshot/`) | — | — |
| ~~4~~ | ~~Tailscale Funnel exposure of the backend~~ **DONE** — `paskamyrsky.tail6ed53b.ts.net` → API `:5088`, feedctl-owned | — | — |
| ~~5~~ | ~~Review `docs/mock-data-register.md`; approve each replacement/retirement~~ **DONE 2026-07-07** | — | — |
| ~~6~~ | ~~5-minute demo rehearsal on a fresh seed (`docs/demo-script.md`)~~ **DONE 2026-07-07** | — | — |
| ~~7~~ | ~~Announce a GPU window for the full LIVE run-through~~ **DONE 2026-07-04** — live loop verified end to end (see CLAUDE.md run-through status); the keyword-free alert-nomination case still waits for the real corpus | — | — |
| 8 | Maintenance watch: bump `SQLitePCLRaw.bundle_e_sqlite3` when upstream ships SQLite ≥ 3.50.2 (GHSA-2m69-gcr7-jv3q has no patched version as of 2026-07-03; exposure assessed low — see docs/audits/dotnet-2026-07-03.md) | upstream release | clean vulnerability scan |
| ~~9~~ | ~~Rebuild + restart the local API/desk instance to pick up the merged demoted-content fixes (PRs #58/#59)~~ **DONE 2026-07-15** — `feedctl down` → rebuild API → `feedctl up`; verified live on `/live/report` (Yhteenveto rated-only, moderation buckets count-only with no trend) | — | — |
