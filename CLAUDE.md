# Retail Feedback Intelligence Demo — Claude Code spell

A work-sample demo for Finnish retail employers (S-ryhmä / Kesko context): a
feedback-intelligence system that ingests customer feedback from multiple
channels, uses an LLM where — and only where — rules cannot do the job, and gives
store management a grounded, live situational view. The demo's design story
matters as much as the code: it survived four rounds of "why does this need AI at
all" scrutiny, and AI remains only in the two places free-form language genuinely
cannot be rule-coded: structuring messy human input, and reading themes/trends
out of free text at scale.

TARGET: demoable end-to-end in well under two weeks, live-runnable in an
interview, with a static snapshot mode so a shared link never shows a dead page.

## Stack and topology (decided, do not re-litigate)

- Backend: C# / .NET (ASP.NET Core minimal API). This is a deliberate
  ecosystem-signal choice for Microsoft-shop employers, accepted at the cost of
  the developer being newer to .NET AI integration — which is why the build order
  below is RISK-FIRST, not familiarity-first.
- LLM serving: local Ollama on the developer's machine (RTX 3080 Ti). Public
  reachability via Tailscale Funnel.
- LLM abstraction is MANDATORY from day one: no code calls Ollama directly.
  Everything goes through a single interface (either wrap
  Microsoft.Extensions.AI's IChatClient or define a thin ILlmClient). Provider
  and model are config values. The production story this enables — "switching to
  Azure OpenAI is a config change plus an eval run" — is part of what the demo
  sells. Structuring and synthesis must be independently configurable to
  different models.
- Models: Poro-2-8B is the DEFAULT for Finnish synthesis (chosen by a published
  30-round blind test, 26/30 firsts). The STRUCTURING model is NOT assumed — it
  is chosen by a small eval in Phase 0 (Poro vs qwen3:8b), because structuring
  is instruction-following/JSON-discipline work, which the blind test did not
  measure, and prior containment data suggests qwen3 may follow format
  instructions more reliably. Model choice must be a measurement, not an opinion.
- Frontend: responsive browser UI, deployed to Azure Static Web Apps (free tier).
  Talks to the Funnel endpoint. Includes a SNAPSHOT mode: the latest generated
  report is persisted (JSON + rendered view) and served statically when the
  backend is unreachable — a shared link must never show a dead page.
- Storage: SQLite or PostgreSQL, one feedback table with a JSON structuring
  column. Do NOT build a normalized category hierarchy — structure is the AI's
  OUTPUT, not an input form's requirement.
- Data: 100% synthetic. No scraped reviews, no real personal data — this is a
  deliberate, documented GDPR decision (see Phase 1). Corpus is committed to the
  repo.

## Global rules

- Work in small, single-concern commits. Propose a plan per phase and wait for
  approval before large changes.
- Config over hardcoding: model names, provider, thresholds, alert keyword lists,
  time windows are config values validated at startup.
- Grounding is non-negotiable: every claim in the management view must be
  traceable to specific feedback items (IDs), clickable open in the UI. The
  system must never present a theme or trend it cannot source. If the LLM asserts
  something ungrounded, the pipeline drops it and logs the drop.
- Finnish output for user-facing text (management view synthesis, desk-entry
  interpretation). Code, logs, and internal docs in English.
- No scope creep. Explicitly OUT: task management (assignments, completions),
  customer-reply generation, user accounts/auth, native mobile apps, speech
  input, real channel integrations. These are interview talking points, not code.

## Environment notes (recorded 2026-07-03)

- Ollama runs INSIDE the developer's other, live RAG solution (Docker compose
  project `mikkonumminendev`, container `mikkonumminendev-ollama-1`, models in
  volume `mikkonumminendev_ollama`, GPU passthrough enabled, NO published host
  ports). That stack must NOT be modified or restarted by this project.
- GPU is shared: before any LLM test run, ANNOUNCE it so the developer can shut
  down the running RAG stack first. Never assume the GPU or Ollama is free.
