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

## Scope

This module is a **switchability proof**, not a finished product. The
`/schema`, desk, ingest, and report paths work immediately. Running the
generator with `Domain:Active=game` additionally needs a game **variants pool**
tagged with the story ids in `domains/game/stories.json` — there is no game
corpus yet, so `generate` is retail-only until one is authored. The point stands:
the engine did not change to gain a new domain.
