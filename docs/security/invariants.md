# Security invariants

The load-bearing properties that must not regress. Each names the mechanism, where
it lives, and the test that guards it. If you change code that touches one, keep the
invariant true or update this file with the ADR that changes it. These are the
"tripwires" — a regression here is a security regression, not a normal bug.

## LLM boundary (ADR-0021)

- **INV-1 — Neutralize before every splice.** Untrusted feedback text passes
  through `Core/Security/UntrustedText` (fence markers stripped to a fixpoint;
  control chars + U+2028/U+2029 collapsed; quotes neutralized) before entering
  *any* model prompt — structuring, synthesis, alert-verify, nomination.
  *Guard:* `UntrustedTextTests`, `RedTeamCoverageTests` (`neutralized` cases), and
  the `TryLlmAsync` neutralize-at-the-seam contract comment.
- **INV-2 — Deterministic alert layer is authoritative.** `AlertMatcher` runs first
  and independent of the LLM; the model may **add** an alert nomination but can
  never **remove** a deterministic one. *Guard:* `AlertMatcherTests`, ingest order
  in `IngestService`.
- **INV-3 — No model output drives an irreversible action directly.** Load-bearing
  decisions (alerts) are owned by the deterministic layer; the model only nominates,
  bounded and grounded. *Guard:* `ReportServiceTests` (grounding/authority), ADR-0021 E.
- **INV-4 — Every model-authored, human-facing slot is authority-bounded and
  grounded.** `NarrativeGuard` drops directive narratives/titles/alert-reasons to a
  deterministic fallback; cited ids are validated against the provided set or the
  narrative is dropped. *Guard:* `NarrativeGuardTests`, `ReportServiceTests`.
- **INV-5 — Injection symptoms are flagged, never silently dropped.**
  `InjectionSignals` raises `needs_review` (preserving the item) and escalates on a
  severe-rating co-occurrence. *Guard:* `InjectionSignalsTests`, `RedTeamCoverageTests`.
- **INV-6 — The red-team fixture stays green.** ≥12 injection vectors + benign
  controls, each pinned to its expected layer; named residuals (homoglyph) are
  asserted as residuals, not silently assumed closed. *Guard:* `RedTeamCoverageTests`
  (`Fixture_CoversEveryAttackClass_WithBenignControls`).

## Ingress & resources

- **INV-7 — Input is bounded before any LLM work.** Body size (`Ingest:MaxBodyBytes`)
  and text length (`Ingest:InputMaxChars`) are enforced in `RequestValidator` /
  Kestrel *before* the model is called. *Guard:* `RequestValidatorTests`.
- **INV-8 — Every request-path model call is gated and time-bounded.** All model
  calls flow through `LlmGate.RunAsync`, which sheds (503) when concurrency is
  exhausted and cancels after `Ingest:LlmCallTimeoutMs` so a hung generation frees
  its slot. *Guard:* covered by `LlmGate` construction + `IngestOptionsValidator`
  (positive timeout); behavioural addition of this PR.
- **INV-9 — All SQL is parameterized.** No SQL string is built from input;
  `FeedbackStore` binds every value via `SqliteParameter`. *Guard:* code review +
  `IngestServiceTests` (round-trip).
- **INV-10 — No request input reaches a filesystem path.** Prompt/domain/snapshot
  paths come from config/descriptors only. *Guard:* `AppPathResolverTests`.

## Cross-origin

- **INV-11 — Cross-origin and private-network access are allowlisted.** CORS uses an
  explicit Origin allowlist with no credentials; the PNA preflight grants access
  only to an allowlisted Origin (not unconditionally — hardened this PR).
  *Guard:* `IngestOptionsValidator` (origin shape) + `Program.cs` PNA middleware.

## Posture (documented, not a code invariant)

- **P-1 — No authentication, by the demo operating assumption.** See
  [`SECURITY.md`](../../SECURITY.md). If real data is introduced this stops being a
  posture and becomes finding **S1**.
