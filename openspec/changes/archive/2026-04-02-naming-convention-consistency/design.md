## Context

The mod exposes game state as JSON over WebSocket. Field names in that JSON become the shared vocabulary between the C# serializer and the TypeScript frontend. Currently some field names use internal engine terms (`playPoint`, from `BattleUnitModel.PlayPoint`) while the frontend component that displays them uses the player-facing name (`LightDisplay`). C# internal class names also appear in TSDoc comments, leaking implementation details that have no meaning outside the game engine.

The established convention going forward:
- C# variable/property names: internal engine terms (unchanged, never crosses the API boundary).
- JSON field names and their TS mirror types: player-facing terms.
- Vue component file names: player-facing terms (already mostly correct).

## Goals / Non-Goals

**Goals:**
- Rename the three Light-resource JSON fields and their TS counterparts.
- Rename `AbnormalityPicker` to accurately reflect its dual role.
- Remove all C# class name references from TSDoc in frontend files.

**Non-Goals:**
- Renaming any C# variables, properties, or methods.
- Renaming any other JSON fields (the rest already use player-facing or neutral terms).
- Changes to runtime behavior, action payloads, or game logic.

## Decisions

### D1: Rename at the serializer, not via an adapter layer

**Decision:** Change the string literals in `GameStateSerializer.cs` directly.

**Rationale:** The serializer owns the JSON contract. An adapter that translates field names at runtime would add indirection with no benefit — there is only one producer (the mod) and one consumer type (the frontend client). The delta engine operates on arbitrary JSON keys and needs no changes.

**Alternative considered:** Keep the C# field names as-is and alias in the TS type. Rejected because it would leave the JSON contract using an internal name, which is exactly what we're trying to fix.

### D2: Rename `AbnormalityPicker` → `EmotionUpgradePicker`

**Decision:** Rename the file and all references.

**Rationale:** The component is shown at every emotion level-up event. At level 3 it presents key-page choices; at levels 4–5 it presents abnormality card choices. `AbnormalityPicker` describes only the second case and would confuse anyone looking at the component during a key-page selection event. `EmotionUpgradePicker` names the trigger (emotion level-up) and covers both cases. `AbnormalityPageCard.vue` is left unchanged — it correctly names a single card type.

**Alternative considered:** `EmotionCardPicker`. Rejected because "emotion card" doesn't include key pages, which suffer the same naming problem.

### D3: Comment cleanup is text-only, no API change

**Decision:** Rewrite TSDoc comments to describe behavior without referencing C# class names. Do not add indirection or wrappers.

**Rationale:** These are documentation comments only. The internal C# class names were useful cross-references for the original author but have no meaning to someone reading the frontend in isolation. Replacing them with plain descriptions of the constraint achieves the same goal more clearly.

## Risks / Trade-offs

- **Breaking JSON API change** → Any browser tab open during a live mod upgrade will receive delta messages with the new field names before its state snapshot was built with the new names. The WebSocket auto-reconnect will fetch a fresh full-state snapshot, so this resolves itself within one reconnect cycle. No migration script is needed.
- **File rename of AbnormalityPicker** → Vue/Nuxt auto-import resolves components by filename. Renaming the file and updating the two explicit `<AbnormalityPicker>` usages in templates is sufficient; no import statements exist to update.
