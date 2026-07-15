# ADR-0033 — Alerts are retail-operational; non-substantive content in a collapsed moderation view

- **Status:** Accepted (2026-07-14)
- **Deciders:** Mikko
- **Amends:** [ADR-0027](0027-racism-recognition-alert-lexicon.md) (which widened
  the alert channel to carry conduct — this narrows it back to operational)
- **Follows:** [ADR-0032](0032-unrated-nonsubstantive-categories.md) (unrated
  demoted categories), [ADR-0026](0026-categories-emergent-topics-live-summary.md)

## Context

Opening the desk greeted the manager with a wall of racist slurs in **Hälytykset
(Alerts)** — because racist keywords trigger the deterministic alert layer, and
ADR-0027 had deliberately widened the alert channel to carry conduct as well as
safety. The owner's call: **a `Hälytys` is retail-operational** — spoiled/outdated
goods, safety/injury, payment, legal-threat, the severe things a manager acts on
now. Racist/abusive speech is **conduct/moderation, not an operational alert**;
it should be recognized but never lead the page.

## Decision

1. **Alerts are operational-only** (`ReportService`, one filter on the
   deterministic alert list). An alert is dropped from `report.Alerts` iff it has
   no `LlmReason` AND every one of its `DeterministicHits[*].Category` is a
   **demoted** category (i.e. pure `rasismi`). Consequences:
   - A comment that is *also* a genuine hazard (a `rasismi` hit **and** an
     `injury_safety`/`payment`/`legal_threat` hit) keeps its operational alert.
   - Every LLM safety nomination is kept (it has no category and is a safety
     screen by construction).
   - **Config-driven** — reuses `DomainDescriptor.DemotedCategories`.
   - **Recognition is untouched**: the item stays classified `rasismi`, keeps its
     `⚑` per-item tag (`ReportSourceItem.AlertCategories`, independent of
     `report.Alerts`), and is counted — it just isn't an *alert*. (ADR-0027's
     "recognized, kept, named" is preserved; only the alert-channel widening is
     reversed.)

2. **A collapsed "Moderoitava sisältö (N)" disclosure at the bottom.** The unrated
   (demoted) themes — `rasismi`, `asiaton` — render inside one collapsed
   `<details>` at the foot of the desk, management, and offline-snapshot views.
   The **count is always visible** (recognized, not hidden); the text stays behind
   a click, so hostile/off-topic material never leads the page.

3. **Full separation.** The non-substantive content also leaves the main
   "Palautteet aiheittain" category chart and the entries tile — the top of the
   page reflects **real feedback only**; the moderation disclosure carries the
   conduct count. (Severity and sentiment already excluded the unrated categories
   per ADR-0032.) This includes the whole-window **Yhteenveto** (the live-summary
   `Overall`, ADR-0026): it is synthesized over the **rated** items only, so the
   lead narrative never names or rates demoted content, its severity digest and
   excerpts exclude it, and its window total and trend cover real feedback. A
   window with only demoted content yields no Yhteenveto (the moderation count
   still shows). Server-side in `ReportService`, so every view and the offline
   snapshot inherit it.

## Consequences

- A slur no longer opens the desk. It is counted in the "Moderoitava sisältö (N)"
  header and tagged `⚑ rasismi` inside — recognized, not front-loaded.
- Genuine safety on a racist comment still alerts (mixed hits are kept).
- The alert channel's meaning is now purely operational — the retail-centric
  definition. This is a deliberate reversal of ADR-0027's widening; the
  recognition ADR-0027 gave racist content lives on in the category / tag / count /
  moderation view, just not as a `Hälytys`.
- The change is a report-layer filter + a presentation partition — no model or
  prompt change, no ADR-0022 gate.
- Numbers reconcile: window total = rated feedback (chart + entries) + the
  moderation count.
