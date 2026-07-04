# ADR-0011 — Sequence-preserving story arcs in the generator

- **Status:** Accepted (2026-07-03)
- **Deciders:** Mikko

## Context

A worsening planted story (e.g. the dairy/freshness signal) must be visible in
the **content**, not only in timestamp density — a mild first remark building to
a severe "third time already" complaint. If the generator assigned story
timestamps randomly within the window, a "third time already" text could land
before the first mild one, breaking the arc the synthesis layer is meant to
read.

## Decision

Story-tagged core items carry an optional **`sequence`** integer (1 = mildest).
Variants inherit their source item's story tag and sequence. `generate` assigns
timestamps **strictly monotonic with sequence** inside the story window (with
worsening frequency-easing on top), and composes exactly **one realization per
step** per set (config `Count` is ignored for sequenced pools). A story pool is
all-sequenced or all-unsequenced — mixing fails loudly.

## Consequences

- Content order equals time order for a sequenced arc; pinned by test.
- Because a mild rewording of a severe text would corrupt the arc, story items
  are multiplied **less** than noise — ×2 via a dedicated intensity-preserving
  prompt (counts, ordinals, frustration level must survive rephrasing), vs. ×6
  for noise. If a variants run shows intensity drift, the fallback is
  `StoryVariantsPerItem = 0` (originals only) — write more originals rather than
  accept a mushed arc.
- Overflow past the window fails loudly rather than silently placing an item
  outside its story window.
