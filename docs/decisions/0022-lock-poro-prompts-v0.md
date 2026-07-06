# ADR-0022 — Lock the retail Poro prompts at v0 (a change is gated, not forbidden)

- **Status:** Accepted (2026-07-07)
- **Deciders:** Mikko
- **Follows:** [ADR-0003](0003-poro-for-both-roles.md) (Poro for both roles),
  [ADR-0002](0002-llm-behind-one-abstraction.md) (model is config, swappable),
  [ADR-0018](0018-llm-call-determinism.md) (prompt **bytes** are input — a CRLF
  flip moved the safety alert), [ADR-0021](0021-prompt-injection-defense-in-depth.md)
  (A4 red-team fixture — a marker "tidy" can reopen a closed hole)

## Context

The retail model-facing prompts are the demo's quality surface, and by ADR-0018
their **bytes** are a real input: a lone CRLF flip once moved a safety alert, and
by ADR-0021 a careless reword can reopen an injection hole a data-guard line
closed. They are also *validated* — the seed-42 live run through real Poro
produced 0 dropped claims with correct grounded alerts, and the A4 live tier
confirmed the injection posture on exactly these prompts. That validation is only
worth anything if the text that was validated is the text that ships.

Nothing today stops a silent edit — a "tidy", a merge, an editor re-encoding —
from changing a validated prompt and quietly regressing synthesis quality or
injection posture with a green build. The quality pass for these prompts is
done (item #19); what was missing is a guard that makes the *next* change
announce itself.

This locks the prompt **text**, not the model binding. Model + provider stay
config and swappable (ADR-0002); "switch to Azure OpenAI" is still a config
change — this ADR is what makes the "+ re-run the eval" half of that non-skippable.

## Decision — freeze at v0, gate the change

The retail live-path prompts are frozen at **v0**, pinned by a
newline-normalized SHA-256 in `PromptLockTests`
(`tests/FeedbackIntelligence.Api.Tests`). Any edit — including a
semantically-equivalent reword or a lone line-ending flip — makes a **RED**
build until the pin is updated.

Locked (the model's ingest + live-report path):

- `prompts/structuring-v0.txt` (shared ingest structuring)
- `domains/retail/prompts/synthesis-v0.txt`
- `domains/retail/prompts/alert-nomination-v0.txt`
- `domains/retail/prompts/alert-verify-v0.txt`

**Changing a locked prompt is allowed, but gated.** The failing test states the
procedure verbatim:

1. Re-run the A4 red-team fixture (`RedTeamCoverageTests`) — must stay green.
2. An **announced** live check: a seed-42 report through real Poro shows 0
   ungrounded and 0 action drops, alerts grounded to real ids (the same bar the
   original validation met).
3. Update the pinned hash to the new value **in the same commit**, citing the
   re-check in the message.

**Hashing is newline-normalized** (`\r\n`/`\r` → `\n`, then UTF-8 bytes): the
lock guards *content*, so a CRLF (Windows dev) vs LF (ubuntu CI) checkout never
trips it. This is complementary to ADR-0018 — that ADR requires the *runtime*
prompt to be byte-stable; this guards the *committed* text from silent drift.

**Explicitly OUT of the lock:**

- **Game-domain prompts** (`domains/game/prompts/*`) — placeholder and
  unvalidated. Pinning unvalidated text would be false assurance; they get a
  hash when the game corpus and its live check land.
- **Offline generator prompts** (`prompts/variants-v0.txt`,
  `prompts/variants-story-v0.txt`) — they run at corpus-generation time, not on
  the model's live report path, and their output is committed, reviewed corpus,
  not user-facing text. Governed by the corpus review, not this lock.

## Consequences

- The re-measurement half of "switch model/provider = config + re-run the eval"
  is now enforced: a prompt or model swap that skips the A4 + live check leaves
  the build red or ships an unvalidated prompt against a stale hash — visible,
  not silent.
- Cost is one deliberate hash update per intentional prompt change. That is the
  point, not a side effect — the friction *is* the announcement.
- Not a wall: the lock cannot tell a good reword from a bad one; it forces the
  human to run the checks that can. It converts "someone changed a validated
  prompt and nobody noticed" into a red test.
- Delivers the Phase-0-closure quality-pass item (#19): the prompts had their
  quality pass, and are now pinned so the validation stays meaningful.
