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
  "language": "fi",                      // output/UI language; default "en"
  "categoryFieldLabel": "osasto",        // the human word for the "category" field
  "categories": {                        // REQUIRED, non-empty: key -> display label
    "maito_kylma": "Maito & kylmä",
    "leipa": "Leipä"
    // ...
  },
  "severities": { "low": "Matala", "medium": "Keskitaso", "high": "Korkea", "critical": "Kriittinen" },
  "types": { "complaint": "Valitus", "praise": "Kehu", "suggestion": "Ehdotus", "question": "Kysymys", "other": "Muu" },
  "sources": ["google_review", "email", "web_form", "desk"],  // REQUIRED: accepted ingest channels
  "prompts": {                           // role -> file, relative to the module dir
    "synthesis": "prompts/synthesis-v0.txt",
    "alertNomination": "prompts/alert-nomination-v0.txt"
  }
}
```

- `categories` is **required and non-empty**. Keys are the enum values the
  structuring model must emit; values are the UI display labels.
- `categoryHints` is **optional**: per-category guidance appended to the label
  inside the STRUCTURING PROMPT only (never displayed), for categories whose
  short label is not self-explanatory (retail's `asiaton`). Keys must exist in
  `categories` — the loader rejects a typo'd hint.
- `catchAllCategory` is **optional**: the key of the domain's catch-all
  (retail's `muu`). It bounds `POST /live/restructure` and marks the bucket
  operator maintenance re-structures as the vocabulary grows. The desk's live
  summary once split this category into emergent topics named by the
  structuring model's free-text theme
  ([ADR-0026](decisions/0026-categories-emergent-topics-live-summary.md)); that
  split is retired and the catch-all now renders as a single category like any
  other
  ([ADR-0035](decisions/0035-categorization-discipline-muu-single-category-hints.md)).
  Must be a key in `categories`.
- `demotedCategories` is **optional**: category keys views sort LAST regardless
  of count (retail demotes `rasismi` and `asiaton` — hostile content must not
  lead the page). **The list order is load-bearing**: demoted sections render
  in declared order among themselves, so the last key lands at the very bottom.
  Presentation-only; keys must exist in `categories`.
- **Category-alert override** ([ADR-0027](decisions/0027-racism-recognition-alert-lexicon.md)):
  an alert-lexicon category (in the module's `alert-keywords.json`) whose name
  IS a declared `categories` key categorizes the item **deterministically** —
  the forced category outranks the structuring model and desk acceptance alike
  (retail's `rasismi`). Alert-only lexicon categories (e.g. `injury_safety`)
  are unaffected.
- `severities` and `types` are **optional and domain-overridable**. Omit them to
  inherit the core defaults (`low/medium/high/critical` and
  `complaint/praise/suggestion/question/other`) — most domains only author
  `categories`. **Declare severities least→most severe**: the declaration order
  is load-bearing — views render the distribution in it and treat the last two
  levels as the "severe" headline pair.
- `sources` is **required and non-empty** — the ingest channels the domain
  accepts as `source` values (a game studio's `steam_review`/`discord`/…, a
  retailer's `google_review`/`email`/…). `POST /feedback` rejects a source not in
  this list, and it **must include `desk`**: the desk-entry UI is always served
  and posts `source=desk`, so the API fails to boot otherwise. The generator also
  draws from this list for a noise item (and validates story sources against it).
- `language` is the domain's **output/UI language** (short code, e.g. `fi`, `en`).
  Optional, **default `en`**; retail sets `fi`. It drives the report's fallback
  prose, the trend/`direction` label, the snapshot page, and the desk/management
  frontend chrome. (It does NOT localize category/severity/type **value** labels —
  those come from `categoryLabels`/`severityLabels`/`typeLabels`; a domain that
  omits `severityLabels` shows raw keys like `low/high`. Nor is it the per-item
  `language` in the feedback schema, which is each item's *detected* language —
  see [schema.md](schema.md).) The LLM narratives follow the domain's voiced prompts.
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

1. `mkdir domains/<name>` and author `domain.json` (`categories` + `sources` at
   minimum; `sources` must include `desk` — the desk UI is always served).
2. Add `alert-keywords.json` (the API loads it at startup).
3. Add `prompts/synthesis-v0.txt` and `prompts/alert-nomination-v0.txt` in the
   domain's voice/language; list them in `domain.json`'s `prompts` map.
4. (For the generator) add `stories.json` and a variants pool tagged with those
   story ids.
5. Run with `--Domain:Active=<name>`. Nothing in `src/` changes.
