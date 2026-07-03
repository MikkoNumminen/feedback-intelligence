# PR #8 — feat(generator): mechanized Phase 4 acceptance (`verify` verb)

Branch `feat/acceptance-verify` → `main` (local; no remote exists).

## What

`verify --ground-truth <f> --report <f>`: the Phase 4 acceptance contract as
one command — per planted story, ID-grounding (≥ minGroundedIds inside the
expected department's theme), expected-alert presence, report-window coverage,
trend comparison, keyword-in-narrative (informational). Verified live against
the run-through artifacts: ACCEPTANCE: PASS (dairy 9/4, safety 1/1 + alert,
availability 7/3).

## Findings → resolutions (all addressed)

1. **Design flaw (the important one): the trend gate compared a story-level
   expectation against a department AGGREGATE** — same-department noise the
   LLM classifies into a story's department legitimately dilutes direction,
   so the first real GPU-window run would have failed a correctly-planted
   story (the run-through only passed because LLM-down left the groups pure).
   → Gates redesigned: grounding + alert + window are HARD (story-owned,
   deterministic); trend is a loud WARNING tier. Rationale recorded in code,
   CLAUDE.md, and as corpus-composition guidance in data/corpus/README.md.
2. Same mechanism for the single-item safety story (two noise items could
   flip its department to "kasvava") → covered by the warning tier.
3. Window clause of the contract was silently dropped → report-window-covers-
   story-window is now a hard gate with an explicit "wrong report?" diagnosis
   (tolerant when unparseable).
4. Test fixtures never exercised noise-sharing-department, window mismatch,
   or vacuous ground truth → all three added (10 verifier tests total).
5. CLAUDE.md not updated in the same change (ground-rule violation) → Phase 1
   verb list + gate-design note added.
6. Empty ground truth produced a vacuous PASS, and usage errors shared exit
   code 1 with gate failure → empty stories throw (exit 2 = "gate never ran",
   distinct from exit 1 = "gate failed"); missing files get clean errors.

## Verification

Build clean; 23/23 generator tests; live `verify` run against the placeholder
run-through artifacts passes with zero trend warnings.
