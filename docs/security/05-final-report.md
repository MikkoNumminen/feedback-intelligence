# Phase 5 — Final verification report — 2026-07-09

Security audit complete. All findings are either remediated (with tests green) or
accepted-and-documented by owner decision. No finding remains open.

## Findings disposition

| # | Finding | Severity | Disposition |
|---|---|---|---|
| S1 | No auth on any endpoint | informational (accepted) | ✅ **Accepted** — owner confirmed synthetic-demo data (2026-07-09); posture documented in `SECURITY.md`. Becomes high only if real data is introduced. |
| S2 | Unauthenticated LLM-work DoS amplification | high | ✅ **Fixed** — server-side `CancelAfter(LlmCallTimeoutMs)` in `LlmGate.RunAsync` (commit `503c9ff`). |
| S3 | Unconditional PNA grant | low | ✅ **Fixed** — gated on the CORS Origin allowlist (commit `6091815`). |
| S4 | Snapshot write integrity | medium | ✅ **Fixed** — per-writer unique temp name + cleanup (commit `6091815`). |
| S5 | XFF trust vs loopback rate-limit exemption | low (accepted) | ✅ **Accepted** — owner confirmed tunnel-only ingress (2026-07-09); no direct-reach spoof path. Hardening noted for a future direct-bind. |

## Verification performed

- **Build:** `dotnet build -c Release` → **0 errors** (10 pre-existing benign warnings: OllamaSharp analyzer version mismatch × 8, xUnit1031 test-only × 2).
- **Tests:** `dotnet test -c Release` → **170 passed, 0 failed** (Api 74 · Llm 71 · Generator 25). The refactors behind S3/S4 and the robustness fixes introduced no regression.
- **Fix presence re-confirmed in source:**
  - `LlmGate.RunAsync` wraps `work` in a linked `CancelAfter(options.Value.LlmCallTimeoutMs)`; `IngestOptionsValidator` requires the timeout > 0.
  - PNA middleware checks `AllowedCorsOrigins.Contains(origin)` before granting.
  - `WriteAtomicAsync` uses `$"{path}.{Guid.NewGuid():N}.tmp"` with failure cleanup.
  - `/interpret` catches non-cancellation exceptions → 503.
  - `FeedbackStore.OpenConnectionAsync` sets `PRAGMA busy_timeout=5000`.

## Injection posture (restated, honestly)

All six defense-in-depth layers are present and wired at every prompt boundary (see
[`invariants.md`](invariants.md) INV-1..INV-6 and
[`../audits/llm-injection-2026-07-09.md`](../audits/llm-injection-2026-07-09.md)).
This is **defense-in-depth, not a proof** — prompt injection remains unsolved. The
named residuals (homoglyph markers, paraphrased directives) are pinned in the
red-team fixture so they stay visible.

## Audit status: COMPLETE

Phases 0 ✅ · 1 ✅ · 2 ✅ · 3 ✅ (S2/S3/S4 fixed; S1/S5 accepted) · 4 ✅ · 5 ✅.
No open findings. Re-audit if the operating model changes (real data, a
directly-reachable bind, a new endpoint, or a model/provider swap).
