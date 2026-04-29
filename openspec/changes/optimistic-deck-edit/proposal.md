## Why

Adding or removing a card in the librarian deck editor currently provides no immediate feedback: the user taps a tile, and nothing visible happens until the server's next state delta arrives. The round-trip is short on a LAN but perceptible enough — especially with many rapid taps — to make the editor feel sluggish and unresponsive, leading users to double-tap and overshoot.

## What Changes

- On a tap that adds a card, the deck editor SHALL immediately show a pending tile at the end of the equipped deck, occupying the next available slot.
- On a tap that removes a card, the deck editor SHALL immediately mark the tapped tile as pending-remove (dimmed) while the request is in flight.
- Pending tiles SHALL count toward the 9-card deck cap and toward the inventory-side at-limit check, so users cannot queue beyond the cap or beyond per-card copy limits while requests are in flight.
- The empty placeholder count SHALL decrement to account for pending-add tiles, preserving the visible "X / 9" total.
- Reconciliation SHALL be delta-driven: a watcher on `deckPreview` compares old vs new copy counts per cardId and clears the oldest matching pending tile (FIFO by cardId) whenever the real count catches up. The promise from `sendAction` SHALL NOT gate clearing.
- On request failure (server reject, lock loss, etc.), the pending tile SHALL silently disappear without a toast or shake.
- Scope: this change applies only to deck add/remove. Other librarian-edit actions (key page equip, source book equip/unequip, passive attribute/remove) are out of scope because their target resources have ownership locks that already prevent concurrent modification.

## Capabilities

### New Capabilities

- `optimistic-deck-edit`: covers the deck editor's optimistic-UI lifecycle for add and remove, the reconciliation rule (delta-driven FIFO match by cardId), the interaction with the 9-card cap and per-card copy limits, and the silent-on-failure behavior.

### Modified Capabilities

None. This change is purely frontend behavior; the wire contract, server handlers, and `deckPreview` payload are unchanged.

## Impact

- `frontend/app/components/librarian/DeckTab.vue`: pending-add tile rendering, pending-remove dimming, cap/limit checks updated to count pending state, watcher that diffs `deckPreview`.
- `frontend/app/components/librarian/EditPanel.vue` (only if pending state needs to live a level up to survive tab switches; otherwise local to `DeckTab`).
- Possibly a small composable (e.g., `composables/usePendingDeckEdits.ts`) if the bookkeeping is non-trivial; decision deferred to design.
- No backend changes. No `useWebSocket` changes. No type changes to `DeckCardPreview` or `AvailableCard`.
- Visual: a new "pending" tile style (reduced opacity, optional spinner badge) — design.md will pin this down.
