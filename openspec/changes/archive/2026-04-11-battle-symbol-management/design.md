## Context

The customize panel already supports appearance, fashion projection, and dialogue editing. Battle symbols ("Gifts" internally) are the remaining major customization system. The game stores them in `GiftInventory` on each `UnitDataModel`, with 9 position-based slots mapped to the `GiftPosition` enum (Eye, Nose, Cheek, Mouth, Ear, HairAccessory, Hood, Mask, Helmet). Each gift has stat effects, a visibility toggle (`isShowEquipGift`), and a visual prefab rendered on the character model.

The frontend customize panel follows a consistent pattern: sub-tab components emit `update:*` events to a parent-held reactive `draft`, and a single "Complete" action sends all changes via WebSocket.

## Goals / Non-Goals

**Goals:**
- Serialize each librarian's equipped gifts and available (unlocked) gift pool per position
- Let users equip, unequip, and toggle visibility of gifts from the web UI
- Extract gift preview sprites to static PNGs for the frontend
- Display equipped visible gifts on the AppearancePreview composite
- Show cumulative stat bonuses from equipped gifts

**Non-Goals:**
- Gift acquisition/unlocking (gifts are unlocked through gameplay)
- Workshop/modded gifts (only base game gifts supported initially)
- Battle-time gift effects (passive abilities) — these are engine-managed
- Animating gift appearances (static preview only)

## Decisions

### Gift data serialization strategy

**Decision:** Serialize gift state per-librarian inline (not as a separate top-level object).

Each librarian entry gets a `gifts` object containing:
- `equipped`: array of 9 entries (one per `GiftPosition`), each either `null` (empty slot) or `{ id, name, desc, position, stat, visible }`.
- `available`: array of all unlocked, unequipped gifts for this librarian, grouped by position.

**Rationale:** Following the same pattern as appearance/dialogue/titles — self-contained per librarian. The `available` list is per-librarian because each librarian has their own `GiftInventory`. This avoids needing a separate global gifts lookup.

**Alternative considered:** Global gift catalog in `customizeOptions` — rejected because gift availability is per-librarian (different librarians can unlock different gifts through their combat history).

### WebSocket action design

**Decision:** Use a single `setGifts` action that receives a flat list of slot assignments.

Payload:
```json
{
  "type": "setGifts",
  "floorIndex": 0,
  "unitIndex": 2,
  "slots": [
    { "position": 0, "giftId": 12, "visible": true },
    { "position": 1, "giftId": -1 },
    ...
  ]
}
```

Where `giftId: -1` means unequip that slot, and omitted positions are unchanged.

**Rationale:** Batching all slot changes into one action (same pattern as `setCustomization`) avoids race conditions and reduces round-trips. The visibility toggle is included per-slot since it's a per-gift property.

**Alternative considered:** Separate `equipGift`/`unequipGift`/`toggleGiftVisibility` actions — rejected for consistency with the existing batched pattern.

### Gift sprite extraction

**Decision:** Extract gift preview sprites from the game's prefabs using the `GiftAppearance.GetGiftPreview()` pattern — load each prefab via `Resources.Load<GiftAppearance>(path)`, get the front sprite, render to texture, save as PNG.

Output path: `wwwroot/assets/gifts/gift_{id}.png`

**Rationale:** Same approach as `IconCache.cs` and `AppearanceCache.cs`. The `GiftAppearance` prefab has `_frontSpriteRenderer` which provides the preview sprite. Some gifts are `GiftAppearance_Aura` type (no sprite, use a generic icon).

**Risk:** Some prefabs may fail to load outside the full game context. Mitigation: skip and log; frontend shows a placeholder for missing sprites.

### Frontend UI layout

**Decision:** A 3×3 CSS grid matching the game's layout, with position labels. Clicking a slot opens an inline selection list below the grid (not a modal). Each equipped slot shows the gift name and a visibility toggle icon (eye open/closed).

**Rationale:** The 3×3 grid is compact and familiar to players. An inline list (similar to how ProjectionTab shows fashion books) keeps the UI consistent. On narrow viewports the grid scales down but remains 3×3.

**Alternative considered:** Flat list of all 9 slots — rejected because the spatial grid is a meaningful organization (face positions vs. head positions) that players already know.

### Stat summary display

**Decision:** Show a compact stat summary below the grid, only listing non-zero bonuses. Format: `+5 HP  +3 Stagger Resist` etc.

### Preview integration

**Decision:** Add gift sprite layers to AppearancePreview for equipped gifts where `visible === true`. Each gift is absolutely positioned on the head area using a `GIFT_LAYOUT` constant mapping each `GiftPosition` to `{ left, top, size, z }`. Each gift `<img>` is centered on its anchor via `transform: translate(-50%, -50%)`.

Approximate positions within the 160px box (character head fills ~top 70%):

| Position | left | top | size | z |
|---|---|---|---|---|
| Eye | 50% | 30% | 32px | 12 |
| Nose | 50% | 40% | 28px | 11 |
| Cheek | 37% | 38% | 30px | 11 |
| Mouth | 50% | 48% | 30px | 11 |
| Ear | 22% | 33% | 28px | 12 |
| HairAccessory | 55% | 15% | 40px | 14 |
| Hood | 50% | 12% | 56px | 15 |
| Mask | 50% | 32% | 48px | 13 |
| Helmet | 50% | 10% | 56px | 10 |

Z-index values mirror the in-game `GiftAppearance.RefreshAppearance` sorting order.

**Head tilt:** Apply the same `faceRotStyle` computed property to gift wrapper `<div>`s, since gifts parent to the head bone in-game.

**replacesHead hiding:** `v-show="showFaceHairLayers"` — when the fashion body replaces the head, gifts are also hidden.

**Rotation + centering order:** Rotation via wrapper `<div>` using `faceRotStyle`; the inner `<img>` uses its own `transform: translate(-50%, -50%)` for centering, keeping the two transforms separate.

## Risks / Trade-offs

- **Gift prefab loading** → Some gifts may use complex prefabs that don't render cleanly via `RenderTexture`. Mitigation: fallback to a generic gift icon; log failures.
- **Gift sprite positioning in preview** → Approximate CSS positioning won't match in-game exactly. Mitigation: acceptable for a preview; grouping by position (face vs. hair) gives reasonable approximation.
- **Large gift pools** → Some librarians may have 50+ unlocked gifts. Mitigation: the selection list is filtered by position (max ~6 per slot typically) and scrollable.
- **Gift availability per librarian** → Each librarian has their own unlock history, requiring per-librarian serialization of available gifts. This increases state payload size. Mitigation: only serialize when customize panel is open (not in battle state).
