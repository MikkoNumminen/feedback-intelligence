# ADR-0008 — SQLite over PostgreSQL for storage

- **Status:** Accepted (2026-07-03)
- **Deciders:** Mikko

## Context

The brief allowed either SQLite or PostgreSQL: one feedback table with the
structure stored as a JSON column, explicitly **not** a normalized category
hierarchy (structure is the AI's *output*, not an input form's requirement).

## Decision

**SQLite** (`Microsoft.Data.Sqlite`) — a single `feedback` table with the
structure as a JSON column and a `corrections_json` audit field.

## Consequences

- Keeps the demo **self-contained**: no separate database service to stand up,
  the whole thing runs from `dotnet run`.
- Timestamps are stored normalized to one fixed-width UTC round-trip format
  because SQLite compares them lexically — mixed client offsets would otherwise
  produce wrong window queries.
- Nothing in the schema-as-JSON-column approach is Postgres-specific, so a later
  move to PostgreSQL is a storage-adapter change, not a data-model change.
