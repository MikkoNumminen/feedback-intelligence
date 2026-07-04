# ADR-0006 — AI in exactly two places (the four-round elimination)

- **Status:** Accepted (2026-07-03)
- **Deciders:** Mikko

> Transcribed and translated from the design-rationale document *"Miksi tässä
> järjestelmässä on niin vähän tekoälyä"* (M. Numminen, July 2026) — a companion
> to the README. The original Finnish is the fuller source; this ADR preserves
> its reasoning in-repo. The starting point was unusual: two decades of Finnish
> retail shop-floor and supervisor experience *before* software, used to look
> for where AI belongs in an environment known from the inside. That turned out
> harder than expected — which is the whole point.

## Context

Four AI ideas were considered and rejected before a fifth survived scrutiny. The
elimination itself is part of the work sample.

- **Round 1 — task management / daily-ops app.** Track, check, assign, and
  sign off store tasks. *Where would AI help?* Honestly nowhere: task CRUD is
  deterministic; a chat window is an *interface*, not a benefit — and a poor one
  between the shelves. **Rejected** — AI for AI's sake.
- **Round 2 — situational reasoning / task generation from data.** Read stock,
  sales, delivery notes, season, weather → a prioritized daily task list, on the
  theory that combinations of signals explode combinatorially and need
  reasoning. Tested against experience: how much of a store morning's
  prioritization is *actually* interpretive? Astonishingly little. Retail
  operations are among the most process-covered environments on earth precisely
  because thousands of professionals spent decades removing interpretation from
  them; clear signals, known actions, ready escalation paths. **Rejected** — a
  checklist with failing events in an attention colour does it: deterministic,
  fast, never hallucinates.
- **Round 3 — customer-message drafting.** Auto-compose "your product has
  arrived", human approves with a click. The information a customer needs is
  binary (is there product or not); they neither need nor should get a narrative
  of why an event failed. A pre-written template beats generated prose —
  deterministic, testable, never hallucinates. **Rejected** — and cut from scope
  entirely.

**The turn: the dead-end was a measurement.** Three rounds of the same pattern —
invent an AI idea, decompose it into deterministic parts, find no AI-shaped gap
left — is not failure but a *repeatable result*, and a repeatable result is a
finding: **store operations are rule-coverable because the industry spent
decades making them so.** Don't look for AI where the process works; look where
the process *cannot be written in advance*. The right question is not "where
would AI be useful" but **"what here requires an experienced human and won't
bend to a rulebook?"** The answer: interpreting unstructured language — reading a
customer's vague problem, fusing hundreds of free-text signals into a trend. The
genuinely hard things in retail live at the customer interface, in language, not
in operations.

## Decision

AI is used in **exactly two places**; everything else is deterministic.

1. **Input side — structuring free speech.** A rule system cannot take "asiakas
   sano et maitokaapis oli vanhoi purkkei" and derive department, theme, and
   severity; free spoken language does not bend to if-statements. The worker
   writes one sentence as it comes; the LLM structures it; the human accepts or
   corrects in one click. AI's role is to **remove the logging friction** that
   otherwise kills the richest feedback channel (spoken at the counter, gone at
   shift's end) — **not** to interpret unsupervised. Friction removal decides
   whether the data exists at all.
2. **Read side — theme/trend synthesis.** Reading and summarizing hundreds of
   weekly free-text signals into a management situational picture
   ("shelf-availability complaints in dept 4 tripled, root cause looks like the
   same order chain") is work nobody does today because it would need a human to
   read everything. It cannot be done by rules: free text has a thousand ways to
   say the same thing, and "terassin kansi petti viikossa" (the deck collapsed
   in a week) contains no alert keyword yet is a safety signal. Accepted **with
   a division of labour**: a deterministic layer in front (known alert words —
   payment, safety, legal — fast, cheap, never sleeps), the LLM behind it
   reading what the rules miss, and **every LLM claim grounded to the original
   feedback. AI reads, the human decides.**

## Consequences

Design principles that crystallized from the process (they govern the whole
system, not just this decision):

- **AI's place is not where the process works, but where the process cannot be
  written in advance** — in practice, unstructured language. Suspect everything
  else as rules first.
- **"Where would AI be useful" is the wrong question.** Ask what requires an
  experienced human and won't reduce to a rulebook.
- **Deterministic layer first, LLM behind it.** A rule that works always beats a
  model that usually works. The LLM may **add**, never **replace**.
- **Grounding is not a feature but a condition of existence.** A claim you cannot
  click open to its sources does not reach the screen. (See
  [ADR-0009](0009-grounding-is-structural.md).)
- **A rejected idea is a result, not waste.** Three decomposed candidates gave
  the certainty that the fourth and fifth are right — and an architecture whose
  boundary falls at a justified place.
- Domain experience's role was decisive but counter-intuitive: it did not say
  where to put AI, but where **not** to. Twenty years on the floor meant every
  tempting idea hit the memory of how the thing is really handled — usually
  already well, without AI. What remained is what nobody handles: the language
  nobody has time to read, and the signal nobody has time to log.

Customer-message drafting is explicitly **out of scope** as a consequence of
Round 3; see the scope-out list in [`../../AGENTS.md`](../../AGENTS.md).
