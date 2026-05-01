## MODIFIED Requirements

### Requirement: Tap to add SHALL render an immediate pending-add tile

When the user taps an inventory tile in `DeckTab.vue` to add a card, the deck editor SHALL immediately render a pending-add tile in the equipped-deck column for the active tab before the server response arrives. The pending tile SHALL be appended after the last real `expandedDeck` tile of the active tab and SHALL be visually distinct from confirmed tiles via reduced opacity and a spinner overlay.

#### Scenario: Pending-add tile appears on tap

- **WHEN** the user taps an inventory tile for a card not at its copy limit on the active tab
- **THEN** a pending-add tile rendered with reduced opacity and a spinner overlay is appended to the equipped-deck column for the active tab within the same render cycle as the tap
- **AND** the inventory tile's available count and the active tab's count badge reflect the pending change immediately

#### Scenario: Pending-add tile carries the correct card identity

- **WHEN** a pending-add tile is rendered for an inventory tile whose `cardId.id` is `X` and `cardId.packageId` is `P`
- **THEN** the pending tile displays the same card name, cost, range, and dice as the inventory tile
- **AND** the tile is visually distinguishable from confirmed deck tiles (opacity ≤ 0.5 and spinner overlay present)

### Requirement: Pending tiles SHALL count toward deck cap and per-card copy limits

Pending-add and pending-remove tiles SHALL participate in the same cap and limit arithmetic that confirmed tiles do, so that the user cannot queue requests that would violate the 9-card cap or per-card copy limit. When a multi-deck key page is loaded, cap and copy-limit math SHALL be evaluated per active tab — pending edits to inactive tabs SHALL NOT count toward the active tab's cap or per-card limit.

#### Scenario: Pending adds consume placeholder slots

- **WHEN** the active tab's deck has 7 confirmed tiles and 1 pending-add tile
- **THEN** the empty-slot placeholder count is `9 - 8 = 1`
- **AND** the active tab's count badge displays `8 / 9`

#### Scenario: Pending removes free placeholder slots

- **WHEN** the active tab's deck has 9 confirmed tiles and 1 pending-remove
- **THEN** the renderedDeck shows 8 tiles (the pending-remove is hidden)
- **AND** the empty-slot placeholder count is `9 - 8 = 1`
- **AND** the active tab's count badge displays `8 / 9`

#### Scenario: Cap blocks queueing past 9

- **WHEN** the active tab's deck effective count (confirmed + pending-add − pending-remove) is 9
- **THEN** every inventory tile renders as `unusable` with respect to deck-cap
- **AND** taps on inventory tiles enqueue no further pending-add tiles

#### Scenario: Per-card limit blocks queueing past copy limit

- **WHEN** a card has rarity `Rare` (limit 3), 2 confirmed copies in the active tab's deck, and 1 pending-add for that card on the active tab
- **THEN** the inventory tile for that card is `unusable`
- **AND** a tap on it enqueues no further pending-add tiles

#### Scenario: Per-card limit accounts for pending removes

- **WHEN** a card has rarity `Rare` (limit 3), 3 confirmed copies in the active tab's deck, and 1 pending-remove for that card on the active tab
- **THEN** the inventory tile for that card is enabled (effective count is 2)
- **AND** a tap on it enqueues a pending-add tile

#### Scenario: Cap and limit on inactive tabs do not influence the active tab

- **WHEN** the librarian has a multi-deck key page, deck 0 has 9 confirmed tiles, and the user switches to deck 1 (which has 0 confirmed tiles)
- **THEN** the inventory tiles on deck 1 are not marked `unusable` due to deck 0's cap
- **AND** taps on inventory tiles on deck 1 enqueue pending-add tiles up to deck 1's own cap

### Requirement: Pending state SHALL reconcile via deckPreview diff (FIFO by cardId)

A watcher on `props.lib.decks` SHALL compute per-`(deckIndex, cardId+packageId)` count diffs against the previous snapshot. For each positive unit of delta on a given deck index, the oldest pending-add tile for that `(deckIndex, cardId+packageId)` key SHALL be removed. For each negative unit of delta, the oldest pending-remove tile for that key SHALL be removed. The `sendAction` promise SHALL NOT clear pending tiles on success.

#### Scenario: Confirmed add clears pending-add for the matching deck

- **WHEN** a pending-add tile exists for cardId `X` with `packageId` `P` on `deckIndex` `2`
- **AND** a state mutation increments the count for `X+P` on `decks[2]` by 1
- **THEN** the oldest pending-add tile for `(2, X+P)` is removed
- **AND** the visible tile count on tab 2 is unchanged across the transition (pending tile gone, confirmed tile present)

#### Scenario: Delta on a different deck does not clear pending on the active deck

- **WHEN** a pending-add tile exists for cardId `X` with `packageId` `P` on `deckIndex` `0`
- **AND** a state mutation increments the count for `X+P` on `decks[1]` by 1
- **THEN** the pending-add tile on `deckIndex` `0` is NOT cleared
- **AND** no spurious visual change occurs on the active tab

#### Scenario: Confirmed remove clears pending-remove for the matching deck

- **WHEN** a pending-remove tile exists for cardId `X` with `packageId` `P` on `deckIndex` `1`
- **AND** a state mutation decrements the count for `X+P` on `decks[1]` by 1
- **THEN** the oldest pending-remove tile for `(1, X+P)` is removed

#### Scenario: Concurrent multi-player adds on the same deck clear in FIFO order

- **WHEN** two pending-add tiles for the same `(deckIndex, cardId+packageId)` exist (e.g. one from this client, one queued just before another client's add)
- **AND** the count for that key increments by 1
- **THEN** exactly one pending-add tile is removed (the oldest)
- **AND** the other pending-add tile remains until the next matching delta

#### Scenario: Promise resolution does not clear on success

- **WHEN** the `sendAction` promise for an add resolves with `success: true` *before* the matching state delta lands
- **THEN** the pending-add tile is NOT cleared by the promise
- **AND** the pending-add tile is cleared by the subsequent state delta

### Requirement: Server-side rejection SHALL silently drop the pending tile

When a request resolves with `success: false`, the oldest pending tile matching that request's `(deckIndex, cardId, packageId, kind)` SHALL be removed without surfacing a toast, shake, error message, or other affordance.

#### Scenario: Server rejects an add (e.g., at-limit, lock lost)

- **WHEN** an `addCardToDeck` request with `deckIndex: i` resolves with `success: false`
- **THEN** the oldest pending-add tile for `(i, cardId+packageId)` is removed
- **AND** no toast, banner, shake animation, or error message is displayed

#### Scenario: Server rejects a remove

- **WHEN** a `removeCardFromDeck` request with `deckIndex: i` resolves with `success: false`
- **THEN** the oldest pending-remove for `(i, cardId+packageId)` is dropped (the previously-hidden tile reappears in the renderedDeck and remaining tiles shift back to accommodate it)
- **AND** no toast, banner, shake animation, or error message is displayed
