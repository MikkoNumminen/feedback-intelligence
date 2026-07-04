# Domain modules — the contract

The engine is domain-neutral. A **domain module** is a folder of data that
parameterizes it; adding a domain means adding a module, never editing the core
([ADR-0012](decisions/0012-pluggable-domain-modules.md)).

## Selecting the active domain

One domain is active at a time, chosen by configuration:

```jsonc
// appsettings.json
"Domain": { "Active": "retail", "Root": "domains" }
```

Switchable from the .NET CLI without a rebuild of code (the module must be
present beside the binary or in the working directory):

```bash
dotnet run --project src/FeedbackIntelligence.Api -- --Domain:Active=game
dotnet run --project tools/FeedbackIntelligence.Generator -- generate --seed 42 --Domain:Active=retail
```

The active domain is loaded once and **validated at startup** — an unknown name
or a malformed module fails the boot rather than silently degrading.

## Anatomy of a module

```
domains/<name>/
  domain.json            # taxonomy + labels + prompt map      (required)
  alert-keywords.json    # deterministic alert lexicon          (required by the API)
  stories.json           # generator planted-story definitions  (required by the generator)
  prompts/
    synthesis-v0.txt         # domain-voiced management synthesis (persona, language)
    alert-nomination-v0.txt  # domain-voiced safety/urgency screen
```

Only the neutral structuring prompt lives in the core (`prompts/structuring-v0.txt`);
it is templated with the active domain's taxonomy at load time via the
`{{categories}}`, `{{severities}}`, and `{{types}}` placeholders.

### `domain.json`

```jsonc
{
  "name": "retail",
  "categoryFieldLabel": "osasto",        // the human word for the "category" field
  "categories": {                        // REQUIRED, non-empty: key -> display label
    "maito_kylma": "Maito & kylmä",
    "leipa": "Leipä"
    // ...
  },
  "severities": { "low": "Matala", "medium": "Keskitaso", "high": "Korkea", "critical": "Kriittinen" },
  "types": { "complaint": "Valitus", "praise": "Kehu", "suggestion": "Ehdotus", "question": "Kysymys", "other": "Muu" },
  "prompts": {                           // role -> file, relative to the module dir
    "synthesis": "prompts/synthesis-v0.txt",
    "alertNomination": "prompts/alert-nomination-v0.txt"
  }
}
```

- `categories` is **required and non-empty**. Keys are the enum values the
  structuring model must emit; values are the UI display labels.
- `severities` and `types` are **optional and domain-overridable**. Omit them to
  inherit the core defaults (`low/medium/high/critical` and
  `complaint/praise/suggestion/question/other`) — most domains only author
  `categories`.
- `categoryFieldLabel` is the domain's word for the category dimension
  (`osasto` for retail, `area` for game); it appears in the report and the desk.

### `alert-keywords.json`

```jsonc
{
  "categories": {                        // REQUIRED, non-empty: alert category -> patterns
    "injury_safety": ["loukkaantu", "liukastu", "ensiapu"]
  }
}
```

Case-insensitive substring match over the raw feedback text (no tokenizer, no
model). This is layer 1 of the two-layer alert design; the LLM nomination pass
(layer 2) catches urgent items that carry no keyword. Keeping a class of urgent
signal *out* of this list on purpose (so it is detectable only by understanding)
is a legitimate, documentable choice — see the retail file's `deliberateExclusions`.

### `stories.json`

An array of planted-story definitions the seeded generator composes into the
demo corpus and its machine-checkable ground truth. Each story's `Category` must
be one of the domain's `categories`; the loader validates the whole set against
the domain taxonomy and fails loudly on any mismatch. See
[data/corpus/README.md](../data/corpus/README.md) for the composition contract.

> The generator can only compose a domain whose **variants pool carries matching
> story tags**. A new domain needs its own corpus before `generate` runs against
> it; the `/schema`, desk, ingest, and report paths need no corpus and switch
> freely.

## Worked examples

- **[domain/retail.md](domain/retail.md)** — Finnish retail, the first
  application (a hybrid hardware-store / grocery).
- **[domain/game.md](domain/game.md)** — a game-studio player-feedback domain,
  added purely as a second module to prove the contract: a new folder, zero core
  edits.

## Checklist: adding a domain

1. `mkdir domains/<name>` and author `domain.json` (`categories` at minimum).
2. Add `alert-keywords.json` (the API loads it at startup).
3. Add `prompts/synthesis-v0.txt` and `prompts/alert-nomination-v0.txt` in the
   domain's voice/language; list them in `domain.json`'s `prompts` map.
4. (For the generator) add `stories.json` and a variants pool tagged with those
   story ids.
5. Run with `--Domain:Active=<name>`. Nothing in `src/` changes.
