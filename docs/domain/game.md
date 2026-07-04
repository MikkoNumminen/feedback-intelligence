# Domain: game-studio player feedback (proof-of-contract module)

> **A second domain, added as a folder to prove the engine is domain-neutral.**
> `domains/game/` exists so the claim in [ADR-0012](../decisions/0012-pluggable-domain-modules.md)
> is demonstrable, not just asserted: switching to it required **zero edits to
> `src/`**. The authoring contract is in [../domains.md](../domains.md).

A live-service game studio's player-feedback surface — Steam reviews, support
tickets, Discord, in-game reports — condensed into the same grounded situational
view retail gets.

## What the switch demonstrates

```bash
dotnet run --project src/FeedbackIntelligence.Api -- --Domain:Active=game
curl localhost:5282/schema
```

`/schema` flips wholesale from the retail taxonomy to:

- `categoryField`: `area` (retail's was `osasto`).
- `categories`: `gameplay_balance`, `performance_stability`, `bugs_crashes`,
  `monetization`, `matchmaking`, `ui_ux`, `content_progression`, `audio`,
  `community_toxicity`, `other` — with English display labels.
- the alert lexicon (`data_loss`, `security_account`, `payment_failure`,
  `blocking_bug`) and the synthesis/alert-nomination voice switch to English.

Severities and types are not overridden — game inherits the core defaults.

## Mock corpus — the generator runs for game too

A hand-written **placeholder** pool proves the seeded generator is domain-neutral,
not just `/schema`:

```bash
dotnet run --project tools/FeedbackIntelligence.Generator -- generate --seed 42 \
  --Domain:Active=game \
  --Generator:VariantsPath=data/corpus/game/dev-placeholder-variants.jsonl \
  --Generator:OutputDir=data/corpus/game
```

From `data/corpus/game/dev-placeholder-variants.jsonl` (30 mock English items:
12 tagged `progression-loss-worsening` + 18 noise) it composes a 69-item game
corpus with machine-checkable ground truth — the planted story grounds to 9 ids
in-window, trend `worsening`, category `bugs_crashes`, and **no story tag leaks**
into the corpus (the analyzer meets it cold). The pool is NON-EVIDENTIAL and
registered in [../mock-data-register.md](../mock-data-register.md); the generated
output is gitignored and regenerable.

## Scope and known edges

This module is a **switchability proof**, not a shipping product. `/schema`, the
desk, and the report/deterministic paths work immediately; the generator now
runs for game via the mock pool above. Two source-related edges remain (not
blockers for this proof, flagged honestly):

- **`Ingest:AllowedSources`** is still a retail-flavored list
  (`google_review/email/web_form/desk`). Pushing the game corpus (sources
  `steam_review/support_ticket/discord/in_game`) through `POST /feedback` would
  be rejected until that list is domain-configurable — so the full game
  ingest→report loop is not wired yet.
- **`CorpusComposer`'s noise-source fallback** is a hardcoded retail source set,
  used only when a noise item declares no source; the mock pool sidesteps it by
  giving every item an explicit source. Making both source lists domain data is
  the natural next step to full neutrality.
