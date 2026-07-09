# Phase 0 — Skill & agent reconciliation — 2026-07-09

## Context
Invoked as stage 5/5 of `/mikko-audit-suite`. The security-audit is the **phase-gated**
stage: it never autopilots and requires explicit user approval between phases.

## Agent availability
The skill's canonical named agents — `security-auditor`, `security-fixer`,
`security-doc-writer` — are **not installed** in this environment (the available agent
roster is `architect`, `scout`, `Explore`, `general-purpose`, etc.). Per the skill's
Phase 0b, this is surfaced rather than silently substituted.

**Decision (orchestrator):** run only the **read-only** phases here —
Phase 1 (attack-surface map) — which the orchestrator can produce directly from the
deep read already performed across the four prior suite audits (dotnet, llm-injection,
ai-smell, robustness). **Stop at the Phase 1 gate.** Phases 2–5 (findings/plan and any
remediation) mutate code or make severity/fix decisions and MUST NOT run without the
owner: they need explicit per-phase approval, and any auth/crypto/secrets or
persistence-layer fix escalates to Opus review (skill rules 6–8). No code is modified in
this stage.

## Repo conventions honoured
- No `Co-Authored-By` / AI-attribution trailers (AGENTS.md invariant, `MEMORY.md`).
- No exploit detail in commit messages (none written — read-only).
- STRONGEST RULE: no merges without fresh per-PR owner approval.

## Gate
Phase 1 map is at `docs/security/01-attack-surface.md`. Reply **approved** to authorise
Phase 2 (findings + remediation plan), or run `/mikko-security-audit` later to resume.
