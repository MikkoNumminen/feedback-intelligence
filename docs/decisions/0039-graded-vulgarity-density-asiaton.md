# ADR-0039 — Graded vulgarity: dense, non-substantive profanity is forced to `asiaton`

- **Status:** Accepted (2026-07-15)
- **Deciders:** Mikko
- **Follows:** [ADR-0027](0027-racism-recognition-alert-lexicon.md) (a lexicon forces a
  conduct category on a single hit — for ethnic slurs → `rasismi`),
  [ADR-0032](0032-unrated-nonsubstantive-categories.md) (demoted = unrated),
  [ADR-0036](0036-deterministic-category-keyword-override.md) (deterministic
  category-keyword override architecture)

## Context

A vulgar pile-up — `Paskapillupersepornolehtipaviaani` — was stored `category=muu`
(the rated catch-all), `severity=high`, `theme=offensive_language`. Poro *recognized*
the offensiveness (the theme) but filed it under the rated catch-all, not `asiaton`,
and rated nonsense `high`. Poro ignoring the `asiaton` affordance is the exact ADR-0032
limitation ("the model ignores the affordance"); the demoted-category suppression
(ADR-0032/0033/0038) can't help, because the item was never in a demoted category. The
only deterministic conduct-forcing rule today is the rasismi ethnic-slur lexicon
(ADR-0027) — this word is not ethnic, so nothing fired.

The owner's requirement, and its crucial nuance: **a lone swear inside real feedback**
(`Möivät paskaa` — "they sold crap", a crude but genuine quality complaint) **must stay
rated**; a **pile-up of distinct vulgar stems mashed into nonsense** is not feedback and
belongs in the moderation view. ADR-0032 gave up on *generic* garbage detection because
Poro routed keyboard-mash unpredictably — but dense vulgarity has a **deterministic
signal** (the stems themselves), so it is catchable where generic gibberish is not.

## Decision

**A deterministic, DENSITY-gated graded vulgarity scorer forces the demoted conduct
category (`asiaton`).**

1. **Domain lexicon** (`domains/<active>/vulgarity-lexicon.json`, optional like the
   category-keyword lexicon): Finnish profanity **stems in tiers** (`mild`, `strong`),
   plus the demote thresholds. Owner-authored (register is cultural), matched as
   case-insensitive invariant substrings — the same contract as the alert lexicon.
2. **Graded level 0–3** per message (`VulgarityScorer`):
   - **0** none · **1** incidental (a mild stem) · **2** notable (a strong stem) —
     Levels 1–2 stay **rated** (a Phase-2 `⚑` recognition tag will read them; see below).
   - **3** dominant → **force `asiaton`** (demoted → unrated, count-only, moderation view).
3. **Density gate for Level 3** — demote **iff** the vulgar-character share of the message
   is `>= demoteRatio` **AND** there are `>= demoteMinDistinctStems` **distinct** stems.
   Both conditions matter:
   - *distinct-count* (not raw occurrences): repeating one swear (`paska`… `paska`) does
     not inflate it — a lone stem stays rated (the owner's "singletons shouldn't flag").
   - *ratio*: a furious-but-real complaint carrying three swears in a substantive sentence
     is mostly real words (low ratio) → **stays rated**; a nonsense pile-up is mostly
     vulgar stems (high ratio) → demoted. The two conditions together separate *angry real
     feedback* from *vulgar nonsense*, which neither alone can.
4. **Placement** — in `CategoryOverrideResolver`, **after** the rasismi alert override and
   an already-demoted model choice, **before** the product category-keyword override:
   racism (extreme, single-hit) → dense vulgarity → product hints → model. Ethnic slurs
   stay in the alert lexicon (single hit → `rasismi`); this scorer never touches them.
5. **Deterministic, config-driven, no model dependency.** Runs at ingest **and**
   restructure, exactly like the other overrides; thresholds are validated config.

## Consequences

- `Paskapillupersepornolehtipaviaani` (`paska`+`pillu`+`perse` = 3 distinct, ~0.45 vulgar
  share) → **`asiaton`**: unrated, no severity, count-only in "Moderoitava sisältö".
  `Möivät paskaa.` (1 distinct, ~0.31 share) → **stays rated** in its real category.
- **Thresholds are empirical and MUST be measured on the owner's real examples**, not
  guessed — the defaults here pass the two known cases and are config-tunable; they will
  mis-fire on cases outside the fixture until tuned. This ADR does not claim the gate is
  precise, only that it is deterministic and honest about its inputs. No invented Finnish
  corpus is used for tuning (the eval cases are the owner's).
- **Deferred to a follow-up (Phase 2):** the visible `⚑ kiroilu` **recognition tag** for
  rated Level 1–2 items ("flag cursing without demoting"). It needs a structure/schema
  field and view work, and is kept out of the operational alert channel (ADR-0033); it is
  a separate, single-concern change. The graded *scorer* lands now; only its lower-tier
  *presentation* is deferred.
- A domain that ships no `vulgarity-lexicon.json` forces nothing (empty lexicon) — game
  and any future domain are unaffected.
