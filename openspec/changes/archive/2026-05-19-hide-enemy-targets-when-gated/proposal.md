## Why

The Library of Ruina encounter against The Crying Children's Page (book 142003) uses passive **Unhearing Child** (240428): "Cannot check or redirect the Crying Children's target. This ability is nullified when this character is Staggered." In the base game this gates `StageController.IsVisibleEnemyTarget()` to `false`, and the vanilla UI hides all enemy outgoing arrows and all parrying (clash) arrows for the duration — leaving only the player's blue outgoing arrows visible, with no clash highlight.

The mod's `GameStateSerializer.WriteSlottedCards` currently emits `targetUnitId`, `targetSlot`, `clash`, and `subTargets` for every slotted card unconditionally, so the frontend renders the full enemy targeting graph and clash markers even when the base game has suppressed them. This leaks combat information that is supposed to be hidden by the encounter's design.

## What Changes

- Mod-side serializer respects `StageController.IsVisibleEnemyTarget()` while serializing slotted cards in the `battle` scene.
  - Enemy units: omit `targetUnitId`, `targetSlot`, `clash`, and `subTargets` entirely from each slotted card entry while gating is active.
  - Ally units: continue emitting `targetUnitId`, `targetSlot`, and `subTargets` (so the player still sees their own outgoing blue arrows and mass-target previews) but force `clash: false` since clash detection requires the hidden enemy-side data.
- Frontend already treats absent `targetUnitId` as "no incoming arrow" / "no clash" in `ArrowOverlay.vue`, `unit/DieRow.vue`, `unit/DisplayCard.vue`, `TargetPicker.vue`, and `composables/useBattleContext.ts::attackMap`, so no template changes are expected — but the change verifies and documents this in tests.
- Wire-contract is **not** a breaking change: every affected field was already optional in JSON (each appears under `if (slot.target != null)`); they simply become conditionally omitted in more cases. No client-version bump required.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `combat-targeting`: adds new requirements covering the mod's responsibility to mirror `StageController.IsVisibleEnemyTarget()` gating when serializing slotted-card target info, so frontend rendering automatically aligns with the base game's hidden-target encounters.

## Impact

- **Mod**: `mod/GameStateSerializer.cs` — one new helper or local lookup of `StageController.IsVisibleEnemyTarget()` cached per `Serialize` call; conditional emission inside `WriteSlottedCards`.
- **Frontend**: no behavior changes expected; affected components are `ArrowOverlay.vue`, `unit/DieRow.vue`, `unit/DisplayCard.vue`, `TargetPicker.vue`, `composables/useBattleContext.ts`. Verify via the existing `useWebSocket` / display-helper tests, no new components.
- **Wire schema**: no schema change (fields already optional). The reference fixture set under `schema/reference-state.json` may grow by one "battle with hidden enemy targets" case so the gating behavior is exercised in tests.
- **No external dependencies, no APIs added or removed.**
