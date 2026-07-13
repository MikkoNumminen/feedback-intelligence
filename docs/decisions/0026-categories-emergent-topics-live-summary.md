# ADR-0026 — Asiaton category, emergent topics, and the desk summary view

- **Status:** Accepted (2026-07-13)
- **Deciders:** Mikko

## Context

Live testing exposed three gaps at once. First, the retail vocabulary had only
*departments*, so abusive/racist test entries landed in `muu` and the narrative
tiptoed around them — there was no right answer for the model to give. Second,
the desk segment rendered one narrative per theme group (N LLM calls per
refresh) with no overall picture, while the owner wanted the
mikkonumminen.dev-style shape: one summary on top, categories below. Third, a
page refresh after a save could show an empty segment for up to a minute — the
save invalidates the report cache, the refetch runs a full synthesis, and a
freshly loaded page had nothing to show while it ran; entries looked "gone"
although they were safely stored.

## Decision

Four moves, none of which adds a new prompt (the ADR-0022 lock is untouched —
categories and hints are injected into the locked template's placeholders):

1. **`asiaton` category** in the retail domain for abusive/inappropriate
   content, plus a domain-level mechanism it needed: optional
   **`categoryHints`** — per-category guidance rendered ONLY into the
   structuring prompt's category list, so display labels stay short while
   non-obvious categories carry an explanation to the model.
2. **Emergent topics**: the domain can name a **`catchAllCategory`**
   (retail's `muu`). The live summary splits that category into topics keyed
   on the structuring model's own free-text `theme` — the AI names the topic,
   arithmetic does the grouping. AI stays in exactly two places (ADR-0006).
3. **Live summary mode** on the report pipeline (`/live/report`): per-category
   groups stay deterministic (no per-group LLM calls), and ONE whole-window
   synthesis produces the `Overall` narrative through the same locked prompt,
   citation grounding, action bounding and deterministic fallback as any theme
   narrative. The desk renders: Yhteenveto on top, category sections below.
4. **Two-phase desk render**: the segment paints the raw categorized entry
   list instantly (`GET /live/feedback`, no LLM) on every load, then the
   summary report replaces it when synthesis completes. A refresh can never
   look empty again; every visitor always sees every stored entry.

Adapting existing data: **`POST /live/restructure`** (feedctl `restructure`)
re-runs structuring with the current vocabulary over the items that NEED it —
unstructured items, catch-all items (a new category or emergent topic may now
fit), and items whose category no longer exists. Items in a still-valid named
category are skipped: a human desk-acceptance there is a deliberate audit the
pass must not overwrite. Re-structured results are model-assigned: the A2
injection scan applies per item, and their old corrections are cleared — they
audited a structure that no longer exists and must not feed the correction
telemetry. The report cache is invalidated even if the pass aborts mid-way.

## Consequences

- Refresh cost drops from one LLM call per theme group to exactly one
  synthesis call (plus the unchanged alert screen); the desk always shows
  content within moments regardless of synthesis time.
- The catch-all's topic labels are model-authored free text grouped by exact
  (case-folded) match — near-duplicate topics ("jonot" / "jonotus") appear as
  separate sections until enough data motivates normalization. Accepted.
- `/live/restructure` is unauthenticated like every operator endpoint (the
  repo's accepted T1/T2 posture) but deliberately NOT in the public `/api`
  proxy allowlist — the static site cannot trigger GPU-burning re-passes.
- Re-structuring overwrites desk-accepted structures ONLY for items in the
  catch-all or in a removed category — there the old acceptance was made
  against a vocabulary that no longer exists, so the audit starts over.
  Human-accepted items in still-valid categories are never touched.
- `categoryHints`/`catchAllCategory` are optional domain data: the game domain
  is untouched and compiles into identical behavior.
- **Amendment (charts + adaptivity, same day):** `demotedCategories` joined the
  domain data — presentation-only ordering (retail demotes `asiaton`: hostile
  content must not lead the page). The rule deliberately lives in the domain
  descriptor and is applied by the SERVER in summary ordering, with the desk's
  instant phase mirroring it via /schema — removing either side as "apparent
  duplication" regresses hostile-content-first. `GET /live/version` (the
  report-cache epoch) is the change tick every open view polls, so the segment
  adapts to every feedback received; pages baseline the tick BEFORE requesting
  the report, so ingests landing during a long synthesis are never swallowed.
