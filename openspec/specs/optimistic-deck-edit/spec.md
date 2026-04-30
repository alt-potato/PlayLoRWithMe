# optimistic-deck-edit Specification

## Purpose

Defines the optimistic-UI lifecycle for the librarian deck editor (`DeckTab.vue`). Server round-trips for `addCardToDeck` / `removeCardFromDeck` produce a perceptible delay between tap and visible state change; this spec governs how the editor reflects user intent in the same render cycle as the tap, reconciles against authoritative `deckPreview` state via diff-based FIFO matching, enforces the 9-card cap and per-rarity copy limits while edits are in flight, and clears phantom optimistic state on connection reset. Scope is limited to deck add/remove because other librarian-edit actions hold ownership locks that already serialize edits.

## Requirements

### Requirement: Tap to add SHALL render an immediate pending-add tile

When the user taps an inventory tile in `DeckTab.vue` to add a card, the deck editor SHALL immediately render a pending-add tile in the equipped-deck column before the server response arrives. The pending tile SHALL be appended after the last real `expandedDeck` tile and SHALL be visually distinct from confirmed tiles via reduced opacity and a spinner overlay.

#### Scenario: Pending-add tile appears on tap

- **WHEN** the user taps an inventory tile for a card not at its copy limit
- **THEN** a pending-add tile rendered with reduced opacity and a spinner overlay is appended to the equipped-deck column within the same render cycle as the tap
- **AND** the inventory tile's available count and the deck-count badge reflect the pending change immediately

#### Scenario: Pending-add tile carries the correct card identity

- **WHEN** a pending-add tile is rendered for an inventory tile whose `cardId.id` is `X` and `cardId.packageId` is `P`
- **THEN** the pending tile displays the same card name, cost, range, and dice as the inventory tile
- **AND** the tile is visually distinguishable from confirmed deck tiles (opacity ≤ 0.5 and spinner overlay present)

### Requirement: Tap to remove SHALL hide the tapped tile immediately

When the user taps a tile in the equipped-deck column to remove a card, that tile SHALL be hidden from the rendered deck before the server response arrives, and remaining tiles SHALL shift to fill the gap. This mirrors the in-game UX where tapping a card removes it instantly and the rest of the deck slides forward, allowing the user to clear multiple cards by tapping the same physical position repeatedly.

#### Scenario: Tapped tile vanishes from the rendered deck

- **WHEN** the user taps an equipped-deck tile for cardId `X`
- **THEN** that tile is removed from the rendered deck within the same render cycle as the tap
- **AND** the remaining equipped-deck tiles shift left/up to close the gap
- **AND** the deck-count badge decrements to reflect the pending change

#### Scenario: Repeated taps in the same physical position cascade through different cards

- **WHEN** the user has 2 confirmed copies of cardId `X` followed by 1 confirmed copy of cardId `Y` in deck positions 0, 1, 2
- **AND** the user taps deck position 0 three times in rapid succession
- **THEN** the first tap queues a pending-remove for `X` and the renderedDeck shifts so position 0 is the second `X` copy
- **AND** the second tap queues a second pending-remove for `X` and the renderedDeck shifts so position 0 is the `Y` copy
- **AND** the third tap queues a pending-remove for `Y`
- **AND** all three taps register distinct `removeCardFromDeck` requests

#### Scenario: Tap is suppressed when no remaining confirmed copies exist for the cardId

- **WHEN** the user has 1 confirmed copy of cardId `X` and 1 pending-remove for `X` already in flight
- **AND** the user tries to tap a tile for cardId `X` (e.g. via a stale render before the renderedDeck shifts)
- **THEN** no additional `removeCardFromDeck` request is sent
- **AND** the renderedDeck remains unchanged

### Requirement: Pending tiles SHALL count toward deck cap and per-card copy limits

Pending-add and pending-remove tiles SHALL participate in the same cap and limit arithmetic that confirmed tiles do, so that the user cannot queue requests that would violate the 9-card cap or per-card copy limit.

#### Scenario: Pending adds consume placeholder slots

- **WHEN** the equipped deck has 7 confirmed tiles and 1 pending-add tile
- **THEN** the empty-slot placeholder count is `9 - 8 = 1`
- **AND** the deck-count badge displays `8 / 9`

#### Scenario: Pending removes free placeholder slots

- **WHEN** the equipped deck has 9 confirmed tiles and 1 pending-remove
- **THEN** the renderedDeck shows 8 tiles (the pending-remove is hidden)
- **AND** the empty-slot placeholder count is `9 - 8 = 1`
- **AND** the deck-count badge displays `8 / 9`

#### Scenario: Cap blocks queueing past 9

- **WHEN** the equipped deck's effective count (confirmed + pending-add − pending-remove) is 9
- **THEN** every inventory tile renders as `unusable` with respect to deck-cap
- **AND** taps on inventory tiles enqueue no further pending-add tiles