- Models already pulled in that volume:
  `hf.co/mradermacher/Llama-Poro-2-8B-Instruct-GGUF:Q4_K_M` and `qwen3:8b`.

## PHASE 0 — Risk first: prove the unknowns (days 1–2)

The only genuinely unknown parts are (a) .NET ↔ Ollama integration and (b) which
model holds JSON discipline. Prove both before writing anything familiar.

1. Minimal .NET console/API spike: through the LLM abstraction, call local
   Ollama, get a completion back. Wire both Poro-2-8B and qwen3:8b as selectable
   configs.
2. Structuring eval: take ~20 messy Finnish feedback texts (hand-written by me —
   ask me for them, do not invent them yourself), define the target JSON schema
   (department, theme, severity, type, language kept as-is), and run both models
   over them. Measure: valid-JSON rate, schema adherence, classification
   sensibility (I judge sensibility; print results side by side for me).
3. Report the numbers. I pick the structuring model; it becomes config. Poro
   stays the synthesis default.
4. CHECKPOINT: if .NET ↔ Ollama integration is still not working end-to-end by
   end of day 2, STOP and tell me — the fallback decision (Python core) is mine
   to make, and I need to know within 48 hours, not at the end of week one.

Acceptance: a spike that calls Ollama from .NET through the abstraction; a
printed eval table for the structuring choice; models set in config.

## PHASE 1 — Seeded demo-data generator (the corpus exists before the analysis)

The demo lives or dies on believable data with findable stories in it. Raw
randomness produces runs with no story (or hallucinated trends); a static
dataset looks staged. The generator is layered and seeded.

- Core corpus: hand-written by me (the domain expert) — realistic Finnish retail
  feedback: dialects, typos, the customer who tells their life story first, the
  one who is angry about the wrong thing, terse desk shorthand ("asiakas sano et
  maitokaapis oli vanhoi purkkei"). Ask me for this corpus; scaffold the format
  (JSON lines with source/text/timestamp) and I will fill it. Do NOT scrape
  Google reviews or any real feedback — real reviews contain personal data, and
  "synthetic but expert-calibrated, because real data would be a GDPR problem"
  is a documented design decision this demo deliberately showcases. Record that
  decision in the README/design notes.
- LLM multiplication OFFLINE: at generation time (not demo time), use the local
  LLM to produce variations of the core corpus (rephrasings, new dates, surface
  noise) so the corpus scales to a few hundred items. The generated corpus is
  committed; the analysis meets it cold. (Same arm-isolation principle as my
  measurement work: the analyzer must not be grading its own just-generated
  text live.)
- Layered composition, seeded RNG (seed is a CLI/config parameter):
  - BASE NOISE: one-off feedback across themes, no repetition — realistic mass.
  - PLANTED STORIES (guaranteed present in every generated set, surface-varied
    by seed): (1) a repeating freshness/dairy signal from multiple channels
    across ~2 weeks, worsening; (2) a safety complaint containing NO alert
    keywords (detectable only by understanding, e.g. a collapsed deck built
    from purchased materials); (3) a slow-burn availability trend on one
    department. These are the demo's ground truth — the report must find them.
  - SURFACE VARIATION: wording, dates, ordering shuffle between seeds; stories
    remain findable.
- Same seed → same scenario (rehearsable demo); new seed → new-looking but
  equally story-bearing dataset.
- The generator feeds data through the public ingest endpoint (Phase 2), not by
  writing to the DB directly — the demo pipeline is exercised end to end.

Acceptance: `generate --seed 42` produces a committed corpus; two different
seeds produce visibly different but equally story-bearing sets; planted stories
enumerated in a ground-truth file (this doubles as the eval fixture).

## PHASE 2 — Ingest pipeline (one endpoint, four sources)

- One ingest endpoint: `POST /feedback` with `{ source, text, timestamp }`.
  "Channels" are source values (google_review, email, web_form, desk) — NOT four
  integrations.
- On ingest, the structuring model produces the JSON structure (department,
  theme, severity, type). Store raw text + structure + source + timestamp +
  a correction-audit field.
- Deterministic alert layer runs FIRST, before and independent of the LLM:
  configurable keyword/pattern list (payment problems, injury/safety vocabulary,
  legal-threat markers) flags items instantly. Cheap, never sleeps, never
  hallucinates. The LLM layer may ADD alerts (the no-keyword safety case) but
  can never remove a deterministic one.
- Validation: reject oversized input, cap lengths — same containment hygiene as
  my RAG.

Acceptance: generator can push the full corpus through the endpoint; each item
lands structured; deterministic alerts fire on the known keyword cases.

## PHASE 3 — Desk-entry UI (the demo's most original moment)

The richest feedback channel in retail is spoken at the counter and dies at
shift's end because logging is heavy. This kills the friction: AI belongs at the
INPUT side here — structuring so the human doesn't have to.

- One text field, mobile-friendly (responsive CSS, not a separate app). Staff
  types one sentence as it comes ("asiakas sano et osa maidoist oli vanhoja").
- The structuring model's interpretation is shown back (department, theme,
  severity) BEFORE saving. One tap to accept, or correct any field — human in
  the loop; corrections are stored in the audit field.
