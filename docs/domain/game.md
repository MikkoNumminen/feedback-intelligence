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

## The full ingest→report loop runs for game

Ingest channels are domain data (`sources` in `domain.json`), so a game item
flows the whole pipeline. Verified live with **zero GPU** (desk/`acceptedStructure`
path skips the model at ingest; `--Report:MaxLlmCallsPerReport=0` keeps the report
deterministic):

- `POST /feedback` with `source: steam_review | discord | in_game` → **201**; a
  retail channel (`email`) or retail category (`maito_kylma`) → **400** under the
  game domain.
- The deterministic alert layer fires the **game** lexicon (`data_loss` /
  "save wiped").
- `GET /report` groups by game categories (`bugs_crashes`, `matchmaking`), each
  **grounded to the exact feedback IDs**, alert on top.

## The report reads English

`domain.json` sets `"language": "en"`, so the whole game surface is English
([ADR-0014](../decisions/0014-domain-output-language.md)) — verified live (zero-GPU):
the same data renders `Trend: growing` / "Automated summary…" under game where
retail renders `Suunta: kasvava` / "Automaattinen kooste…", with an identical
neutral `direction` key. The deterministic fallback prose, direction labels, the
snapshot page, and the desk/management frontends all follow the domain language;
the LLM narratives follow the English game prompt.

## Scope and known edges

This module is a **switchability + loop proof**, not a shipping product. Remaining
edges, flagged honestly:

- **The corpus pool path is not domain-owned.** `Generator:VariantsPath` /
  `OutputDir` stay global, so `--Domain:Active=game` alone does not pick up the
  game pool — the explicit `--Generator:VariantsPath=…`/`--Generator:OutputDir=…`
  above are required. It fails *safe* (a bare command errors with "variants file
  not found" rather than composing against the wrong pool); making the pool path a
  domain property is the clean fix.
- **LLM-written game narratives** (synthesis with the model up) need an announced
  GPU window to exercise, exactly like retail's live run — the loop above proves
  the wiring without one.
