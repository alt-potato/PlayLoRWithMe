## Why

Each librarian in Library of Ruina has an immutable "base" (origin) key page that the game auto-equips whenever no other key page is selected — but the current frontend has no way to fall back to it. Once a librarian's key page has been swapped, the user can only switch to another inventory page; there is no "unequip" affordance. The librarian editor must offer a clear way to return to the base.

## What Changes

- Emit the librarian's base (origin) key page in `GameStateSerializer` as a new per-librarian `baseKeyPage` field — same shape as the existing `keyPage` payload. The field is NOT surfaced as a separate selectable tile; the client uses it to detect "currently on base" (`keyPage.instanceId === baseKeyPage.instanceId`) so it can gate the Unequip affordance and the on-base placeholder state.
- Add a new WebSocket action `unequipKeyPage` that calls `UnitDataModel.EquipBook(null)` on the Unity main thread, gated by the librarian edit lock — mirrors the existing `equipKeyPage` handler's validation and post-equip refresh.
- In `KeyPageTab`, the action button switches between "Equip" (when an unequipped inventory tile is selected) and "Unequip" (when the currently equipped inventory tile is selected — this is the sole path to fall back to the base). When the librarian is already on their base, no tile is highlighted as equipped, the detail panel shows a placeholder, and the action button is hidden until the user picks a tile.
- Update mock backend (`useMockBackend.ts`) to route `unequipKeyPage` (log-only, no mutation — matching the existing dev-mock-backend contract).

## Capabilities

### New Capabilities

- `key-page-unequip-to-base`: Defines how the base key page is surfaced in state and how unequip-to-base works across the wire, server, and librarian editor UI.

### Modified Capabilities

- `wire-contract-schema`: Adds the `baseKeyPage` field on each librarian entry and the `unequipKeyPage` client→server action.
- `dev-mock-backend`: Extends the enumerated action-function list to include `unequipKeyPage` so the no-mutation, log-only contract covers the new handler.

## Impact

- **Wire contract**: New per-librarian `baseKeyPage` object (same schema as `keyPage`); new client action `unequipKeyPage` with `{ floorIndex, unitIndex }`.
- **C# server** (`mod/`): `GameStateSerializer.cs` (emit `baseKeyPage`), `Server.cs` (new `HandleUnequipKeyPage` handler + dispatch case).
- **Frontend** (`frontend/app/`): `types/game.ts` (extend `LibrarianEntrySchema`), `composables/useLibrarianActions.ts` and `composables/useWebSocket.ts` (new action), `components/librarian/KeyPageTab.vue` (action-button switches Equip ↔ Unequip; placeholder when on base), `dev/useMockBackend.ts` (handler).
- **Game compatibility**: Uses only existing `UnitDataModel` APIs (`defaultBook`, `EquipBook(null)`); the existing change-item-lock paths (Binah / Black Silence / Gebura) continue to govern whether the call takes effect.