- Accepted entry goes through the same ingest pipeline, source=desk.

Acceptance: a messy dialect one-liner round-trips: typed → interpreted →
corrected → saved → visible in the archive with source=desk.

## PHASE 4 — Analysis + grounded management view

Two-layer analysis over a selectable time window (day / week / custom):

- ALERTS (top of view): deterministic flags + LLM-added alert nominations, each
  with the source item(s) one click away.
- THEMES & TRENDS: LLM synthesis over the window's structured items — grouped
  themes, counts, direction ("hyllysaatavuus, osasto 4, kolmas viikko,
  paheneva"), written in natural Finnish (Poro). EVERY claim carries the IDs of
  the feedback items behind it; the UI opens them on click. Ungroundable claims
  are dropped and logged, never shown.
- LIVE moment: when a new desk entry is accepted, the view updates — the new
  signal joins its theme or founds a new one. This single moment shows the whole
  loop working and is the centerpiece of a live demo; make it smooth.
- SNAPSHOT: every generated report persists (JSON + static render). Frontend
  serves the latest snapshot when the backend is down.

Acceptance (this is the demo's eval, run against the Phase 1 ground-truth
file): on a fresh seeded corpus, the report surfaces all three planted stories;
the no-keyword safety case appears as an alert; every displayed claim opens to
real source items; a live desk entry updates the view.

## PHASE 5 — Deploy + demo readiness

- Frontend to Azure Static Web Apps; backend reachable via Tailscale Funnel;
  snapshot mode verified with the backend deliberately stopped.
- README/design notes covering, briefly and honestly: why AI is only in two
  places (the four-round elimination story); the synthetic-data GDPR decision;
  the model choices as measurements (blind test → Poro for synthesis; Phase 0
  eval → structuring model); the provider abstraction and what "switch to Azure
  OpenAI" actually costs (config + re-running the eval — prompts are not
  perfectly portable across models, quality must be re-measured, and moving
  from local to hosted is a data-residency decision that belongs to the
  customer).
- A rehearsal script: seed 42, the exact click-path of a 5-minute live demo,
  including the live desk-entry moment.

Acceptance: shared link shows a live report (or snapshot if backend down); the
5-minute path rehearsed end to end on a fresh seed.

## Build order summary

0 risk spike + structuring eval (48h checkpoint) → 1 seeded generator (my
corpus) → 2 ingest + deterministic alerts → 3 desk entry with human-in-the-loop
→ 4 analysis + grounded view + live update + snapshot → 5 deploy + rehearsal.

Phases 2–4 are the familiar bulk; they come AFTER the unknowns are retired.
