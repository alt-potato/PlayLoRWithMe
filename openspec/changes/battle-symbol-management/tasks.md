## 1. Gift Sprite Extraction

- [x] 1.1 Create `GiftCache.cs` to extract gift preview sprites from `GiftAppearance` prefabs to `wwwroot/assets/gifts/gift_{id}.png`, skipping already-extracted files and logging failures
- [x] 1.2 Hook `GiftCache` extraction into `StateBroadcaster.Patch_ActivateUI` (same hook point as `AppearanceCache`)

## 2. Gift Data Serialization

- [x] 2.1 Add gift types to `frontend/app/types/game.ts`: `GiftSlot`, `GiftOption`, `GiftStat`, `GiftInventoryData`, and add `gifts` field to `LibrarianEntry`
- [x] 2.2 Serialize equipped gifts (9 positional slots) per librarian in `GameStateSerializer.cs`, including id, name, desc, position, stat, and visible
- [x] 2.3 Serialize available (unlocked, unequipped, non-NoAppear) gifts per librarian in `GameStateSerializer.cs`

## 3. setGifts WebSocket Action

- [x] 3.1 Add `HandleSetGifts` handler in `Server.cs` that processes flat indexed keys (gift0–8, vis0–8) — equip, unequip, and toggle visibility — with librarian lock authorization
- [x] 3.2 Refresh in-game character appearance after gift changes (OnUpdateCharacterGift) and broadcast state

## 4. BattleSymbolsTab Frontend Component

- [x] 4.1 Create `BattleSymbolsTab.vue` with 3×3 CSS grid of gift positions, showing equipped gift name and visibility toggle per cell
- [x] 4.2 Add slot selection → available gift list below grid, with gift name, stat summary, and description
- [x] 4.3 Add cumulative stat summary display for all equipped gifts (non-zero bonuses only)
- [x] 4.4 Wire `BattleSymbolsTab` into `CustomizePanel.vue` as a new sub-tab with immediate gift actions (not draft-batched)

## 5. Gift Preview Integration

- [x] 5.1 Add `GIFT_LAYOUT` constant mapping each `GiftPosition` name to `{ left, top, size, z }` anchor values and render equipped visible gifts as individually positioned overlays on the head area of `AppearancePreview.vue` (apply `faceRotStyle`, hide when `showFaceHairLayers` is false)
- [x] 5.2 Add `.gift-wrapper` (absolute positioning) and `.gift-sprite` (centering + drop-shadow) CSS styles; remove any strip-based layout
- [x] 5.3 Update `edit-panel-appearance-preview` spec to reflect gift overlay rendering

## 6. Validation

- [x] 6.1 Build (`cd mod && dotnet build`) and verify 0 warnings 0 errors
- [ ] 6.2 Manual test: equip/unequip/toggle gifts, verify preview updates, verify in-game character updates
