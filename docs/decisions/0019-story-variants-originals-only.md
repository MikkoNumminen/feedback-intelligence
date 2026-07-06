# ADR-0019 — Story items ship as originals only (the ×2 variant fallback, taken)

- **Status:** Accepted (2026-07-06)
- **Deciders:** Mikko
- **Follows:** [ADR-0011](0011-sequence-preserving-arcs.md) (sequence-preserving
  arcs; pre-registered the `StoryVariantsPerItem = 0` fallback)

## Context

ADR-0011 multiplied planted-story items ×2 through an intensity-preserving
prompt, with a pre-registered fallback: *"if a variants run shows intensity
drift, the fallback is `StoryVariantsPerItem = 0` (originals only)."* This ADR
records **taking that fallback**, with the evidence.

A judgment pass over the first real `variants.jsonl` (the task-#2 checkpoint)
found the LLM story rephrasings drift in exactly the ways the prompt was meant to
prevent:

- **Language flips** — `core-004`'s *both* variants abandoned Finnish for English.
- **Count/ordinal inversions** — e.g. `core-003-v1` asserts "ensimmäistä kertaa"
  (first time) at sequence step 3 of a *recurring, worsening* arc.
- Plus peak-rage softening and one cross-story topic drift.

Not hypothetical: `generate --seed 42` had **selected two of them into the live
demo corpus** (`gen-42-0034` English, `gen-42-0063` count-inverted), and both
passed the `verify` gate — grounding and trend are checked, language and per-item
ordinals are not. That is precisely why ADR-0011 made this a human gate.

## Decision

**`StoryVariantsPerItem = 0`: planted-story steps compose from the domain
expert's originals only.** Noise still multiplies ×6 — visible corpus variety is
unaffected; only the ground-truth story texts are pinned to what Mikko wrote.

Mechanics: the 21 story-variant lines were dropped from `data/corpus/variants.jsonl`
(the composer's pool is that file; `core.jsonl` is not read at generate time), the
generator config set to `StoryVariantsPerItem = 0` so a future `variants` run
stays originals-only, and `generate --seed 42` recomposed the corpus + ground
truth. Because `Random.Next(n)` consumes one draw regardless of pool size, the
recompose is a fresh deterministic arrangement (the safety story's spread-composer
shuffle shifts the RNG) — still 71 items, same 3 stories, same windows/trends,
every story item now an original, zero English/drift.

## Consequences

- **Resolves task #2** (judge story-variant intensity): the answer is the
  fallback. The intensity arc is now 100% the expert's authored escalation, which
  is the strongest, most trustworthy ground truth for the demo.
- **Trade-off (accepted, per ADR-0011):** story texts no longer surface-vary
  across seeds — two seeds differ in noise, timestamps and ordering but share the
  story wording. Fine for a rehearsable demo; if cross-seed story variety is ever
  wanted, the durable fix is a stricter variants prompt + a deterministic
  acceptance gate on the `variants` verb (language match, no-ordinal-inversion),
  re-run in an announced GPU window — not shipping the drifted variants.
- Validated after the recompose (with the [ADR-0018](0018-llm-call-determinism.md)
  reliability fixes in place): acceptance PASS — dairy 5/4, availability 5/3, and
  the no-keyword safety case fires.
- The dropped variants remain in git history if ever needed.
