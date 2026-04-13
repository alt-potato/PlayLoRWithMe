## Why

The customize panel currently covers appearance, fashion projection, and dialogue, but has no way to manage battle symbols (internally called "Gifts"). Battle symbols are a core customization system in Library of Ruina — 9 position-based slots (Eye, Nose, Cheek, Mouth, Ear, Hair Accessory, Hood, Mask, Helmet) that provide stat bonuses and visual effects on the librarian's character model. Without this feature, players must use the base game UI to manage them, breaking the co-op workflow.

## What Changes

- **Serialize battle symbols** in the game state: each librarian's equipped and available (unlocked) gifts per slot, including name, description, stat effects, and visibility toggle state.
- **New WebSocket action `setGifts`** to equip, unequip, and toggle visibility of gifts from the frontend.
- **New `BattleSymbolsTab` sub-tab** in CustomizePanel showing a 3×3 grid of the 9 gift positions, each displaying the equipped gift (or empty). Clicking a slot opens a selection list of unlocked gifts for that position. Each equipped gift has a visibility toggle (show/hide on character model).
- **Gift stat summary** displayed alongside the grid showing cumulative stat bonuses (HP, Stagger Resist, Stagger Recovery, Haste, Slash/Pierce/Blunt damage).
- **Extract gift preview sprites** from the game's prefabs at runtime (similar to `IconCache.cs` pattern) so the frontend can display gift icons.
- **Show visible equipped gifts on AppearancePreview** — display gift sprites on the preview for gifts that are equipped and not hidden.

## Capabilities

### New Capabilities
- `battle-symbol-management`: Serialization of gift data (equipped/available per librarian), WebSocket action to equip/unequip/toggle gifts, frontend UI for the 3×3 gift grid with selection and visibility controls, gift sprite extraction, and preview integration.

### Modified Capabilities
- `edit-panel-appearance-preview`: The appearance preview needs to render equipped and visible gift sprites on the character composite.

## Impact

- **`mod/GameStateSerializer.cs`**: Add gift inventory serialization per librarian and available gifts list in customizeOptions.
- **`mod/Server.cs`**: New `setGifts` action handler with lock authorization.
- **`mod/AppearanceCache.cs`** or new `GiftCache.cs`: Extract gift preview sprites from prefabs to `wwwroot/assets/gifts/`.
- **`mod/Initializer.cs`**: Hook gift sprite extraction.
- **`frontend/app/types/game.ts`**: New types for gifts (GiftSlot, GiftOption, GiftStatEffect).
- **`frontend/app/components/librarian/customize/BattleSymbolsTab.vue`**: New sub-tab component.
- **`frontend/app/components/librarian/CustomizePanel.vue`**: Add BattleSymbols sub-tab.
- **`frontend/app/components/librarian/AppearancePreview.vue`**: Render visible gift sprites.
