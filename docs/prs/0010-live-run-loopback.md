# PR #10 — live LLM run-through record + loopback rate-limit exemption

Branch `fix/ratelimit-loopback` → `main` (local; no remote exists).

## What

The record of the first fully-live run (announced GPU window, 2026-07-04,
placeholder data — non-evidential, registered) plus the one fix it surfaced.

Live results: 32/32 items structured by real Poro at ingest (0 failures,
0 normalizations, ~0.6 s/item warm); desk /interpret 0.8 s on dialect with
the "kolmas kerta" escalation read correctly; 20.1 s report with 6 cited
Finnish narratives and 0 dropped claims; LLM budget cap engaged as designed;
`verify` → ACCEPTANCE: PASS, all three stories, zero trend warnings;
telemetry recorded the desk correction. Honest caveat recorded: the
keyword-free alert-nomination case had no candidate in this seed's
realization and waits for the real corpus.

## Fix

The 32-item corpus push could not fit the 30/60 s per-IP window (twice —
409-duplicates consume permits too). Ported the RAG's measured lesson
("never accept throttled data as variance"): loopback callers are exempt
(`Ingest:RateLimitExemptLoopback`, default on). Tunnel traffic is unaffected —
ForwardedHeaders runs before the limiter, so remote clients carry their real
IP and stay limited.

## Review

Inline (primary agent): the change is 15 lines behind an option default; the
exemption logic was verified against the middleware order established in
PR #2/#6 (ForwardedHeaders → CORS → RateLimiter). Rate-limiter behavior is
middleware-level and has no unit seam here; validated operationally during
the live run's throttling and documented.

## Verification

Build clean; 81/81 tests green; live run completed end to end.
