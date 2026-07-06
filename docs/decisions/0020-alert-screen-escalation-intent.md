# ADR-0020 — The alert screen also flags intent to escalate to authorities / legal action

- **Status:** Accepted (2026-07-06)
- **Deciders:** Mikko
- **Follows:** [ADR-0015](0015-poro-real-corpus-tuning.md) (per-item safety
  screen), [ADR-0009](0009-grounding-is-structural.md) (deterministic layer +
  grounded LLM layer)

## Context

The deterministic keyword layer already flags explicit legal-threat vocabulary
(`domains/retail/alert-keywords.json` `legal_threat`: `viranomais`,
`terveystarkasta`, `ruokavirasto`, `lakimies`, `oikeustoim`, `haastan`,
`korvausvaatimus`, …). Mikko's requirement is broader: **"keywords OR intent to
call the officials — it needs to flag."** A message that *implies* going to the
authorities without using a listed word (e.g. *"En jätä tätä tähän, otan yhteyttä
oikeille tahoille ja vien asian eteenpäin"*) slipped through — the deterministic
layer needs a keyword, and the LLM alert screen (ADR-0015) was scoped to
**physical safety only**, so keyword-less escalation intent had no detector.

This mirrors the no-keyword safety case exactly: rules catch the explicit form,
AI understanding is needed for the implicit form.

## Decision

**Broaden the per-item alert screen** (`domains/retail/prompts/alert-verify-v0.txt`)
to flag, in addition to physical safety, a customer's **clear intent to take the
matter to an authority or to court** — even without the exact keyword. Ordinary
anger, cursing, threats to switch stores or "spread the word" remain explicitly
**not** alerts; the prompt names those exclusions to hold precision.

No code change: the screen already runs per keyword-less non-praise item, its
whole-word `kyllä`/`ei` parse and fail-closed behaviour (ADR-0015, ADR-0018) are
unchanged, and the deterministic keyword legal-threats still fire first and are
excluded from the screen. This is purely a widening of what the screen's prompt
counts as an alert. The alert-verify prompt is a **retail-domain** asset; another
domain defines its own alert semantics.

## Consequences

- **Tuned, not asserted (temperature 0, seed-42 + synthetic probes).** The LLM
  screen reliably flags the no-keyword safety item; keyword-less escalation-intent
  probes flag, while ordinary anger, store-switching, and plain **service** refund
  / hyvitys demands do not. The escalation exemplars name explicit venues
  (`kuluttajariitalautakunta`, `käräjäoikeus`, `lakimies`, `terveystarkastaja`) —
  deliberately NOT "korvausvaatimus", so a routine compensation demand is not read
  as escalation (review finding). The deterministic legal-threat item
  (`viranomais`) alerts via **layer 1** and never reaches the screen (candidate
  filter `Alerts.Count == 0`, `ReportService.cs`), so the screen adds no false
  volume — the report-time SCREEN output is the keyword-less items only.
- **Named residual:** borderline consumable-health items (e.g. a spoiled-product
  complaint that already caused symptoms) are gray-area 8B judgments and may flag
  or not run-to-run — a real signal when caught, never a flood. The clear cases
  (structural safety, explicit escalation intent) are stable.
- **Residual, named honestly:** this is an 8B model's judgment on implicit intent
  — it will miss some phrasings and could over/under-flag on adversarial input.
  The deterministic legal-threat keywords remain the reliable backstop; the screen
  adds recall for the implicit case. (The hardening of this exact prompt against
  injection — a crafted body forcing `kyllä` — is handled separately in the
  injection-defense work, ADR-0021.)
- Correction telemetry (desk audit) remains the ongoing drift measure.
