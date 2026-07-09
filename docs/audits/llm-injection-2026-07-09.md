# LLM prompt-injection audit — 2026-07-09

> **Honest limitation, stated up front.** Prompt injection is an *unsolved* research
> problem. This report grades **defense-in-depth and measurable coverage** — it does
> **not** prove the system safe. A clean scorecard means the layers are present and
> wired, not that injection is impossible, especially against a local 8B model. Read
> every "present" below as "this hole's concrete mechanics are closed," never as "this
> can't be injected."

- Pre-flight: **LLM boundary confirmed** — 4 production call sites (`IChatClient.GetResponseAsync` via Ollama), 3 domain prompt assets + `prompts/*.txt`, untrusted-text-in-prompt: **yes** (customer feedback free text). Proceeding.
- Commit audited: `0689d9de848bb481dea4acef483095ce07118ae0` on branch `master`
- Scope: repo root (production path: `FeedbackIntelligence.Api` + `FeedbackIntelligence.Core.Security` + `FeedbackIntelligence.Llm`)
- Result: **all six layers PRESENT.** 0 high/critical findings. 1 low (defense-durability note). Named residual (homoglyph) already owned by the red-team fixture — not a finding.

## Surface map (Phase 1) — every untrusted → prompt splice

| # | Call site | Untrusted fragment | Source | Neutralized before splice? |
| --- | --- | --- | --- | --- |
| 1 | `Llm/Structuring/LlmStructuringService.cs:26` — structuring | `feedbackText` | HTTP ingest body | ✅ `UntrustedText.Fence()` (delimited block + neutralize) |
| 2 | `Api/Analysis/ReportService.cs:296` — alert-verify (yes/no screen) | `item.Text` | stored feedback | ✅ `UntrustedText.Neutralize()` |
| 3 | `Api/Analysis/ReportService.cs:246` — alert nomination | `Excerpt(item.Text)` | stored feedback | ✅ `UntrustedText.Neutralize()` |
| 4 | `Api/Analysis/ReportService.cs:343,348` — theme synthesis | item excerpts **+ model-produced `Theme` key** (2nd-order) | stored feedback + prior model output | ✅ `UntrustedText.Neutralize()` on both |
| — | `Api/Program.cs:321` — health ping | none (constant `"ping"`) | n/a | N/A (no untrusted text) |
| — | `tools/*Generator*`, `tools/*StructuringEval*` | corpus text | offline dev tooling | out of production scope |

Second-order surface (model output that becomes an action or is re-displayed): the synthesis **title/narrative** and the **alert reason** are model-authored and manager-facing — all three are authority-bounded (see layer C). Counts, trend direction, groupings, and severities are computed deterministically and never read from the model.

## Layer scorecard (A–F)

| Layer | Verdict | Deciding evidence |
| --- | --- | --- |
| **A — data/instruction separation** | **present** | `Core/Security/UntrustedText.cs` — one shared chokepoint. Fence markers stripped to a **fixpoint** (loop guards against split-marker reassembly, `:45-53`); every C0/C1 control char + U+2028/U+2029 collapsed to space (`:56-73`); `"`/`` ` `` → `'`. A data-guard block wraps the structuring splice. Applied at **all four** boundaries. |
| **B — input-symptom flagging / salvage** | **present** | `Core/Security/InjectionSignals.cs` — deterministic multi-lingual symptom scan; `IngestService.cs:97-102` raises `needs_review` **without dropping or altering** the item, and adds `severe-rating-with-injection-symptoms` on the "talked-into-critical" co-occurrence. Flagged items stay in aggregates but surface a `flaggedCount` (`ReportService.cs:179`). |
| **C — output-authority bounding** | **present** | `Core/Security/NarrativeGuard.cs` — directive/recommendation markers (first-person/imperative only, to keep FPs low). Applied to **every** model-authored, human-facing slot: theme narrative **and** title (`ReportService.cs:396`) and alert reason (`:156`); a directive slot drops to the deterministic fallback, counted separately from ungrounded drops. |
| **D — grounding is structural** | **present** | Citation gate: every `citedIds` entry must be in the provided id set or the whole narrative is dropped + counted (`ReportService.cs:371-386`). Counts/trends/directions computed deterministically (`ComputeDirection`/`TrendDirection`, statistical significance gate). `StructureValidator.cs` constrains structuring enums to the domain taxonomy. |
| **E — deterministic trust anchor** | **present** | `Core/Alerts/AlertMatcher.cs` runs **first**, independent of the LLM; documented invariant "the LLM layer may ADD alerts but can never remove a deterministic one." Alert-verify is a constrained yes/no screen that **fails closed** on model outage (`ReportService.cs:312-319`). No irreversible/outbound action is driven by model output alone. |
| **F — red-team regression fixture** | **present** | `tests/.../RedTeamCoverageTests.cs` + `data/eval/redteam-injection.jsonl` — ≥12 vectors (override, role, field-injection, forged answer/JSON, newline **and** U+2028 row breakout, fence-marker reassembly, suppression, an A3 directive, a homoglyph), benign controls, **per-class label pinning** so deleting a marker group turns the build red, and a purity tripwire. The homoglyph residual is asserted as a **named, pinned gap**. |

