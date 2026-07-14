# Poro-2-8B: measured behavior and how we work around it

What Poro-2-8B does well, what it does badly, the numbers behind both, and the
**deterministic architecture** we use to patch its failure modes. This is a living
reference: every claim here is measured, not asserted, and traces to an ADR, a test,
or a recorded experiment.

Two independent projects measured Poro and reached the **same conclusion** — *pick the
best Finnish writer and patch its failure modes with architecture, rather than pick a
weaker writer for its obedience*:

- **This project (feedback-intelligence)** — retail feedback structuring + synthesis.
  Adopted Poro for both roles ([ADR-0003](decisions/0003-poro-for-both-roles.md)); the
  findings below about **categorization** come from here.
- **mikkonumminen.dev** — a personal-portfolio RAG chat backend (Postgres + pgvector +
  Ollama on an RTX 3080 Ti). Ran a blind, statistically-tested evaluation of Poro vs
  `qwen3:8b` / `llama3.1:8b`, then **kept `qwen2.5:7b` for production** and used Poro
  only to characterize the Finnish-quality ceiling. The findings below about **language
  drift and translation** come from there (its `content/posts/rag-finnish-*.md` posts
  and `chat-backend/` mitigations).

The same 26/30 naturalness result led to *different* deployment calls — we adopted Poro
for Finnish synthesis; the RAG kept its resident model because the naturalness edge did
not outweigh Poro's drift for a chat use case. Same evidence, different trade-off. That
is the honest shape of a model decision.

## What Poro does well

- **Finnish naturalness — decisively best.** Blind test, 30 Finnish questions, native
  judge: Poro won **26/30** rounds, mean rank **1.37** vs qwen3 **2.23**, llama **2.40**
  (Friedman χ² = 22.85, **p < 0.0001**). Pairwise Poro beat qwen3 **20–3**, llama
  **22–1**. Sole-worst in **0/30** rounds (qwen3 9, llama 11).
- **Morphology and inflection** — the qualitative edge: natural inflected forms
  ("Astro 6:sta", "TypeScriptistä") the general models get stiff.
- **Spelling/grammar** (Voikko error rate): Poro **3.3%** < qwen3 4.0% < llama 5.8%
  (human-approved-string floor 7.2%).
- **Grounded synthesis** is on par with the best: substantive grounded Finnish
  **25/27 (93%)**, tied with qwen3, well above llama (18/27). Poro "wins nothing on
  synthesis" but loses nothing either — so its naturalness edge is free.
- **Cheap to run**: ~**5.9 GB** VRAM, 8192-token context (max prompt+output measured
  5205), deterministic at temperature 0 ([ADR-0018](decisions/0018-llm-call-determinism.md)).

## What Poro does poorly

### Categorization (this project)
- **A strong attractor toward one category.** With the deterministic override *off*, a
  bare fruit and a juice both land in dairy. Measured live (override disabled, real Poro):

  | Input | Poro's raw category | Correct? |
  |---|---|---|
  | `nektariinierä oli homeessa` | `hevi` | ✓ |
  | `maitosuklaa oli sulanut` | `maito_kylma` | ✗ candy, not dairy |
  | `banaani oli mustunut` | `maito_kylma` | ✗ a banana → dairy |
  | `omenamehua ja se oli hapanta` | `maito_kylma` | ✗ juice → dairy |
  | `juustokakku oli kuiva` | `leipa` | ✓ |
  | `tomaatit olivat homeessa` | `hevi` | ✓ |

  It is **inconsistent**, not uniformly bad — right on nektariini/tomaatti/juustokakku,
  wrong on banana/juice/milk-chocolate, and it defaults **service/premises** comments
  (a helpful salesperson, parking-lot cleanliness) to dairy too. Substring proximity
  ("maito" inside "maitosuklaa") and a dairy prior pull it there.
- **Ignores an optional field it is told about.** An optional model-authored `sentiment`
  field was wired into the schema; an announced seed-42 live run showed Poro emitted it
  **0/71** times ([ADR-0031](decisions/0031-model-authored-sentiment-field-optional.md)).
- **Will not self-identify garbage.** A dedicated `ei_palautetta` bucket for nonsense was
  tried; Poro routed **0/3** obvious garbage items to it — automatic garbage detection is
  not achievable with this model ([ADR-0032](decisions/0032-unrated-nonsubstantive-categories.md)).
- **JSON discipline on messy Finnish is unmeasured/unreliable** — the reason a salvage
  layer is mandatory ([ADR-0004](decisions/0004-salvage-layer-mandatory.md)).
