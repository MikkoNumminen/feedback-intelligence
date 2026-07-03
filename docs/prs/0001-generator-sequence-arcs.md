# PR #1 — feat(generator): sequence-preserving arcs, alert keyword config

Branch `feat/generator-sequence-arcs` → `main` (local; no remote exists).

## What

- `config/alert-keywords.json`: the deterministic alert layer's keyword list
  (injury/safety, payment, legal-threat; Finnish-stem substring contract) with
  deliberate exclusions documented — needed by Mikko before writing the
  safety-story texts, and by Phase 2 regardless.
- Sequence-preserving arcs: optional `sequence` on story-tagged core items;
  variants inherit story + sequence; timestamps strictly monotonic with
  sequence; one realization per step per set; worsening easing.
- Intensity protection: story items multiply ×2 via a dedicated prompt
  (`prompts/variants-story-v0.txt`); ×0 fallback supported.
- fi-FI culture bug fix: `HH:mm` formatting produced `18.14` on this machine —
  invariant culture is now explicit.

## Review

8-angle review: 3 angles ran as subagents (conventions, efficiency+altitude,
reuse+simplification); the 3 correctness angles (line-by-line, removed-behavior,
cross-file) hit the provider session limit and were performed inline by the
primary agent instead (noted for transparency; the multi-agent correctness pass
can be re-run after the limit resets).

## Findings → resolutions (all fixed in the fix commit)

1. Unclamped monotonicity bump could push a sequenced timestamp past the story
   window → fail-loudly overflow check at compose time + window-containment
   assertion added to the monotonicity test.
2. `MinGroundedIds` was validated against config `Count`, which sequenced pools
   ignore → compose-time check against the actual step count (unsatisfiable
   ground truth now fails at generation, not in Phase 4) + test.
3. `StoryVariantsPerItem=0` ("no LLM call") still required the story prompt
   file to exist → templates load lazily; validator requires the path only
   when the multiplier is > 0.
4. Business-hours window (8–22) hardcoded at three call sites + hardcoded
   collision gap (CLAUDE.md: config over hardcoding) → `DayStartHour`/
   `DayEndHour`/`SequenceCollisionGapMin/MaxMinutes` config, validated; single
   `RandomTime` helper so story and noise hour distributions cannot diverge
   (an hour histogram must not leak story membership).
5. Two ~30-line inline branches in `Compose` → extracted
   `ComposeSequencedStory` / `ComposeSpreadStory`.
6. Corpus-size breakdown copy-pasted into three docs → `data/corpus/README.md`
   is the single authoritative source; CLAUDE.md and TODO.md now point to it.

## Verification

`dotnet build` clean; 28/28 tests green including three new pinning tests
(window overflow fails loudly, unsatisfiable minGroundedIds fails loudly,
window containment on sequenced arcs).
