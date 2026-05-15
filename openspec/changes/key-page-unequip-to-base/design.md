## Context

In Library of Ruina, each `UnitDataModel` carries two book references:

- `_defaultBook` — the librarian's immutable origin/base key page, bound via `SetBasicBookOwner(this)` during `UnitDataModel.Init`. It never appears in `BookInventoryModel.GetBookList_equip()` and cannot be transferred to another librarian.
- `_bookItem` — the currently selected key page, which may be null.

The public `bookItem` getter returns `_bookItem ?? _defaultBook`, so "unequip" is implemented in-engine as `EquipBook(null)`. Today the mod's `GameStateSerializer` emits only `unit.bookItem` — which silently resolves to the default when nothing else is equipped — and the librarian editor (`KeyPageTab`) only knows about pages present in `availableKeyPages`. The result is a UI where the user cannot inspect their base page, cannot return to it, and the "Equip" button is permanently labeled "Equipped" (disabled) for any librarian sitting on their base, because no inventory tile matches the equipped `instanceId`.

The in-game equip screen (`UISettingEquipPageInvenPanel` + `UIOriginEquipPageList`) renders the origin key page as a regular slot and uses normal click-to-equip semantics on it. This change brings parity to the web UI without changing engine behavior.

## Goals / Non-Goals

**Goals:**

- Make each librarian's base key page visible in the librarian editor — its name, stats, and selection state.
- Allow users to fall back to the base from any equipped key page via a clear, single action.
- Preserve all existing engine-side guardrails (Binah / Black Silence / Gebura change-item-locks) without duplicating their logic in the mod layer.
- Keep the wire delta footprint small: `baseKeyPage` is a static field per librarian and never changes across the session unless a librarian rotates (which already triggers a full librarian-entry rewrite).

**Non-Goals:**

- Changing how the base key page itself is selected or generated (the game decides this at `UnitDataModel` construction; the mod only mirrors it).
- Allowing users to swap base key pages between librarians (the game disallows this; we don't surface it).
- Exposing `_defaultBook` in battle-context `keyPage` payloads — battle UI continues to read the active book directly. The new field is librarian-management only.

## Decisions

### Decision 1: New wire field `baseKeyPage` on `LibrarianEntry`, same shape as `keyPage`

The base page is conceptually parallel to the equipped page — same stats, same passives, same multi-deck flag possible. Reusing `KeyPageSchema` keeps the contract symmetric, lets `KeyPageDetail.vue` render either page without branching, and makes "currently on base" a straightforward `keyPage.instanceId === baseKeyPage.instanceId` check on the client.

**Alternatives considered:**

- **Boolean `isOnBase` flag instead.** Cheaper on the wire, but the frontend still has no way to show the base's name/stats without a second round-trip when the user is currently on a non-base page. Rejected: solves only half the problem.
- **Reuse `availableKeyPages` and push the base into it with a `kind: "base"` tag.** Pollutes the inventory list (which is also used for passive-source selection in `PassivesTab`) and makes ownership/availability semantics non-uniform across entries. Rejected: leakier.

### Decision 2: New action `unequipKeyPage`, not an overload of `equipKeyPage`

`equipKeyPage` is keyed on `bookInstanceId`, and the base book's `instanceId` is not findable via `BookInventoryModel.GetBookList_equip()` (the only lookup path `FindEquippedBook` uses). Extending `equipKeyPage` to accept a sentinel (`bookInstanceId: -1` or similar) would require special-casing the lookup, the ownership check (`book.owner != null`), and the success-verification post-condition. A separate handler is mechanically simpler and matches the engine API 1:1 (`EquipBook(null)`).

**Alternatives considered:**

- **Reuse `equipKeyPage` with the base's real `instanceId`.** Would require either (a) widening `FindEquippedBook` to also probe `defaultBook` per librarian, or (b) emitting the base in `availableKeyPages`. (a) leaks engine ownership semantics into the lookup; (b) is rejected above.

### Decision 3: No base tile; the equipped-tile-selected state IS the Unequip affordance

The base page is conceptually present but does not deserve its own selectable surface. Surfacing it as a tile inflates the concept count ("inventory pages plus a special non-inventory page") and forces a dedicated visual treatment to mark it as not-transferable. Instead, the inventory tile that is currently equipped doubles as the Unequip affordance: selecting it (which the user does anyway on tab-open via the default selection) reveals an "Unequip" button. The user thinks "I want to take off my current page" → clicks the page they're wearing → presses "Unequip". The base becomes a pure mechanical fallback rather than a UI concept.

Action button states:

- Selected = currently equipped inventory page → "Unequip" (calls `unequipKeyPage`).
- Selected = any other inventory page → "Equip" (existing `equipKeyPage` call).
- Librarian on base + no selection → button hidden, detail pane shows "Select a key page to view details".

**Alternatives considered:**

- **Pinned base tile above the grid** (prior design). Made the base equally prominent with inventory pages, but introduced a special-cased tile that the user could never actually remove — a category-of-one UI element. Rejected after user feedback: the value of showing base stats inline is small versus the cost of an extra concept on the canvas.
- **Separate "Unequip" button next to "Equip".** Two action buttons clutter the right pane on mobile and the second is only meaningful when an inventory page is equipped. Rejected: worse mobile UX.
- **Per-tile inline "X" remove control.** Inconsistent with the inventory-tile language ("Equipped by X" label) and easy to mis-tap on touch. Rejected.

### Decision 4: Trust the engine's change-item-lock guards; verify post-condition

`UnitDataModel.EquipBook(null)` already honors `IsChangeItemLock()`, `IsBinahChangeItemLock()` (which forces newBook=null and proceeds), and `IsBlackSilenceChangeItemLock()` (which forces newBook=BlackSilenceBook). The handler does not re-implement these; it calls `EquipBook(null)` and inspects `unit.bookItem` afterward — if it didn't become `_defaultBook`, the handler returns an error to the client.

## Risks / Trade-offs

- **Risk:** Black Silence / Binah lock states silently override the unequip (Black Silence force-equips its own book; Binah only allows null). → Mitigation: handler verifies `unit.bookItem == unit.defaultBook` after the call and surfaces a specific error string when it doesn't match, so the client can show a meaningful message rather than a stale optimistic state.
- **Risk:** Adding a new per-librarian wire field grows the snapshot size for every floor. → Mitigation: `baseKeyPage` is structurally identical to `keyPage` (no new schema cost) and is per-librarian, not per-tick; the delta engine only resends it when the librarian entry itself changes (which is rare and already triggers a full rewrite of that subtree).
- **Trade-off:** The "Unequip" affordance lives behind a tile click rather than as a top-level button. Users must first select the base tile, then press the action. This is one tap more than a permanent button but keeps the action button slot doing one job consistently and matches how the in-game UI works (click origin tile → preview → confirm).

## Migration Plan

No migration needed — purely additive at the schema level (`baseKeyPage` is optional in Zod, but always present in the C# emitter for librarian-context payloads). Clients that ignore the field continue to work; the new UI affordance only activates once the field is present.