- **Garbled input makes it worse.** Display labels glued onto text
  (`KorkeaKielteinenOstamani nektariinierä…`) degraded the pick; on the clean text Poro
  got it right.

### Language drift and translation (mikkonumminen.dev)
- **Mid-answer language drift** — a Finnish question answered in English or vice versa;
  Poro, tuned Finnish-first, drifts to Finnish on English questions.
- **Translation overstep** — translates meaning-bearing proper nouns no matter how firmly
  the prompt forbids it: `"kasvulabs"` → `"Growth Labs"` (measured live).
- **Appends commentary after a perfect translation** — "…work experience?" followed by an
  unbidden "However, considering…".
- **Code-dense Finnish routing fails** — identifier-heavy Finnish dilutes the ä/ö
  language heuristic, so a code-heavy Finnish question can answer in English.
- **Containment is noisy** — whether Poro refuses off-scope questions could not be
  separated from qwen at that sample size (an earlier "Poro worst at containment" claim
  was *retracted* after a rate-limiter contaminated the run — a measurement correcting
  itself).

## Our solutions — patch the failure modes with deterministic architecture

The unifying rule ([ADR-0006](decisions/0006-ai-in-exactly-two-places.md)): **the LLM
lives in exactly two places** (structuring messy input, reading themes at scale); a
deterministic layer runs first and the model may *add*, never *remove or replace*, a
deterministic result. Everything below is that layer.

### In this project
- **Deterministic alert lexicon** ([ADR-0027](decisions/0027-racism-recognition-alert-lexicon.md))
  — a wordlist forces the `rasismi` category (and safety/payment/legal alerts) before
  the model, and outranks it. Precision layer; the LLM is the recall net.
- **Category-keyword override** ([ADR-0036](decisions/0036-deterministic-category-keyword-override.md),
  [ADR-0037](decisions/0037-category-keywords-service-premises.md)) — a per-department
  wordlist forces product departments (produce → `hevi`, and grocery core) and, as a
  fallback, service/premises, with cross-category exclusions so compounds route to what
  they are (`maitosuklaa` → candy). Directly fixes the dairy-attractor errors above.
  Curation is validated by a **corpus false-positive scan** that caught over-forcing
  stems (`kana` matched "aikana"; `kala` matched the fish *department* name; bare `kassa`
  matched premises complaints) before they shipped.
- **Mandatory salvage layer** ([ADR-0004](decisions/0004-salvage-layer-mandatory.md)) —
  fence-strip → schema-validate → normalize → one re-prompt → `structure_failed` with raw
  text preserved; unit-tested against measured failure shapes.
- **`categoryHints`** ([ADR-0028](decisions/0028-categorization-accuracy-makeiset-theme-normalization.md),
  [ADR-0035](decisions/0035-categorization-discipline-muu-single-category-hints.md)) —
  nudge the model on confusable boundaries and novel names the wordlist misses.
- **Deterministic sentiment** ([ADR-0030](decisions/0030-sentiment-indicator-deterministic-from-type.md))
  — derived from the `type` the model already assigns, so no second (ignored) model call.
- **Grounding is structural** ([ADR-0009](decisions/0009-grounding-is-structural.md)) — a
  synthesis claim whose cited feedback IDs fail validation is dropped, not shown; the
  model cannot hallucinate a claim into the management view.
- **The desk correction loop + telemetry** ([ADR-0035](decisions/0035-categorization-discipline-muu-single-category-hints.md))
  — the human-in-the-loop backstop for everything the rules miss, and the *measurement*
  of the model-vs-human correction rate that says which departments to harden next.

### In mikkonumminen.dev (same pattern, different failure)
- **Deterministic entity restoration** — a `KNOWN_ENTITIES` map re-appends the canonical
  spelling when Poro translated a proper noun away (`kasvulabs` → `Kasvu Labs`). This is
  the direct analogue of our keyword override.
- **Language anchoring** — moving Finnish-path anchors from 1 to 3 lifted adherence on
  code-heavy Finnish from **85% → 100%**.
- **Translation truncation** — keep the first non-empty line, cap the runaway, to stop
  appended commentary.
- **Deterministic task gates** — keyword gates block generative off-task requests before
  they reach the model.

## The bottom line

Poro-2-8B is the **best Finnish writer** available at 8B, and genuinely unreliable at
**structured decisions** (categorization, obeying format/scope instructions, staying in
one language). The engineering answer in both projects is the same: **keep the strong
writer, and make every decision it cannot be trusted with deterministic** — a wordlist,
a validator, a gate, or a human — never a hopeful prompt.
