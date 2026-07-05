# ADR-0015 — Poro tuning after the first real-corpus run (keep the model, fix the usage)

- **Status:** Accepted (2026-07-05)
- **Deciders:** Mikko
- **Follows:** [ADR-0003](0003-poro-for-both-roles.md) (Poro for both roles),
  [ADR-0002](0002-llm-behind-one-abstraction.md) (keyed-DI role split)

## Context

ADR-0003 accepted Poro-2-8B for both roles with one open caveat: **its behavior
on *messy* Finnish was unmeasured** (clean placeholder Finnish does not predict
dialect/desk-shorthand). The first evidential run measured it — the real
27-text corpus → `variants` → `generate --seed 42` → live ingest → report →
`verify`.

Findings (Poro on the real corpus):

- **JSON discipline: perfect.** 0/71 `structure_failed`, 0 salvage
  normalisations. The salvage layer (ADR-0004) never had to fire. The
  unmeasured risk did not materialise.
- **Two behavior issues surfaced — both were OUR usage, not Poro's judgment:**
  1. **Structuring misclassified** the no-keyword safety item ("runkopuuta /
     lankku" → `liha_kala`, reading *runko* as carcass). Cause: the structuring
     prompt injected the **bare enum keys** with no meaning, so the model
     guessed from the key string.
  2. **Alert nomination flooded** — 21–25 alerts, most fabricated (delivery
     delays, litter, noise stamped "vakava terveysriski"), while the *genuine*
     no-keyword safety case was missed or drowned. Cause: the model was asked to
     **select** safety cases from a 60-item list; an 8B model rationalises a
     safety angle for almost anything in a long list.

Diagnostic that fixed the direction: asked about **one** item as a strict
yes/no, Poro discriminated **flawlessly** (`kyllä` only for the real safety
text; `ei` for spoiled milk, delivery, litter, out-of-stock, service). Poro is
not weak here — list-selection is the wrong shape for it.

**Model swap was rejected:** Poro is the only Finnish-capable model at the size
this machine can run. qwen3:8b was tested and does not speak Finnish well enough
for the user-facing synthesis. So the fix is to use Poro to its strengths, not
to replace it. The keyed-DI split (ADR-0002) remains the escape hatch if that
ever changes.

## Decision

**Keep Poro for both roles; fix the two usage patterns.**

1. **Structuring — give the model the taxonomy's meaning.** The prompt now
   injects each category as `"key" (Finnish label)` (labels already existed in
   `domain.json`), not the bare key. Domain-neutral: labels come from the active
   domain. Structuring stays deterministic (`temperature 0`).

2. **Alerts — screen every keyword-less complaint individually.** The LLM alert
   layer judges each candidate **alone** as a strict `kyllä`/`ei` (`alertVerify`
   prompt); a "kyllä" becomes an alert. Candidates are keyword-less items that
   are `complaint`-typed (or unstructured) — praise, suggestions and questions
   are never safety alerts. The judgment is **temperature 0** (deterministic).
   For the confirmed few, one `alertNomination` call over *only* those items
   supplies a grounded Finnish reason (falling back to a localized generic line
   if the model omits one). Recall-biased and fail-open: only an explicit
   negative (`ei` / `no`) rejects; an error or ambiguous answer keeps the alert.

   **Two designs were tried and rejected before this one:**
   - A **list-selection** nomination (ask Poro to pick alerts out of a batch)
     both **floods** (rationalises a safety angle for ordinary complaints) *and*
     **misses** the real case, non-deterministically (temperature 0.3). It is
     unreliable for precision *and* recall — the whole reason to go per-item.
   - A **two-stage** nominate→verify inherits stage 1's recall gap (verify can
     only re-judge what nomination surfaced; it missed the timber some runs).
   - A **JSON verify contract** (`{"alert": true|false}`) *degraded Poro's
     judgment* — it defaulted even the real safety case to `false`. A **natural
     yes/no** question ("kyllä vai ei?") is exact. So the verify prompt asks a
     plain-language yes/no and the parser accepts the supported languages' words
     (fi `ei`, en `no`).

### Tuning knobs (these will move again — retune here)

| Knob | Where | Now | Purpose |
|---|---|---|---|
| Category labels in the structuring prompt | `domains/<d>/domain.json` `categories`, injected by `LlmStructuringService.RenderLabelledKeys` | key + label | give the model each department's meaning |
| Structuring prompt | `prompts/structuring-v0.txt` | v0 | "return the KEY, not the label" |
| Alert screen prompt (the decision) | `domains/<d>/prompts/alert-verify-v0.txt` | v0 | strict single-item yes/no |
| Alert reason prompt | `domains/<d>/prompts/alert-nomination-v0.txt` | v0 | grounded reason, confirmed items only |
| `Report:MaxLlmCallsPerReport` | `appsettings.json` | 16 | synthesis + reason-call budget |
| `Report:MaxAlertVerifyCalls` | `appsettings.json` | 80 | per-item screen budget (covers every keyword-less complaint) |

## Consequences

- **`verify` on seed 42: ACCEPTANCE PASS** — all three planted stories grounded;
  the no-keyword safety story is caught **by understanding** and surfaces as
  **one** clean alert; dairy 5/4, availability 5/3, correct trends.
- A report now makes more LLM calls (nomination batches + one tiny verify per
  nomination). ~40 s per on-demand report on this GPU — acceptable for the demo.
  Verify calls are one-word outputs on their **own** budget, so precision-
  checking never starves narrative synthesis.
- The structuring label fix and the two-stage alert are **domain-neutral** — a
  new domain (e.g. `game`) inherits both by supplying its own labels and
  prompts; no engine change.
- The correction-telemetry drift signal from ADR-0003 still stands as the
  ongoing measure; this ADR is the first *offline* measurement that informed the
  prompts and knobs above.
