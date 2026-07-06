# ADR-0018 — LLM calls must be deterministic and prompt-byte-stable (the safety alert was never reliable)

- **Status:** Accepted (2026-07-06)
- **Deciders:** Mikko
- **Follows:** [ADR-0015](0015-poro-real-corpus-tuning.md) (per-item safety
  screen), [ADR-0002](0002-llm-behind-one-abstraction.md) (LLM behind one
  abstraction)

## Context

The demo's centerpiece is the **no-keyword safety alert**: Poro reads an
implicit structural-failure complaint (`core-011`, timber that snapped when
walked on) and flags it `kyllä` via the per-item screen (ADR-0015). A
clean-corpus run surfaced that it **silently stopped firing**. Poro judged the
text correctly in isolation (`kyllä`) but the app's screen returned `ei`.

A logging proxy between the API and Ollama captured both requests and diffed
them. Two independent defects, both reproduced from the wire:

1. **Prompt line endings flip the answer.** The prompt files are stored with
   Windows **CRLF**. Poro's greedy (temperature 0) decode is *not* invariant to
   it: with `\r\n` it answers **`ei`**, with `\n` it answers **`kyllä`** — same
   prompt text, same model, same options. The API read the file verbatim (CRLF)
   and the safety alert never fired. External reproductions used Python, whose
   default read normalises CRLF→LF, which is why the bug hid for so long — the
   wire capture is what exposed it (the two request bodies differed by exactly
   `\r\n` vs `\n`).
2. **`ChatOptions` were dropped.** The `think:false` escape hatch
   (`OllamaLlmClientFactory`) returned a bare `new ChatRequest { Think = false }`
   from `RawRepresentationFactory`; OllamaSharp used it as the base and did **not**
   overlay the mapped `ChatOptions`, so `temperature` and the token cap were lost
   and every call ran at Ollama's default **0.8**. That made the "deterministic"
   paths (structuring `temp 0`, alert-verify `temp 0`) actually random — the
   safety alert was a ~2/3 coin flip. ADR-0015 passed on a good roll.

Together: at CRLF + temp 0.8 the safety alert was unreliable; the earlier
"PASS" was luck, not correctness.

## Decision

**Every LLM call must be deterministic where configured, and prompt bytes must
be stable across OS / editor / git checkout.**

1. **Normalise prompt line endings to LF on load.** All prompts/templates are
   read through `AppPathResolver.ReadPromptAsync` / `ReadPrompt`, which applies
   `NormalizeNewlines` (CRLF→LF, lone CR→LF). Wired into the alert-verify,
   synthesis, alert-nomination and structuring load paths. LF is the correct
   branch (it yields the right judgment) and, more importantly, makes behaviour
   independent of how the file was checked out.
2. **Carry `ChatOptions` onto the raw think-off request.** `WithThinkOff` now
   builds the `RawRepresentationFactory` `ChatRequest` with an explicit
   `Options { Temperature, NumPredict }` from the incoming `ChatOptions`, so the
   configured temperature and token cap reach Ollama. Verified on the wire:
   alert-verify now sends `temperature 0 / num_predict 12`, synthesis
   `0.3 / 700`.

## Consequences

- **Safety alert is reliable.** Post-fix acceptance on the clean seed-42 corpus:
  all three planted stories ground (dairy 5/4, availability 5/3) and the
  no-keyword safety case fires (`gen-42-0008`) — deterministically, run to run.
- **Structuring is deterministic** (confirmed: 4/4 identical `/interpret` on the
  same dialect text), which the temp-0 design always intended.
- **Regression guards:** a unit test pins `NormalizeNewlines` and that a CRLF
  template round-trips to LF through `ReadPrompt`.
- **Known caveat:** the wire capture showed OllamaSharp does *not* honour the raw
  request's `Think` field in this version (only `Options` survived). Harmless for
  Poro (not a reasoning model); if a reasoning model (e.g. qwen3) is ever
  configured for a role, reasoning-suppression must be re-verified on the wire.
- **Lesson recorded:** prompt files are LLM *input bytes*; a stray CRLF is a
  behaviour change, not cosmetics. Read every prompt through the normaliser.
