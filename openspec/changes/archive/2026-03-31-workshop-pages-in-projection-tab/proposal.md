## Why

The Projection tab's "X Page Fashion" selector only shows dedicated fashion books from `CustomCoreBookInventoryModel`. Workshop key pages that define a character skin via `CharacterSkin` in their XML are invisible here, so players cannot project a workshop librarian's appearance onto another librarian.

## What Changes

- Backend: extend the serialized `fashionBooks` list (or the `availableKeyPages` entries) to include workshop key pages that have a non-empty `CharacterSkin`, carrying their `packageId` so they can be identified and equipped.
- Backend: update the `setCustomization` handler to accept and correctly resolve workshop book IDs (which carry a `packageId` in addition to the numeric `id`) when calling `EquipCustomCoreBook`.
- Frontend: update `ProjectionTab` to display workshop key pages in the fashion list and pass the correct identifier payload when selecting one.
- Frontend types: extend `FashionBook` (or add a discriminated variant) to carry an optional `packageId` string for workshop entries.

## Capabilities

### New Capabilities
- `workshop-fashion-projection`: Expose workshop key pages with a character skin as selectable appearance projections in the Projection tab.

### Modified Capabilities

## Impact

- `mod/GameStateSerializer.cs` — fashion book enumeration logic
- `mod/Server.cs` — `setCustomization` action handler
- `frontend/app/types/game.ts` — `FashionBook` type
- `frontend/app/components/librarian/customize/ProjectionTab.vue`
