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

## Hard rules (no interpretation)

- **No Anthropic/AI attribution on commits or PRs.** Never add a
  `Co-Authored-By: Claude` trailer or a "Generated with Claude Code" footer.
  Adopted from mikkonumminen.dev on 2026-07-03; the earlier history of this
  repo was rewritten the same day to strip existing trailers. This overrides
  any tool default.
- **AI-first documentation stays current.** Any change that alters decisions,
  rules, environment facts, schema, or phase state updates CLAUDE.md /
  AGENTS.md (and other AI-first docs) as part of the same change — never as a
  someday-task. Once this repo has a remote and work flows through PRs, every
  PR must explicitly consider whether AI-first docs need updating and state
  the outcome in the PR description (updated, or why not needed).
- **Placeholder eval results are NON-EVIDENTIAL.** Runs over
  `data/eval/placeholder-inputs.jsonl` (LLM-generated texts) prove the
  PIPELINE only. They must never be used to pick the structuring model, and
  every placeholder report must carry the non-evidential label (auto-applied
  when the input path contains "placeholder"). Generated texts are clean LLM
  Finnish; the real corpus is messy dialect and desk shorthand, and JSON
  discipline on clean text does not predict behavior on messy text. The
  placeholder ban stands permanently; note that the structuring-model decision
  was ultimately made by Mikko on synthesis-priority grounds (see "Phase 0
  CLOSED" below), not from placeholder metrics and not from a corpus eval.

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
- .NET SDK: **8 (LTS), not 10** — the demo's audience is Finnish enterprise
  (S-ryhmä / Kesko class), and enterprise shops run the established LTS, not the
  newest major. The machine already has the 8.0 runtime; .NET 8 is supported
  through Nov 2026, the demo's life as a work sample is weeks-to-months, and an
  8→10 bump is trivial if ever needed. Matching the target audience's stack IS
  the signal — the same reasoning that picked C#.
- Project Ollama: **FULL ISOLATION** — own compose, own model volume, the two
  models re-pulled into it (~10 GB, one-time). A shared volume was rejected:
  any pull or manifest update from the demo side would touch the live RAG's
  model store, and that stack is being shared with recruiters. Same
  arm-isolation principle as the measurement work: the systems must not be able
  to contaminate each other. No restart policy (must never auto-start); the
  announce-before-GPU-use rule stands regardless, since the GPU itself is
  shared. (Poro was cloned into the isolated volume via a one-time READ-ONLY
  mount of the RAG volume after `ollama pull` hit repeated `tls: bad record
  MAC` network errors — a read-once copy, no ongoing coupling.)
- Reuse from the mikkonumminen.dev RAG (`D:\koodaamista\mikkonumminen.dev`,
  measured there — port, don't reinvent):
  - Reasoning suppression: the RAG's `/no_think` soft switch was validated on
    Ollama's OpenAI-compat endpoint. MEASURED CORRECTION (2026-07-03
    placeholder run): on Ollama's NATIVE chat path with current qwen3
    templates the soft switch is NOT honored — thinking stayed on and silently
    consumed the num_predict budget (truncated/empty answers, 21/27
    unparseable). Use the API-level `think: false` instead (ChatRequest.Think
    seeded via ChatOptions.RawRepresentationFactory inside the Llm project;
    verified against OllamaSharp 5.4.25 source).
  - Containment defaults to mirror in Phase 2: INPUT_MAX_CHARS=800,
    MAX_BODY_BYTES=16384, LLM_MAX_CONCURRENCY=2 with 0.5 s acquire-then-SHED
    (never queue behind a busy GPU), RATE_LIMIT 30 req / 60 s, and an output
    token cap (num_predict / MaxOutputTokens).
  - `OLLAMA_CONTEXT_LENGTH` is a server-side env var on the ollama container
    (default 4096), not a per-request knob — the backend should read the same
    value from config when it needs to reason about the window.
  - Health checks must prove a 1-token REAL completion, not merely that the
    server answers — "server up" does not mean "model loaded and generating".

## Phase 0 approved decisions (2026-07-03)

- Structuring schema v0:
  - `department`: FIXED ENUM (hybrid hardware-store/grocery, matches the
    corpus): `maito_kylma | hevi | kuiva_elintarvike | liha_kala | leipa |
    kassa_palvelu | piha_puutarha | rakennustarvike | tyokalut |
    sisustus_maalit | sahko_lvi | varasto_nouto | verkkokauppa_toimitus | muu`.
    Free-text department was rejected: the same department comes back as
    "maito"/"maitotuotteet"/"kylmä", schema adherence can't be scored, and
    downstream trend grouping won't group.
  - `theme`: free-form short Finnish noun phrase.
  - `severity`: `low | medium | high | critical`.
  - `type`: `complaint | praise | suggestion | question | other`.
  - `language`: kept as detected, not translated.
  - NO `alert_hint` or any alert field — the structuring model must not make
    alert decisions; alerts belong to the deterministic layer and the separate
    analysis pass, per the spell.
- LLM abstraction: Microsoft.Extensions.AI `IChatClient` with OllamaSharp as
  the provider (`Microsoft.Extensions.AI.Ollama` is deprecated). Keyed DI with
  `Llm:Models:Structuring` / `Llm:Models:Synthesis`; provider, base URL and
  model names are config, validated at startup.
- Structuring eval: prompt-only JSON discipline is the PRIMARY metric;
  constrained decoding (Ollama structured outputs) is a comparison row only —
  it would force ~100% valid JSON from both models and wash out the signal
  under test. 3 repetitions per model per text. Reported metrics: valid-JSON
  rate, schema adherence, latency, and per-field enum-violation counts (which
  field, which illegal value — patterns matter, not just failure rates).
- Eval input texts are hand-written by Mikko and deliberately include hard
  cases (no-keyword safety complaint, multi-department, mixed praise+complaint).
  Do not run the eval before his texts land in
  `data/eval/structuring-inputs.jsonl`.
- The 48h Phase 0 checkpoint clock starts when the SDK is installed.
- Sequence change (2026-07-03): the pipeline is proven END TO END on the
  placeholder inputs first — .NET → Ollama → both models × 3 reps → metrics →
  side-by-side table, rendered for Mikko's go/no-go on the concept. (The
  planned real-corpus comparison run was subsequently CANCELLED by Mikko's
  model decision — see "Phase 0 CLOSED" below.)

