# TODO — items requiring Mikko's input

Kept current during the autonomous run-through (2026-07-03). Mirrors the
session task list; this file is the durable copy.

| # | Item | Blocked on | Unblocks |
|---|------|-----------|----------|
| 1 | Write core corpus into `data/corpus/core.jsonl` — format and the authoritative per-story breakdown: `data/corpus/README.md`; safety texts verified against `config/alert-keywords.json` | Mikko writing | announced variants run → real generated corpus → salvage smoke test |
| 2 | Judge story-variant intensity stability after the first real variants run (keep ×2 or drop to ×0) | #1 | final variants.jsonl commit |
| 3 | Azure Static Web Apps deployment | Azure account; remote-repo decision | Phase 5 public frontend |
| 4 | Tailscale Funnel exposure of the backend | Mikko's tailnet + funnel setup | Phase 5 public backend |
| 5 | Review `docs/mock-data-register.md`; approve each replacement/retirement | run-through complete | demo readiness |
| 6 | 5-minute demo rehearsal on a fresh seed (`docs/demo-script.md`) | #1, #3, #4 | interview readiness |
| 7 | Announce a GPU window for the full LIVE run-through: real Poro structuring at ingest, synthesis narratives + alert nominations in /report, desk /interpret moment. (Everything else was exercised end to end without the LLM — the RAG was up during the autonomous run.) | RAG shutdown window | live-LLM verification of the whole loop |
