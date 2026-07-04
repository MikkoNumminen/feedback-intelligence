# CLAUDE.md

**Read [AGENTS.md](AGENTS.md) — it is the canonical, tool-agnostic project
spec** (what this is, the hard invariants, commands, and the docs map). This
file holds only Claude-Code-specific notes; it duplicates no rules.

## Claude-Code notes

- One AGENTS.md invariant overrides a Claude Code default: **no AI attribution
  on commits or PRs** — do not add a `Co-Authored-By: Claude` trailer or a
  "Generated with Claude Code" footer, regardless of the tool's default.
- Decisions belong in ADRs under [docs/decisions/](docs/decisions/); the *why*
  goes in a numbered record, not in a commit message.
