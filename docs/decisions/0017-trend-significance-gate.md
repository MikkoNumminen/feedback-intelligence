# ADR-0017 — Trend direction requires statistical significance (no hallucinated trends on organic noise)

- **Status:** Accepted (2026-07-06)
- **Deciders:** Mikko
- **Follows:** [ADR-0009](0009-grounding-is-structural.md) (grounding is
  structural: deterministic layer 1 + grounded LLM layer 2),
  [ADR-0010](0010-verify-gate-tiering.md) (trend is a warning tier)

## Context

Trend direction (`vakaa`/`kasvava`/`laskeva`/`paheneva`) is a **deterministic**
layer-1 output: the report splits a category group at the window midpoint and
compares halves. The original rule declared a trend when one half exceeded the
other by `1.25×` (min 3 items) — and `paheneva` (worsening) additionally when
average severity rose over a non-empty early half.

The build plan names the risk directly: *"raw randomness produces runs with …
hallucinated trends."* Every eval so far used **planted stories**, so the trend
claims were always real — the organic case was never measured.

**Measured it** (`OrganicNoiseTests`, 40 seeds × 80 uniform-in-time items, no
planted story — so any non-`stable` direction is a false positive):

- **86% of category groups with ≥3 items were labelled with a false trend**
  (growing/declining/worsening), including **22 false `paheneva`**. On real
  organic retail feedback the report would slap a confident direction on ~6 of 7
  departments — all sampling artifacts. The IDs are grounded; the *direction
  claim* is noise. That violates "never present a trend it cannot source."

Root cause: a `1.25×` split over 3 items (e.g. 1 vs 2) is statistically
meaningless. Under uniform arrival the second-half count is ~`Binomial(n, 0.5)`,
so a 1-vs-2 split is well within one standard deviation of chance.

## Decision

**A trend is reported only when the volume split is statistically significant.**
Under H0 (uniform arrivals) the `(second − first)` gap has sd `√n`, so require
`|second − first| ≥ z·√n`, plus a minimum group volume. Worsening keeps its extra
severity-rise-over-non-empty-early-half condition.

The decision is a pure function, `ReportService.TrendDirection`, with two config
knobs validated at startup (config-over-hardcoding):

| Knob | `appsettings.json` `Report:` | Default | Meaning |
|---|---|---|---|
| `MinItemsForTrend` | | 6 | groups below this are always `stable` |
| `TrendSignificanceZ` | | 1.6 | required significance of the volume split, in σ |

**Defaults chosen by measurement, not opinion** — the sweep over the 40-seed
noise set and canonical story shapes:

| setting | false-trend rate | false-`paheneva` | keeps moderate story (3/9)? |
|---|---|---|---|
| old `1.25×`, min 3 | **86%** | 22 | — |
| min 6, z=1.6 (**chosen**) | **6%** | ~1% | **yes** |
| min 6, z=1.8 | 2.6% | ~0.5% | no (drops to `stable`) |
| min 6, z=2.0 | 1.9% | ~0.2% | no |

`z=1.6` is the knee: it cuts hallucinated trends ~14× while still detecting both
**strong** (2 low / 10 high) and **moderate** (3 low / 9 high) real stories.
`z≥1.8` is cleaner on noise but starts calling moderate real stories `stable`, so
we keep story **recall** and accept ~6% residual mild noise (mostly
growing/declining; false `paheneva` ~1%). The bias is deliberate: **a missed weak
trend reads `stable` (honest); a hallucinated one is the sin.** Both knobs are
config, so a domain or corpus that wants a different point on this curve retunes
without code.

## Consequences

- A weak split that used to read as a trend (e.g. 2 vs 6) is now `stable`. The
  two existing direction unit tests were updated: the worsening fixture is
  strengthened to a significant shape (3/9), and a new test pins that a weak 2/6
  split is `stable` — the intended behavior change, not a regression.
- `OrganicNoiseTests` is the standing regression guard: false-trend rate < 10%
  and false-`paheneva` < 3% on the noise set, plus a table of canonical shapes
  that must still detect.
- **Demo benefit:** the management view no longer decorates noise departments
  with spurious `kasvava`/`laskeva` alongside the real planted-story trends.
- Domain-neutral: the gate is pure math on counts/severity ranks; a new domain
  inherits it. The verify gate (generator) still treats trend as a WARNING tier,
  so a real story that reads `stable` under the gate loudly warns but never fails
  acceptance — consistent with [ADR-0010](0010-verify-gate-tiering.md).