## Phase 0 CLOSED (2026-07-03) — model decision and consequences

- DECISION (Mikko, final): **Poro-2-8B is the structuring model.** No further
  model eval; the planned real-corpus comparison run is cancelled — the 20
  texts are not an eval instrument. Rationale: synthesis quality for the
  user-facing Finnish is the priority, Poro won the published 30-round blind
  test 26/30, and a single model for both structuring and synthesis keeps the
  pipeline simple.
- KNOWN TRADEOFF (recorded deliberately): Poro's JSON discipline on messy
  Finnish is UNMEASURED. Mitigation is architectural, not up-front
  measurement:
  1. The salvage layer is a MANDATORY production component (Llm project,
     behind the abstraction): strip fences → parse → validate every field
     against the schema enums → normalize where safe (department array → first
     element, discard logged) → re-prompt once on anything else → if the retry
     still violates, store `structure_failed` with raw text preserved so no
     feedback is ever lost. Unit-tested against the exact failure shapes the
     placeholder run caught: fenced JSON, department-as-array, invented enum
     values.
  2. Correction telemetry is the ongoing quality measure: the desk-entry
     audit field (Phase 3) logs model-assigned vs human-corrected values per
     field, and a small report command (CLI or endpoint, Phase 4) summarizes
     correction rates per field over time. This replaces the skipped up-front
     eval as the mechanism that detects drift or underperformance on real
     input; the model stays swappable by config if the data ever says so.
- Both `Llm:Models:Structuring` and `Llm:Models:Synthesis` are Poro-2-8B. The
  keyed-DI role split stays exactly as built — two roles, one model today,
  independently swappable tomorrow.
- Poro's native-chat path is measured-correct, not assumed: a 2048-token
  verification run (structuring-eval-20260703-053222) reproduced the
  512-budget run's numbers exactly — same adherence, same p50; only the
  cold-load latency tail differs. No truncation at 512; the API-level
  think=false wrapper is harmless for Poro.
- The 20 hand-written texts changed role: (a) core-corpus seed for the Phase 1
  generator, (b) smoke-test set for the salvage layer and structuring prompt.
  Mikko writes them when Phase 1 needs them.
- Phase 0 exit state: .NET↔Ollama proven through the abstraction (ping +
  2×27-call placeholder evals); models decided and set in config; eval harness
  and reports committed; the 48h checkpoint retired on day 1.