## Findings

### Low

- [src/FeedbackIntelligence.Api/Analysis/ReportService.cs:419](../../src/FeedbackIntelligence.Api/Analysis/ReportService.cs#L419) [low] — **A (defense durability, not a live hole):** `TryLlmAsync` splices `template.Replace("{{data}}", data, …)` **raw**, relying on every caller (`SynthesizeThemeAsync`, `NominateAlertsAsync`) to have already `Neutralize()`d each untrusted fragment when building `data`. Today all callers do (verified: `:246`, `:343`, `:348`). But the neutralize-at-caller contract is a **convention the signature doesn't enforce** — a future caller could hand `TryLlmAsync` an un-neutralized fragment and nothing would catch it (the structuring call at `:26` has the same shape but is the single obvious site). *Failure scenario:* someone adds a 4th `{{data}}` prompt that interpolates `item.Text` directly, skipping `Neutralize`, and re-opens the quote-breakout vector at a boundary the red-team fixture doesn't exercise (the fixture tests `Neutralize`/`Detect` as units, not each call site's wiring). *Move toward:* accept a pre-neutralized wrapper type (e.g. `NeutralizedText`) at the `{{data}}`/`{{text}}` seam so the boundary can't compile with raw text, **or** add a coverage test asserting each production prompt-splice site routes through `UntrustedText`.

## Named residuals (deliberately not covered — kept visible)

1. **Homoglyph fence markers** — a Unicode look-alike of the delimiter tail (`ALAUTE_LOPPU`) survives the exact-ASCII strip. Owned and *pinned* by the red-team fixture (`expect: "residual-homoglyph"`), so it stays visible rather than silently assumed closed. Substring-based `Neutralize` does not fold homoglyphs by design.
2. **In-band imperative that stays inside the data block** — layer A only stops breakout; an imperative that never breaks out can still nudge the model. Explicitly handled by B (flag) + C (authority bound) + E (deterministic anchor), and named in `UntrustedText.cs:18-20`.
3. **Paraphrased directives** — `NarrativeGuard` is substring-based; a directive that avoids the marker verbs evades it. Named in `NarrativeGuard.cs:26-29`; the prompt constraint is the primary defense, the guard a backstop.
4. **Determined adversary vs an 8B local model** — stated in `UntrustedText.cs:9-11`. Delimiting is not a wall.

## What's verifiable vs editorial

| Claim | Source of truth | Verifiable? |
| --- | --- | --- |
| Does the codebase call an LLM with untrusted text? | 4 call sites traced to feedback text | ✅ Yes (surface map) |
| Is each boundary neutralized before splice? | The four call sites | ✅ Yes (file:line cited) |
| Are all six layers present & wired? | The named security files + their call sites | ✅ Yes |
| Does this make injection impossible? | — | 🔴 No — unsolved; layers ≠ proof |
| Is the `{{data}}` convention a real risk *today* | Human judgement (all callers currently comply) | 🟡 Heuristic — low (durability, not a live hole) |

**Bottom line:** this is a reference-grade defense-in-depth posture — every boundary neutralized, every model-authored slot authority-bounded, deterministic layers owning every load-bearing decision, and a per-class-pinned red-team fixture with an honestly-named residual. The single low finding is about making the neutralize-at-the-seam contract *structurally* unbreakable, not about a currently-open hole.
