# Structuring eval — 2026-07-03 05.10

> ⚠ **NON-EVIDENTIAL — PIPELINE TEST ONLY.** Inputs are synthetic,
> LLM-generated placeholder texts. Per the CLAUDE.md hard rule these
> results prove the pipeline and MUST NOT be used to pick the
> structuring model. The model decision waits for the hand-written corpus.

- Items: 9; repetitions: 3; temperature: 0; max output tokens: 512; reasoning off via /no_think soft switch: True
- Prompt: `structuring-v0.txt` (identical for all candidates)
- Primary metric is PROMPT-ONLY JSON discipline — no constrained decoding.

## Summary

| metric | Llama-Poro-2-8B-Instruct-GGUF:Q4_K_M | qwen3:8b |
|---|---|---|
| strict JSON rate | 0% (0/27) | 22% (6/27) |
| parseable rate (incl. salvaged) | 100% (27/27) | 22% (6/27) |
| schema-adherent rate (of all calls) | 89% (24/27) | 22% (6/27) |
| consistency (items where all reps agree) | 89% (8/9) | 22% (2/9) |
| latency mean | 1264 ms | 4898 ms |
| latency p50 | 664 ms | 4798 ms |
| latency max | 6841 ms | 11694 ms |

## Per-field violations

### Llama-Poro-2-8B-Instruct-GGUF:Q4_K_M

| field | kind | value | count |
|---|---|---|---|
| department | non_string | ["maito_kylma", "tyokalut"] | 3 |

### qwen3:8b

(none)

## Side by side (sensibility judgment)

Majority result over repetitions as department/severity/type, then theme of the first adherent repetition.
⚠ = repetitions disagreed; ✗ = no schema-adherent output at all. (n/m✓) = adherent repetitions.

| id | text | Llama-Poro-2-8B-Instruct-GGUF:Q4_K_M | qwen3:8b |
|---|---|---|---|
| ph-001 | asiakas valitti että maitohyllyssä oli eilen vanhentuneita j… | maito_kylma/medium/complaint “tuotteiden_laatukysymys” (3/3✓) | maito_kylma/high/complaint “tuotteiden tuoreus” (3/3✓) |
| ph-002 | Kassajonot ovat perjantaisin aivan liian pitkät, vain kaksi … | kassa_palvelu/medium/complaint “jonotusajat” (3/3✓) | ✗ |
| ph-003 | Puutarhaosaston palvelu oli erinomaista, sain hyvät neuvot k… | piha_puutarha/low/praise “palvelu_ja_neuvonta” (3/3✓) | ✗ |
| ph-004 | Hei, olen kahdesti käynyt kysymässä kestopuuta 28x95, ja hyl… | rakennustarvike/medium/complaint “tavaran saatavuus” (3/3✓) | ✗ |
| ph-005 | Ostin teiltä lautoja terassiin viime kuussa ja nyt yksi aske… | muu/high/complaint “tuotteen_laatuvirhe” (3/3✓) | ✗ |
| ph-006 | leipäosastolta loppu ruisleipä taas, asiakas kysyi miksei ti… | leipa/medium/complaint “tuotteiden saatavuus” (3/3✓) | leipa/medium/complaint “tuotteiden saatavuus” (3/3✓) |
| ph-007 | Verkkokaupan toimitus myöhästyi kaksi päivää eikä kukaan ilm… | verkkokauppa_toimitus/high/complaint “toimituksen myöhästyminen” (3/3✓) | ✗ |
| ph-008 | Trevlig butik men kassakön var alldeles för lång på lördagen… | kassa_palvelu/medium/complaint “odotusaika_kassalla” (3/3✓) | ✗ |
| ph-009 | Maito oli hapanta ja lisäksi työkaluosastolla ei ollut ketää… | ✗ | ✗ |