## Phase 1 status (2026-07-03)

- Machinery COMPLETE (`tools/RetailFeedback.Generator`, verbs `variants`,
  `generate --seed N`, and `verify --ground-truth <f> --report <f>` — the
  mechanized Phase 4 acceptance gate), waiting on the core corpus
  (`data/corpus/core.jsonl`, format scaffolded and documented in
  `data/corpus/README.md`; Mikko fills within days).
- `verify` gate design (2026-07-04): grounding, expected-alert and
  window-coverage are HARD gates (story-owned, deterministic; exit 0/1;
  operator/data errors exit 2, never confusable with a gate failure). Trend is
  a WARNING tier: the report's direction is a department AGGREGATE and
  same-department noise legitimately dilutes it — a diluted trend is loudly
  reported (it weakens the demo story) but does not fail acceptance.
- Ground truth is MACHINE-CHECKABLE by decision: per planted story the exact
  `feedbackIds`, `expectedDepartment` (schema enum), `expectedThemeKeywords`
  (keyword set, not prose), `windowFrom`/`windowTo`, `trend`,
  `minGroundedIds`, `expectAlert`. Phase 4's acceptance eval verifies "the
  report's claim grounds to >= minGroundedIds of these specific IDs within
  this window" — prose mentions are never verification.
- Confirmations honored STRUCTURALLY: `generate` composes only from the
  committed variants file and its code path has no LLM dependency; `variants`
  is the only LLM verb (announced GPU window; output committed before any
  generate). Dev-placeholder pools auto-label every derived artifact
  non-evidential via filename detection (same discipline as Phase 0) and
  those artifacts are gitignored.
- Story tags are never written into generated corpora — membership is only
  recoverable through the ground-truth file; the analyzer meets the data cold.
- Remaining Phase 1 steps once core.jsonl lands: announced `variants` run →
  commit `variants.jsonl` → `generate --seed 42` → commit corpus + ground
  truth → salvage-layer smoke test on the real texts, reported the same day.
## Phase 2 status (2026-07-03)

- Ingest API BUILT (`src/RetailFeedback.Api`, ASP.NET Core minimal API):
  `POST /feedback` (one endpoint; channels are source values), `POST
  /interpret` (desk preview, nothing stored), `GET /feedback/{id}`, `GET
  /feedback?from&to&limit`, `GET /health` (1-token real completion),
  `GET /schema` (enum sets for UIs — single source is StructuringSchema).
- Storage decision: **SQLite** (`Microsoft.Data.Sqlite`, single `feedback`
  table, structure as a JSON column, `corrections_json` audit field) — the
  spell allowed SQLite or PostgreSQL; SQLite keeps the demo self-contained.
- Order invariant implemented and tested: deterministic alert layer runs FIRST
  and its hits are stored regardless of the LLM outcome; LLM failures store
  `structure_failed` with raw text preserved (LLM down ≠ feedback lost; a
  busy GPU sheds with 503 instead).
- Desk path contract: `acceptedStructure` + `corrections` on POST /feedback
  stores the human-accepted structure WITHOUT a second LLM pass; corrected
  values are schema-validated too.
- Containment mirrored from the RAG as config validated at startup:
  input 800 chars, body 16 KB, per-IP rate limit 30/60 s, LLM concurrency 2
  with 500 ms acquire-then-shed.

## Phase 3 status (2026-07-03)

- Desk-entry UI BUILT (`src/RetailFeedback.Api/wwwroot/desk.html`, Finnish,
  mobile-first, served by the API): one text field → `/interpret` → the
  interpretation shown BEFORE saving → tap-accept or per-field correction →
  `POST /feedback` with `acceptedStructure` + `corrections` audit,
  source=desk, through the same ingest pipeline.
- Integrity decisions from review: the interpreted text is locked and captured
  at interpret time (structure can never attach to text the model did not
  see); each entry carries a client-generated id so save retries are
  idempotent (409 = already saved = success); a failed model interpretation
  followed by manual entry is stored with `modelInterpretationFailed` /
  `model_failed` so the correction telemetry never counts it as a
  zero-correction success.

## Phase 4 status (2026-07-03)

