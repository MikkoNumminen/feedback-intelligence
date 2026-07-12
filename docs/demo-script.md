# 5-minute demo script (draft — rehearse on the real corpus, seed 42)

> STATUS: written against the placeholder run-through; final texts and timings
> land after the real corpus exists (TODO #1) and the rehearsal (TODO #6).

Pre-demo checklist (10 min before):
0. Fastest path: `feedctl data demo` then `feedctl up --supervise` does steps
   1–2 in one go — `--supervise` restarts the API if it dies mid-demo and backs
   off the moment the shared RAG comes up (never contends for the GPU). The
   explicit steps below are the manual form.
1. RAG stack down; `docker compose up -d ollama --wait` (models warm after
   first call; hit `/health` once to load Poro).
2. Fresh demo DBs: delete `data/feedback.db` AND `data/desk-live.db` (the desk's
   own channel, ADR-0024 — stale rehearsal entries would resurface in the desk
   segment; `feedctl data <mode>` wipes both), start API, push the seed-42
   corpus: `tools/push-corpus.ps1 -Corpus data/corpus/generated-42.jsonl`.
3. Open `/` (management view) and `/desk.html` on the phone.

Minute 0–1 — the situation view:
- Open `/`, 7-day window. "Tämä näkymä kokoaa neljä palautekanavaa yhteen."
- Point at the alert on top: the no-keyword safety complaint. "Sanahaku ei
  löytänyt tästä mitään — kielimalli ymmärsi, että teiltä ostetuista laudoista
  rakennettu terassi petti."

Minute 1–2 — grounding:
- Open the dairy theme card: "hyllysaatavuus/tuoreus, suunta paheneva".
- Click through to a cited message → the original customer text opens. "Jokainen väite on
  jäljitettävissä alkuperäiseen palautteeseen — jos mallin väitteelle ei löydy
  lähdettä, väite pudotetaan eikä sitä näytetä."

Minute 2–4 — the desk moment (the centerpiece):
- On the phone, `/desk.html`. Type a fresh dairy complaint in dialect
  ("asiakas sano et maitokaapis taas vanhoi purkkei").
- Show the interpretation appearing BEFORE saving; correct one field (e.g.
  severity) to show human-in-the-loop; save.
- The desk's own segment below refreshes: the entry appears categorized, and
  the AI writes the desk channel's summary live (ADR-0024 — desk entries live
  in their own database, never mixed into the demo corpus).
  "Tiskillä kuultu palaute ei enää kuole vuoron loppuun."

Minute 4–5 — resilience + the design story:
- Kill the backend; reload the shared link → it still renders (the snapshot
  paints first on every load), and the "Tallennettu tilannekuva" badge is what
  proves it is the saved copy, not the live one. "Jaettu linkki ei koskaan näytä
  kuollutta sivua."
- Close: "AI on vain kahdessa paikassa — sotkuisen kielen rakenteistamisessa
  ja teemojen lukemisessa — kaikki muu on sääntöjä, jotka eivät nuku eivätkä
  hallusinoi. Mallin vaihto Azure OpenAI:hin on konfiguraatiomuutos plus
  eval-ajo."
