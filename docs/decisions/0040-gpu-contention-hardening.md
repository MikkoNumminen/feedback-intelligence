# ADR-0040 — GPU contention under many simultaneous users: shed, don't queue (and make the layers agree)

- **Status:** Accepted (2026-07-14)
- **Deciders:** Mikko

## Context

The app runs one 8B model (Poro-2-8B, Q4) on a single GPU that is time-shared
with a sibling project's RAG. A demo is shown to a room, so "many people use it
at once" is the expected load, not an edge case. The question: what happens when
concurrent users all need the GPU, and do we queue?

The existing design already sheds rather than queues (`LlmGate`, a 2-slot
semaphore: a caller waits at most `LlmAcquireTimeoutMs = 500 ms` for a slot,
then gets `LlmBusyException` → HTTP 503). A concurrency audit confirmed the
shed is the right call for an interactive desk, but found three sharp edges:

1. **`/health` bypassed the gate.** It fired a real 1-token completion on every
   probe with no slot acquisition, so health checks added uncontrolled
   concurrent GPU calls on top of the 2 slots and could steal a slot from a
   live request under load.
2. **Ollama's own concurrency was unconfigured.** Beneath the app's fast-shed
   gate, `ollama serve` ran at defaults — `OLLAMA_MAX_QUEUE = 512`. A call the
   gate admits could then stall for minutes in a *hidden* deep queue bounded
   only by `LlmCallTimeoutMs = 120 s`. The two layers disagreed: the app
   fast-sheds, Ollama deep-queues.
3. **A shed on the desk had no client resilience.** A single transient 503
   dropped the clerk straight to the busy message, and a roomful of manual
   re-clicks could re-collide in lockstep and also synchronize into the shared
   rate-limit bucket (one Azure egress IP → one 240/60 s bucket, ADR-0025).

### Why not just use Ollama's queue?

Because we do not want a deep queue at all. On one 8B GPU a 512-deep queue means
an interactive "Tulkitse" click could wait minutes; a fast "busy, try again"
beats a spinner that resolves in three minutes. And the app-level gate expresses
policy Ollama cannot: cap *this* app at 2 concurrent so it stays a good neighbour
to the sibling RAG, distinguish shed-vs-degrade per endpoint (ingest sheds and
stores nothing so the client retries; the report swallows the shed and renders
its deterministic layer), and enforce the per-call timeout. Ollama's queue is
tuned for batch throughput, not latency-bounded interactivity. The fix is not to
adopt Ollama's queue but to make Ollama *agree* with the gate.

## Decision

1. **Gate `/health`.** The probe acquires a slot **non-blocking**
   (`LlmGate.TryRunAsync`, `WaitAsync(0)`): if a slot is free it runs the
   1-token completion; if the gate is saturated it returns `status: "ok"`
   without a completion, because a full gate already proves the model is loaded
   and generating. Health can never add GPU load beyond the 2-slot bound nor
   steal a slot from a live request. A genuine failure (unreachable, or slower
   than `HealthTimeoutSeconds = 10 s`) still returns `503 llm_unavailable`.
2. **Make Ollama agree with the gate** (`docker-compose.yml`):
   `OLLAMA_NUM_PARALLEL = 2` (both slots the gate admits actually run in
   parallel instead of being serialized) and `OLLAMA_MAX_QUEUE = 8` (shallow, so
   Ollama sheds too rather than hiding a 512-deep backlog under the app's
   fast-shed design). Both env-overridable; `NUM_PARALLEL = 2` is safe on VRAM
   because this container runs only while the shared RAG is down, so the whole
   GPU is ours.
3. **Client resilience on the desk** (`desk.html`): a 503 on interpret retries a
   few times with **jittered** backoff (`300 + attempt·400 + random·500 ms`, up
   to 2 retries) so transient sheds self-heal and a roomful of retries
   desynchronize instead of re-colliding; then it falls to the manual busy
   message. A 429 (shared rate bucket, not the GPU) is never retried — it shows
   a distinct "too many requests, wait" message.

## Consequences

- The two concurrency layers now express the same policy (2 parallel, shallow
  queue, shed fast) instead of fighting; there is no hidden deep queue under the
  gate.
- Health is honest under load: busy → healthy without adding load; idle → real
  probe; dead/slow → `503 llm_unavailable`. `/health` consumers
  (`feedctl` board, the public `/api/health` proxy probe) read `status == "ok"`
  and need no change.
- A transient GPU shed on the desk mostly self-heals silently; a sustained one
  still shows the busy message quickly. Auto-retry is **bounded** (≤2, jittered)
  so it smooths sheds without becoming a retry storm.
- **Accepted, not fixed:** `POST /live/restructure` still holds a slot per item
  in a loop with no priority over live visitors. It is operator-only
  (loopback-exempt, absent from the public proxy allowlist), so only the
  operator can trigger it and they know a batch is running; a per-caller slot
  reservation would be a larger change than the risk warrants.
- **Accepted, not fixed:** the browser-path rate-limit bucket is shared across
  all public visitors (Azure egress IP). At demo scale (240/60 s ≈ 4 req/s
  sustained) this is ample; the client-side 429 message + jittered 503 retry are
  the mitigation. A per-visitor bucket would require the proxy to forward a
  stable per-client key, which the SWA managed function does not.
