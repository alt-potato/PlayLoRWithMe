## Why

The codebase mixes internal engine names (e.g. `playPoint`, C# class names in comments) with player-facing terms (e.g. `LightDisplay`, `AbnormalityPicker`) without a consistent rule, making it harder to reason about the boundary between game-engine internals and the public API. Establishing one clear convention now prevents this drift from compounding as more features are added.

## What Changes

- **BREAKING (JSON API):** Rename JSON state fields `playPoint` → `light`, `maxPlayPoint` → `maxLight`, `reservedPlayPoint` → `reservedLight`. Any client reading these fields must update their field references.
- Rename Vue component `AbnormalityPicker.vue` → `EmotionUpgradePicker.vue` — the component handles both key-page and abnormality card selection; the old name only described one of the two cases.
- Remove C# internal class names (`BookModel`, `DiceCardXmlInfo`, `StageController`) from TSDoc comments in `game.ts` and `DeckTab.vue`; replace with plain descriptions.
- C# variable names are unchanged — they remain internal and follow engine conventions.

## Capabilities

### New Capabilities

*(none — this is a pure refactor with no behavioral changes)*

### Modified Capabilities

*(none — spec-level requirements are unchanged)*

## Impact

- `mod/GameStateSerializer.cs` — JSON key names for the Light resource
- `frontend/app/types/game.ts` — `AllyUnit` interface fields + TSDoc comments
- `frontend/app/components/unit/DisplayCard.vue` — template bindings
- `frontend/app/components/AbnormalityPicker.vue` — file rename
- `frontend/app/components/battle/Stage.vue` — component reference
- `frontend/app/components/LibrarianManager.vue` — component reference
- `frontend/app/components/librarian/DeckTab.vue` — TSDoc comments
