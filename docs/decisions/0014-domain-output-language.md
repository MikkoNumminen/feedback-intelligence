# ADR-0014 — Output language is a domain property (English default, retail Finnish)

- **Status:** Accepted (2026-07-05)
- **Deciders:** Mikko
- **Builds on:** [ADR-0012](0012-pluggable-domain-modules.md) (pluggable domain modules)

## Context

The engine's user-facing output — the report's `direction` words and
deterministic fallback prose, and the desk + management frontends — was
hardcoded **Finnish**. That was correct while retail was the only application,
but wrong the moment a second domain exists: **retail is aimed at a Finnish
audience only; the game-studio domain (and every future domain) is aimed at an
English audience.** A game report reading "3 palautetta… Suunta: vakaa" is a
language bug, not a taxonomy one.

The LLM-written narratives already follow each domain's voiced prompt
([ADR-0012]) — retail Finnish, game English. The gap was everything *around* the
model: deterministic prose, labels, and UI chrome.

## Decision

Make the **output/UI language a domain property**: `domain.json` gains
`"language"` (short code, e.g. `"fi"`, `"en"`), exposed as
`DomainDescriptor.Language`. **The core default is `"en"`; retail overrides to
`"fi"`.** So a new domain is English with zero effort, and retail stays Finnish.

Localized by the active domain's language:

- **Report (backend):** the deterministic fallback narrative, the trend/`direction`
  label, and the row labels of the digest fed to the synthesis model. Centralized
  in `ReportText` (fi/en). (Category/severity/type **value** labels are a separate
  domain axis — `categoryLabels`/`severityLabels`/`typeLabels` in `domain.json` —
  not driven by `language`.)
- **`direction` is stored as a language-neutral KEY**
  (`stable/growing/declining/worsening`) with a separate localized
  `DirectionLabel`. The verify gate and report JSON key off the neutral value, so
  machine-checking stays language-independent; the label is presentation only.
- **Snapshot page** (`SnapshotHtml`) — chrome + `<html lang>` follow the language;
  the report carries a top-level `language` so the static snapshot is
  language-correct even when the backend is unreachable.
- **Frontends** (`desk.html`, `index.html`) — an `fi`/`en` string bundle selected
  from `/schema`'s `language` (index also from the report's `language`, so
  snapshot mode is correct offline). Category/severity/type **values** already
  come from the domain via `/schema`.

## Consequences

- Adding a domain means declaring its `language` (or nothing → English). Retail is
  the single Finnish exception, by its audience.
- The report contract gains `ReportTheme.DirectionLabel` and a top-level
  `ManagementReport.Language`. The neutral `direction` key means the `verify` gate
  and its tests key off `stable/growing/declining/worsening` (was Finnish words).
- **Not to be confused with the per-item `language` field** in the feedback schema
  ([schema.md](../schema.md)) — that is the *detected* language of each feedback
  item, kept as-is. `DomainDescriptor.Language` is the *presentation* language of
  the whole domain. Different layers, different meanings.
- **Supersedes the "Finnish for user-facing text" reading** of the older
  documentation invariant: user-facing text is now in the **active domain's**
  language (retail Finnish, game/others English); code, logs, and internal docs
  stay English.
- Verified live (zero-GPU): the same data renders `Suunta: kasvava` /
  "Automaattinen kooste…" under retail and `Trend: growing` / "Automated
  summary…" under game, with an identical neutral `direction` key.
