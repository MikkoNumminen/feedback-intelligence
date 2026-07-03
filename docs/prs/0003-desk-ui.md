# PR #3 — feat(desk): Phase 3 desk-entry UI

Branch `feat/desk-ui` → `main` (local; no remote exists).

## What

`wwwroot/desk.html` (Finnish, mobile-first, vanilla JS, served by the API):
one text field → `/interpret` shows the model's reading BEFORE saving →
tap-accept or correct any field (corrections highlighted) → saved through the
same `POST /feedback` pipeline with source=desk and the correction audit.
Plus `GET /schema` so UI dropdowns come from `StructuringSchema`, never a copy.

## Review

One combined subagent angle (correctness + spec) + inline pass.

## Findings → resolutions (all fixed)

1. Stale `language` leaked from the previous entry onto the manual-entry path
   → language is set per shown form; entry state fully reset between entries.
2. Empty dropdowns after a failed `/schema` load could silently produce
   rejected saves → interpret is disabled until the schema loads (auto-retry),
   and save guards against empty selections.
3. Manual entry after a failed model interpretation was indistinguishable from
   a zero-correction model success, silently corrupting the correction
   telemetry that replaced the cancelled eval → `modelInterpretationFailed`
   marker added through the whole stack (contract → store column → UI).
4. The textarea stayed editable after interpretation, so stored text could
   diverge from what the model saw → text is captured and locked at interpret
   time; only that text is ever saved.
5. "Yhteysvirhe — palaute EI tallentunut" asserted certainty the client cannot
   have, and retries could double-store → client-generated entry id makes
   retries idempotent (server 409 = already saved = success), message now says
   the save is uncertain and safe to retry.
6. Hard-rule violation: no Phase 3 status in CLAUDE.md and `/schema` missing
   from the Phase 2 endpoint inventory → both updated.

## Verification

Build clean, 55/55 tests green; `/schema` and `desk.html` smoke-tested over
HTTP (schema returns all 14 departments; page serves 200).
