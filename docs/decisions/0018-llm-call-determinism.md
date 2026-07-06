# ADR-0018 — Prompt files are LLM input bytes: normalize line endings (CRLF flipped the safety alert)

- **Status:** Accepted (2026-07-06)
- **Deciders:** Mikko
- **Follows:** [ADR-0015](0015-poro-real-corpus-tuning.md) (per-item safety
  screen), [ADR-0002](0002-llm-behind-one-abstraction.md) (LLM behind one
  abstraction)

## Context

The demo's centerpiece is the **no-keyword safety alert**: Poro reads an implicit
structural-failure complaint (`core-011`, timber that snapped when walked on) and
flags it `kyllä` via the per-item screen (ADR-0015). A clean-corpus run surfaced
that it **silently stopped firing** — the report's screen returned `ei` for the
safety text, while Poro judged the *same* text `kyllä` in a direct call.

A logging proxy between the API and Ollama captured both requests and diffed
them. The bodies were identical **except line endings**: the app sent the prompt
with Windows **CRLF**, the direct call with **LF**.

- **Poro's greedy (temperature 0) decode is not invariant to line endings.**
  With `\r\n` it answers **`ei`**; with `\n` it answers **`kyllä`** — same prompt
  text, model, and options. The prompt files are stored CRLF (git `autocrlf` on
  a Windows checkout), the API read them verbatim, and the safety alert never
  fired. Every external reproduction used Python, whose default text read
  normalizes CRLF→LF — which is why the bug hid for so long. The wire capture is
  what finally exposed it.

**A temperature red herring, recorded so it is not re-chased.** The first
hypothesis was that the `think:false` escape hatch (`OllamaLlmClientFactory`)
dropped the mapped `ChatOptions`, running calls at Ollama's default 0.8. **This
was wrong**, established by reading the pinned OllamaSharp **5.4.25** source:
`AbstractionMapper.ToOllamaSharpChatRequest` takes the `RawRepresentationFactory`
base request and *backfills* the mapped options onto it
(`request.Options ??= new(); Temperature ??= options?.Temperature;
NumPredict = options?.MaxOutputTokens; TopP/TopK/Seed/Stop via ??=`). So a bare
`new ChatRequest { Think = false }` still sends `temperature 0` / the token cap.
The proxy capture confirms it (alert-verify on the wire: `temperature 0`,
synthesis `0.3`). Two further facts seal it: the alert path uses the *synthesis*
client, which is **not** reasoning-wrapped (`SynthesisDisableReasoning` defaults
false), so it never touches `WithThinkOff` at all; and the CRLF repro is itself
*deterministic*, which requires temperature to already have been 0. **CRLF was
the sole cause.**

## Decision

**Every prompt/template is read through a single normalizer that converts line
endings to LF.** Prompt bytes are LLM *input* — a stray CRLF is a behaviour
change, not cosmetics.

- `AppPathResolver.ReadPromptAsync` / `ReadPrompt` resolve the path and apply
  `NormalizeNewlines` (`\r\n`→`\n`, lone `\r`→`\n`). Wired into all four
  product-path prompt reads: alert-verify, synthesis, alert-nomination
  (`ReportService`), and structuring (`LlmStructuringService`).
- `WithThinkOff` is left as the minimal `new ChatRequest { Think = false }`; it
  does **not** set `Options` (the mapper backfills them — carrying them
  explicitly is a verified no-op, so it was removed to avoid a misleading claim).

## Consequences

- **Safety alert is reliable again.** Post-fix acceptance on the clean seed-42
  corpus: dairy 5/4, availability 5/3, and the no-keyword safety case fires
  (`gen-42-0008`) — deterministically, run to run.
- **Regression guards:** unit tests pin `NormalizeNewlines` and a CRLF file
  round-trip through `ReadPrompt`, plus a call-site test that a CRLF prompt
  reaches the model normalized through the load-bearing read path (so a refactor
  back to raw `File.ReadAllText` fails a test, not a demo).
- **Off-product-path prompt reads** in the generator (`VariantsRunner`) and the
  structuring-eval harness (`StructuringEvalRunner`) are normalized too, so an
  eval instrument is never silently skewed by a CRLF checkout.
- **Lesson:** the earlier "PASS" of the safety alert was not luck-of-temperature
  (temperature was always 0) — it was luck-of-checkout: whoever's prompt file
  happened to be LF passed, CRLF failed. Read every prompt through the normalizer.
- **Provider caveat:** if OllamaSharp is bumped, re-verify that it still backfills
  options onto a raw request; if that changes, `WithThinkOff` would need to carry
  options explicitly (add a wire test then).
