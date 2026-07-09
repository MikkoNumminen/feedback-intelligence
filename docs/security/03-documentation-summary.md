# Phase 4 — Documentation summary — 2026-07-09

AI-first security documentation written this phase (read-only; no code changed):

| Artifact | Purpose |
|---|---|
| [`SECURITY.md`](../../SECURITY.md) (root) | Operator-facing posture: the demo operating assumption, what is/isn't defended, the injection-honesty statement, reporting. |
| [`docs/security/threat-model.md`](threat-model.md) | Assets, actors, trust boundaries, and the threat → mitigation → residual table (T1–T9). |
| [`docs/security/invariants.md`](invariants.md) | The 11 load-bearing security invariants + the documented no-auth posture, each with its guarding test. |

## What this phase deliberately did NOT do

- **Did not add authentication** (finding **S1**) — that decision belongs to the
  owner and depends on whether real data will flow (see `02-findings-and-plan.md`).
  The docs frame no-auth as an explicit, confirmable *operating assumption*, not a
  silent design choice.
- **Did not change the forwarded-header config** (finding **S5**) — needs the
  owner's confirmation of the ingress model (tunnel-only vs also-direct) first.
- **Did not touch code** — Phase 4 is documentation only. The code remediations
  (S2/S3/S4 + robustness findings) landed in the earlier commits on this branch.

## State of the security audit

- Phase 0 (reconciliation) ✅ · Phase 1 (attack surface) ✅ · Phase 2 (findings/plan) ✅ · **Phase 4 (docs) ✅**
- Phase 3 (remediation): S2/S3/S4 done as robustness fixes; **S1/S5 await the owner decision.**
- Phase 5 (verification) pending — run after S1/S5 are resolved and any resulting fixes land.

## Open decisions carried forward

1. **S1** — synthetic-demo-by-design (confirm `SECURITY.md`'s assumption) or real
   data (plan an auth control)?
2. **S5** — is the backend tunnel-only (safe) or also directly reachable (pin
   `KnownProxies` / bind loopback-only)?