#### Scenario: Per-card limit blocks queueing past copy limit

- **WHEN** a card has rarity `Rare` (limit 3), 2 confirmed copies in the deck, and 1 pending-add for that card
- **THEN** the inventory tile for that card is `unusable`
- **AND** a tap on it enqueues no further pending-add tiles

#### Scenario: Per-card limit accounts for pending removes

- **WHEN** a card has rarity `Rare` (limit 3), 3 confirmed copies in the deck, and 1 pending-remove for that card
- **THEN** the inventory tile for that card is enabled (effective count is 2)
- **AND** a tap on it enqueues a pending-add tile

### Requirement: Pending state SHALL reconcile via deckPreview diff (FIFO by cardId)

A watcher on `props.lib.deckPreview` SHALL compute per-`cardId+packageId` count diffs against the previous snapshot. For each positive unit of delta, the oldest pending-add tile for that key SHALL be removed. For each negative unit of delta, the oldest pending-remove tile for that key SHALL be removed. The `sendAction` promise SHALL NOT clear pending tiles on success.

#### Scenario: Confirmed add clears pending-add

- **WHEN** a pending-add tile exists for cardId `X` with `packageId` `P`
- **AND** a `deckPreview` mutation increments the count for `X+P` by 1
- **THEN** the oldest pending-add tile for `X+P` is removed
- **AND** the visible tile count is unchanged across the transition (pending tile gone, confirmed tile present)

#### Scenario: Confirmed remove clears pending-remove

- **WHEN** a pending-remove tile exists for cardId `X` with `packageId` `P`
- **AND** a `deckPreview` mutation decrements the count for `X+P` by 1
- **THEN** the oldest pending-remove tile for `X+P` is removed

#### Scenario: Concurrent multi-player adds clear in FIFO order

- **WHEN** two pending-add tiles for the same `cardId+packageId` exist (e.g. one from this client, one queued just before another client's add)
- **AND** the `deckPreview` count for that key increments by 1
- **THEN** exactly one pending-add tile is removed (the oldest)
- **AND** the other pending-add tile remains until the next matching delta

#### Scenario: Promise resolution does not clear on success

- **WHEN** the `sendAction` promise for an add resolves with `success: true` *before* the matching `deckPreview` delta lands
- **THEN** the pending-add tile is NOT cleared by the promise
- **AND** the pending-add tile is cleared by the subsequent `deckPreview` delta

### Requirement: Server-side rejection SHALL silently drop the pending tile

When a request resolves with `success: false`, the oldest pending tile matching that request's `(cardId, packageId, kind)` SHALL be removed without surfacing a toast, shake, error message, or other affordance.

#### Scenario: Server rejects an add (e.g., at-limit, lock lost)

- **WHEN** an `addCardToDeck` request resolves with `success: false`
- **THEN** the oldest pending-add tile for that `cardId+packageId` is removed
- **AND** no toast, banner, shake animation, or error message is displayed

#### Scenario: Server rejects a remove

- **WHEN** a `removeCardFromDeck` request resolves with `success: false`
- **THEN** the oldest pending-remove for that `cardId+packageId` is dropped (the previously-hidden tile reappears in the renderedDeck and remaining tiles shift back to accommodate it)
- **AND** no toast, banner, shake animation, or error message is displayed

### Requirement: Connection reset SHALL clear all pending state

After a WebSocket disconnect/reconnect cycle (i.e., after the client receives a fresh full-state `hello` payload), all pending-add and pending-remove tiles SHALL be cleared. The fresh full state is the new source of truth and any leftover pending tiles would be phantom.

#### Scenario: Pending tiles cleared on reconnect

- **WHEN** the WebSocket connection drops while one or more pending-add/pending-remove tiles exist
- **AND** the client reconnects and receives a fresh `hello` payload
- **THEN** all pending-add and pending-remove tiles are removed
- **AND** the deck-count badge and placeholder count reflect only the freshly-received `deckPreview`

### Requirement: Optimistic UI scope SHALL be limited to deck add/remove

The optimistic-tile lifecycle defined here SHALL apply only to `addCardToDeck` and `removeCardFromDeck`. Other librarian-edit actions (`equipKeyPage`, `equipSourceBook`, `unequipSourceBook`, `attributePassive`, `removeAttributedPassive`) SHALL retain their existing behavior of waiting for the state delta before any visual change.

#### Scenario: Key page equip remains non-optimistic

- **WHEN** the user equips a different key page in `KeyPageTab.vue`
- **THEN** no pending-state visual is shown on the key-page picker
- **AND** the key-page change becomes visible only after the next state delta lands

#### Scenario: Source book equip remains non-optimistic

- **WHEN** the user equips or unequips a source book in `PassivesTab.vue`
- **THEN** no pending-state visual is shown on the source-book picker
- **AND** the source-book change becomes visible only after the user commits and the next state delta lands
