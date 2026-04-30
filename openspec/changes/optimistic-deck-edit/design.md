## Context

`DeckTab.vue` shows the equipped deck (left, tap-to-remove) and the inventory (right, tap-to-add). Today the tap handlers call `actions.addCardToDeck` / `actions.removeCardFromDeck`, which round-trip via WebSocket to the server's `HandleAddCardToDeck` / `HandleRemoveCardFromDeck`. The server calls `SaveAndBroadcast()` (which emits the next state delta) and *then* `SendResult` (the request ack). The frontend re-renders the deck only when the delta lands and patches `gameState`.

The user-visible result is "tap, briefly nothing, then the deck updates" — usually <100 ms locally but enough to make rapid edits feel disconnected. There's no per-card affordance during the in-flight window; only `editBusy` (a panel-wide flag) signals activity.

Card slots in the deck are interchangeable per cardId: `expandedDeck` flattens `deckPreview`'s `{cardId, count}` entries into one tile per copy, and the server returns whatever order `book.GetCardListFromCurrentDeck()` produces. The 9-card cap and per-card copy limit (3 normal / 1 Unique) are enforced both server-side and in the inventory tile's `unusable` check.

## Goals / Non-Goals

**Goals:**

- Instant visual feedback on every deck-editor tap, with the pending state visibly distinct from confirmed state.
- Robust reconciliation that survives concurrent edits from multiple players against the same shared inventory.
- No flicker, no double-render between tap and delta.
- Strict adherence to the 9-card cap and per-card copy limits even with pending edits in flight.

**Non-Goals:**

- Optimistic UI for any other librarian-edit surface (key page, source book, passives) — those resources have ownership locks that already serialize edits.
- A new wire-protocol message, request ID surfacing, or any backend change.
- Cancelling an in-flight request from the UI.
- Animating the reorder when a real deck entry's position differs from the pending tile's position.

## Decisions

### Decision: Reconcile via `deckPreview` diff, not via the `sendAction` promise

When a request resolves successfully, the state delta has already been broadcast (server calls `SaveAndBroadcast()` *before* `SendResult`). Two events fire on the client — promise resolution and a `deckPreview` mutation — but their order is not guaranteed across a slow connection. Tying the clear to the promise risks a flicker (pending → empty → real) if the delta arrives after the ack.

A `watch` on `props.lib.deckPreview` (deep) compares old vs new copy counts per `cardId+packageId` key. For each unit of positive delta, drop the oldest pending-add for that key. For each unit of negative delta, drop the oldest pending-remove. Promise resolution is ignored on success.

**Alternatives considered:**

- **Promise-only**: simpler to write but susceptible to flicker; cannot disambiguate concurrent multi-player adds (two players add the same card, both see their own pending tile dropped on the *first* delta).
- **Hybrid (ack-gated diff)**: only let the diff clear pending tiles whose request has resolved. Strictly more correct but more state to track; the diff-only approach is already correct because the server is the only source of `deckPreview` mutations and the diff matches exactly the count change the action caused.

### Decision: Failures use the promise, not the diff

Failed requests don't produce a delta, so the diff watcher will never clear them. When `sendAction` resolves with `success: false`, drop the oldest pending edit matching that request's `(cardId, packageId, kind)`. This is the only role the promise plays in the lifecycle.

### Decision: Pending state lives in `DeckTab.vue`, not in a composable

The bookkeeping is small (two arrays of `{cardId, packageId, addedAt}`) and only `DeckTab` consumes it. A composable would add an extra file without isolating reusable logic. If a future surface (e.g., during battle or in a different librarian flow) needs the same pattern, we can extract then.

Pending state lives long enough to survive a single request lifetime. If the user closes `EditPanel` mid-flight, the pending state dies with the unmounted component — acceptable, because the panel close also drops the user's view of the affected librarian.

### Decision: Pending-add gets a dimmed tile + spinner; pending-remove hides the tile entirely

Pending-add tiles render at the end of `expandedDeck` with `opacity: 0.5` and a small spinner SVG overlaid in a corner — a brand-new tile needs a clear "in flight" affordance.

Pending-remove takes the opposite approach: the tapped tile vanishes from the rendered deck immediately (the renderedDeck filters out one tile per pending-remove for that cardId, leftmost-first), and remaining tiles shift to fill the gap. The disappearing tile is the feedback. This mirrors the in-game UX where tapping a card removes it instantly and lets the user spam-tap a single physical position to clear multiple cards in sequence (each tap finds whatever tile slid into that position next).

**Alternative considered**: dim the tapped tile in place + show a corner spinner, like pending-add. Rejected because (a) tiles for the same cardId are visually interchangeable, so reliably dimming the *exact* tapped tile vs. an arbitrary other copy of the same cardId is awkward to express in the data model, and (b) the dim/spinner combo blocks the in-game spam-tap-to-clear UX, which the user explicitly relies on.

### Decision: Cap and limit checks include pending state

`effectiveDeckCount = expandedDeck.length + pendingAdds.length - pendingRemoves.length`.

- Empty-slot placeholder count uses `effectiveDeckCount` so pending tiles consume placeholder slots.
- The deck-count badge shows `effectiveDeckCount / 9`.
- Inventory `isAtLimit(card)` adds pending-add count and subtracts pending-remove count for that cardId before comparing to `cardLimit(rarity)`.
- The remove handler refuses to enqueue another pending-remove for a tile already marked pending-remove.

### Decision: Clear pending state on connection reset

A pending request issued just before a WebSocket disconnect could be lost server-side. After reconnection the client receives a fresh `hello` payload (full state). We watch the connection status and clear all pending edits on reconnect, since the new full state is the new source of truth and any leftover pending tiles would be phantom.

## Risks / Trade-offs

- **Reorder snap on reconciliation**: pending-add tiles render at the end of the deck; the real entry from `deckPreview` may sit elsewhere in game-internal order. When the pending tile clears, the real tile appears in its actual slot — visually a small jump.
  → Mitigation: accept the jump for now (it's brief and infrequent). If it proves jarring, revisit with a FLIP-style animation.

- **Multi-tap during pending-remove**: a user could tap the same physical tile twice before the first response arrives. The second tap is a no-op (tile is already in `is-pending-remove`).
  → Mitigation: handler short-circuits if the tile is already pending; no second request is sent.

- **Pending state desync after a manual `resync` action**: if the user (or auto-reconnect logic) issues a `resync` mid-flight, the full-state replacement could land before or after the action response. Clearing pending state on full-state-replace handles both.

- **Promise-failure drop targets the wrong tile**: if two pending-adds for the same cardId are in flight and one fails, we drop the *oldest* — which might not be the one that failed. Acceptable: from the user's perspective, all that matters is that the deck reflects exactly the successful adds; which physical pending tile cleared is invisible because tiles for the same cardId are interchangeable.
