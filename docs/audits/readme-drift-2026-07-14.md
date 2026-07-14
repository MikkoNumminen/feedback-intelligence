# README drift report — 2026-07-14

## Summary
- README audited: `README.md`
- Trigger: after the ADR-0028/0029/0030/0031 branch (taxonomy 16→30, sentiment
  indicator, theme-format + optional sentiment structuring field).
- Total drifts: **0 actionable** (0 stale, 0 broken refs; 2 borderline items left
  untouched by calibration rules).
- Rewrites applied: **0**. Voice profile: not extracted (no rewrites needed).
- Verdict: **no content update required.** The README operates above the altitude
  of this branch's changes.

## Findings by axis

### 1. file-structure-drift — none
Every path the README references still exists: `docs/domains.md`,
`docs/decisions/`, `docs/architecture.md`, `docs/schema.md`,
`docs/domain/retail.md`, `data/corpus/README.md`, `deploy/snapshot/`,
`docs/mock-data-register.md`, `docs/TODO.md`, `docs/prs/`, `AGENTS.md`,
`src/FeedbackIntelligence.Api`, `tools/FeedbackIntelligence.Generator`,
`tools/FeedbackIntelligence.StructuringEval`. No stale or renamed paths.

### 2. dependency-drift — none
Stack claims (.NET 8, Ollama, `Microsoft.Extensions.AI.IChatClient`) unchanged
this branch.

### 3. skill-drift — N/A
The README enumerates no skills list (this is a .NET app, not a skills repo).

### 4. feature-drift — 2 borderline, both LEFT UNTOUCHED
- **Structuring tuple (line 35):** the README illustrates structuring output as
  `{department, theme, severity, type, language}` (5 fields). The schema gained an
  OPTIONAL 6th `sentiment` field (ADR-0031) — but Poro-2-8B does not emit it, so
  the 5-field illustration is still **accurate for what the model actually
  produces**. Not a completeness claim ("exactly these five" language is in
  `docs/schema.md`, which WAS updated, not the README). Left as-is: adding a field
  the model never emits would make the README less accurate about real behavior.
- **Sentiment feature not mentioned** in the management-view description (lines
  8–12: "alerts on top, themes and trends below"). This is a *missing addition*,
  not a stale claim. Calibration rule: do not ghostwrite new feature sections for
  existing-but-undocumented features unless the README claims completeness — it
  does not. Left as-is.

### 5. status-drift — none
- "hand-written core (27 texts, evidential)" (line 145): corpus untouched this
  branch — still accurate.
- CI badge: auto-updating.
- No version / test-count / dependency-count claims to re-verify.
- The 30-round blind-test claim (lines 62–63) is unaffected.

## Recommendation
No drift-driven edit is warranted. **Separately** — not drift, an editorial choice
— if the owner wants the README (a work-sample showcase) to advertise the new
**positive/negative sentiment indicator** and the **fuller retail taxonomy**, that
is deliberate new-section authoring for the human to draft, outside this skill's
maintenance scope. Flagged, not written.
