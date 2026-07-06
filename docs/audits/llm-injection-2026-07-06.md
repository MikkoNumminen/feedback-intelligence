# LLM prompt-injection audit — 2026-07-06

> **Honest limitation, up front:** prompt injection is an unsolved research problem.
> This audit maps the LLM boundary and grades defense-in-depth — it does **not** and
> cannot prove the system safe. A clean scorecard means the layers are present and
> measurably covered, not that injection is impossible.

`pre-flight: LLM boundary confirmed (6 call-site files, 8 prompt assets, untrusted-text-in-prompt: yes). Proceeding.`

Run by `mikko-llm-injection-audit` (read-only) against the whole repo. Six
defense-in-depth layers, one auditor each. Provenance: this skill's checklist was
extracted from this repo's own injection hardening (ADR-0021), so the audit is in
part a dogfood — it re-confirms, from a fresh and independent angle, that the layers
the hardening added are actually applied at every boundary site.

## Phase 1 — injection surface map

Every place attacker-influenced text reaches a model prompt (or a manager-facing
render). All prompt splices route through the Core chokepoint
(`UntrustedText.Fence`/`Neutralize`).

| # | Site (`file:line`) | Prompt / sink | Untrusted fragment | Chokepoint |
|---|--------------------|---------------|--------------------|-----------|
| 1 | `Llm/Structuring/LlmStructuringService.cs:26` | structuring `{{text}}` | raw customer feedback (ingest + desk `/interpret`) | `UntrustedText.Fence` |
| 2 | `Api/Analysis/ReportService.cs:215` | alert-nomination `{{data}}` rows | feedback excerpt | `UntrustedText.Neutralize` |
| 3 | `Api/Analysis/ReportService.cs:265` | alert-verify `{{text}}` | feedback text | `UntrustedText.Neutralize` |
| 4 | `Api/Analysis/ReportService.cs:312` | synthesis `{{data}}` theme keys | model-derived `theme` (2nd order) | `UntrustedText.Neutralize` |
| 5 | `Api/Analysis/ReportService.cs:317` | synthesis `{{data}}` excerpt rows | feedback excerpt | `UntrustedText.Neutralize` |
| 6 | `Api/Analysis/ReportService.cs:134` | management view / snapshot (2nd-order render) | feedback text + model `reason` | HTML-encode + A3 reason guard |

Prompts audited: `prompts/structuring-v0.txt`;
`domains/{retail,game}/prompts/{synthesis,alert-nomination,alert-verify}-v0.txt`.
Governing ADRs: `docs/decisions/0021` (injection A1–A4), `0009` (grounding is
structural).

## Layer scorecard

Six layers, one independent auditor each (medium effort), told to hunt genuinely for
gaps — a splice skipping the chokepoint, an unguarded model-authored slot, a model
output driving an action. **All six PRESENT; zero findings.** A fresh angle
re-confirming the hardening, not a rubber stamp: each verdict is anchored to code.

| Layer | Defense | Verdict | Deciding evidence |
|-------|---------|---------|-------------------|
| **A** | Data / instruction separation | **present** | `UntrustedText.cs:34,80` (Neutralize + fixpoint Fence); all 5 splices at `LlmStructuringService.cs:26`, `ReportService.cs:215/265/312/317`; data-guard line in every prompt |
| **B** | Input-symptom flagging / salvage | **present** | `IngestService.cs:94-102,119` (Detect→needs_review, severe co-occurrence); `InjectionSignals.cs:80-101`; telemetry `NeedsReviewAllSources`; **measured 0-FP / 343 items** |
| **C** | Output-authority bounding | **present** | `ReportService.cs:365` (narrative **and** title guarded), `:139-142` (alert reason guarded), `:192` (`ActionDropped` counted); `NarrativeGuard.cs:45-54` |
| **D** | Grounding is structural | **present** | `ReportService.cs:343,348-355` (citedIds validated vs providedIds → drop+count); `:157,167` (ids/counts from `groupItems`, never the model); ADR-0009 |
| **E** | Deterministic trust anchor | **present** | `AlertMatcher.cs:14-22` + `IngestService.cs:35,117` (deterministic alerts run FIRST, stored unconditionally); `ReportService.cs:107` (LLM only sees keyword-less items — can ADD, never remove) |
| **F** | Red-team regression fixture | **present** | `data/eval/redteam-injection.jsonl` (12 vectors + 2 controls); `RedTeamCoverageTests.cs:37-38,92-110` (per-class pinning + anti-shrink floor + homoglyph residual pinned) |

## Findings

**None.** No splice reaches a model prompt raw; no model-authored, manager-facing
slot is unguarded; no model output drives an irreversible/outbound action (alerts are
owned by the deterministic layer, which the model can only add to, never remove); and
the red-team fixture makes each of those a red build if it regresses.

That is the honest ceiling of a good result here: **the layers are present and
measurably covered.** It is not a proof that the system cannot be injected.

## Named residuals (deliberately not closed — kept visible)

These are owned honestly by ADR-0021 and were correctly **not** re-flagged:

- **Homoglyph marker evasion.** A Cyrillic look-alike of the fence marker
  (`<<<РALAUTE_LOPPU>>>`) is not stripped by the exact-ASCII strip. Pinned by
  `RedTeamCoverageTests` (rt-10) so a future defense is noticed, not assumed.
- **Attributed 3rd-person relay.** An injected demand the model relays as *"a customer
  recommended firing the manager"* is a grounded description of what a customer said —
  the guard is first-person-anchored and deliberately allows it. Observed and expected
  in the live red-team run.
- **A single valid-but-wrong classification.** A payload that talks the model into a
  legal-enum value the salvage layer accepts is not fully preventable; the deterministic
  layer stays authoritative and correction telemetry + the desk human-in-the-loop are
  the ongoing detectors.

## Verdict

Defense-in-depth is present at every boundary site with measured coverage, and the
residuals are named rather than hidden. The system is **injection-hardened to the
state of the art for a local 8B model** — which, stated plainly, means layered and
regression-guarded, **not** proven safe. Injection remains unsolved; this audit
confirms the posture, and the red-team fixture keeps it from silently rotting.

