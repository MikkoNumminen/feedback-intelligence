# ADR-0024 — The desk is its own live channel: separate database, own segment

- **Status:** Accepted (2026-07-13)
- **Deciders:** Mikko

## Context

The public site serves two stories at once. The demo story is the frozen,
provenance-verified seed-42 snapshot (ADR-0023) — planted, machine-checkable,
never to be contaminated. The live story is a human at the desk logging real
entries and watching the AI process them. Until now both flowed into the same
database: a desk entry joined the seeded corpus and surfaced inside the demo's
live report, so real entries and planted evidence mixed in one dataset, in both
directions — a desk test entry could drift into demo evidence, and the seeded
corpus (which deliberately *simulates* a desk source) polluted any view meant
to show "what has actually been entered here."

A visitor-facing feedback page (`feedback.html`) was built and discarded the
same day: the desk page already is the entry surface, and a second entry page
added an audience without adding a capability.

## Decision

The desk becomes its own channel end to end:

- **Its own database** — `Ingest:LiveDbPath` (default `data/desk-live.db`),
  validated at startup to differ from `Ingest:DbPath`.
- **Its own pipeline** — keyed `"live"` DI clones (`Channels.Live`) of
  `FeedbackStore`, `ReportCache`, `IngestService`, `ReportService`. The
  `LlmGate` and chat clients stay shared: GPU containment is global, channels
  are not allowed to starve each other.
- **Its own endpoints** — `POST /live/feedback` (the desk UI's save),
  `GET /live/feedback`, `GET /live/report`. The handler bodies are shared
  static functions with the `/feedback` + `/report` endpoints so the two
  channels' validation and failure semantics cannot drift. `/live/report`
  never persists a snapshot — the shared-link fallback belongs to the demo.
- **Its own view** — a segment at the bottom of `desk.html` rendering
  `GET /live/report`: theme narratives (the grounded short summary) and each
  theme's source items (the categorized list), refreshed on load, after every
  save, and manually.
- **Correction telemetry follows the desk** — `CorrectionTelemetryService`
  reads the live store; pointed at the main store it would read permanent
  zeroes once desk saves moved.

The demo view additionally gets a stable explicit address: publish copies
`index.html` to `demo.html` as well, and the SWA fallback excludes it.

## Consequences

- The demo dataset and real desk entries can no longer contaminate each other,
  by construction rather than by filter — the seeded corpus's simulated `desk`
  and `web_form` items stay demo-only, and a mid-presentation test entry can
  never become fake evidence.
- **The demo's centerpiece beat moves.** "Save at the desk → the entry joins
  the dairy theme on the management view" no longer happens; the live-update
  moment now plays out inside the desk segment itself. The demo script's
  minute 2–4 changes accordingly. What is lost: the cross-view moment on the
  big screen. What is gained: the live loop is demonstrable end to end on one
  page, over provably real data only.
- Each fresh desk save triggers a fresh synthesis on the next segment load
  (cache invalidation, as on the main report) — the ~20–40 s LLM wait is now
  visible at the desk. Acceptable: it is the honest cost of the live loop, and
  the segment shows a loading state.
- Two databases to reset instead of one when preparing a demo. `feedctl data
  <mode>` wipes both and its post-wipe emptiness guard probes both channels; a
  manual reset must delete `data/desk-live.db` alongside the main database.