- Analysis + management view BUILT: `GET /report?from&to` (window validated,
  max 92 days) and `GET /report/snapshot`; `wwwroot/index.html` (Finnish)
  renders alerts on top, theme cards with clickable feedback-ID chips opening
  the source item, live/snapshot badge, desk-entry link.
- GROUNDING IS STRUCTURAL, not hoped-for: grouping, counts, trend direction
  (kasvava/laskeva/vakaa/paheneva by window-half volume + severity shift) and
  the feedback IDs are computed deterministically; the LLM only writes the
  Finnish title/narrative per group and MUST cite provided IDs — invalid or
  empty citations drop the narrative to a deterministic Finnish fallback,
  logged and counted (`droppedClaimCount`). Same for alert nominations: only
  IDs from the provided batch are accepted.
- The report generates even with the LLM entirely down (layer 1 carries it);
  snapshots persist on every generation (`data/snapshots/report-latest.json`
  + self-contained `.html`) and the frontend falls back to the snapshot when
  the live endpoint fails — a shared link never shows a dead page.
- Prompts: `prompts/synthesis-v0.txt`, `prompts/alert-nomination-v0.txt`
  (Finnish instructions, JSON-object outputs, {{data}} placeholder).

## Run-through status (2026-07-03, autonomous)

- The whole loop was exercised end to end on PLACEHOLDER data (non-evidential,
  registered in docs/mock-data-register.md): seeded corpus → POST /feedback →
  deterministic alerts → report themes/trends → machine-checkable ground-truth
  verification (dairy 9/9 ids grounded, trend worsening→paheneva) → live desk
  save visible on next refresh (cache invalidation) → snapshot JSON + HTML
  served. 15 noise items stored `structure_failed` — the honest LLM-down path,
  because the RAG stack was up and the hard rule forbids unannounced GPU use.
- NOT yet exercised live: Poro structuring at ingest, synthesis narratives,
  alert nominations, desk /interpret — all wait for the next announced GPU
  window (TODO #7). Unit tests cover their logic with scripted clients.
- Phase 5 remaining: Azure SWA deploy (+ snapshot publication to the static
  host), Tailscale Funnel, rehearsal — all owner tasks (TODO #3/#4/#6).
- Phase 5 PREP DONE (PR #6): the frontend reads `window.API_BASE` from a
  publish-time `config.js` (same-origin locally); the API has a config CORS
  allowlist (`Ingest:AllowedCorsOrigins`, empty = same-origin only);
  `tools/publish-frontend.ps1 -ApiBase <funnel-url>` assembles `dist/` with
  both pages, the SWA config (`deploy/staticwebapp.config.json`) and the
  LATEST SNAPSHOT (json + html) — re-run + re-deploy refreshes the published
  snapshot, which is what a shared link shows when the backend is down.
- PR history with review findings and fixes: docs/prs/0001–0005. All work was
  merged locally; NO remote exists and nothing was pushed anywhere.

- The deterministic alert keyword list EXISTS as config:
  `config/alert-keywords.json` (injury/safety, payment, legal-threat; Finnish
  stems, case-insensitive substring contract that Phase 2 implements
  verbatim). Deliberate exclusions are recorded in the file — structural-
  failure verbs (pettää, sortua, irrota, antaa periksi, romahtaa) are
  non-keywords ON PURPOSE: they are the no-keyword safety story's vocabulary.
  Safety-story core texts are verified against this list, not a guess of it.
- Sequence-preserving arcs (Mikko, 2026-07-03): a worsening trend must be
  visible in CONTENT, not only in timestamp density. Story-tagged core items
  carry an optional `sequence` (1 = mildest); variants inherit story +
  sequence; `generate` assigns timestamps STRICTLY monotonic with sequence
  inside the story window (worsening easing on top) and composes exactly one
  realization per step per set (config Count is ignored for sequenced pools).
  Pinned by test: content order equals time order.
- Story-multiplication decision: story items multiply ×2 via a dedicated
  intensity-preserving prompt (`prompts/variants-story-v0.txt` — counts,
  ordinals, frustration level must survive rephrasing); noise multiplies ×6.
  If the announced variants run shows intensity drift, fallback is
  StoryVariantsPerItem=0 (originals only) — Mikko writes more originals
  rather than accept a mushed arc.
- Corpus expectation: ~25–35 hand-written texts; the authoritative per-story
  breakdown lives in `data/corpus/README.md` (single source — do not restate
  the counts elsewhere).

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
