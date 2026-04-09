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

- [x] 5.1 Render each gift sprite onto the shared face-canvas in `GiftCache.cs` using `AppearanceCache.SpriteToPng` with a world-space offset computed by walking the prefab transform hierarchy; handle differing `pixelsPerUnit` between gift sprites and the face canvas via RenderTexture rescaling
- [x] 5.2 Expose `FaceHairBounds`, `FaceHairPpu`, `FaceHairCanvasW`, `FaceHairCanvasH` from `AppearanceCache` for `GiftCache` to consume; make `SpriteToPng` internal with optional `worldOffset` parameter
- [x] 5.3 Display gift overlays in `AppearancePreview.vue` as `.layer-sprite.gift-layer` divs (same CSS stacking as face layers: `inset: 0`, `background-size: 100% auto`); apply `faceRotStyle` and hide when `showFaceHairLayers` is false
- [x] 5.4 Update `edit-panel-appearance-preview` spec to reflect canvas-based gift rendering

## 6. Validation

- [x] 6.1 Build (`cd mod && dotnet build`) and verify 0 warnings 0 errors
- [x] 6.2 Manual test: equip/unequip/toggle gifts, verify preview updates, verify in-game character updates
