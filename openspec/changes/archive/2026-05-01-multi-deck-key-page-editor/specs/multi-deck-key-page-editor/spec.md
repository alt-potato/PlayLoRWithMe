## ADDED Requirements

### Requirement: Editor SHALL surface every deck slot for multi-deck key pages

For librarians whose equipped key page reports `isMultiDeck === true`, the deck editor SHALL render a tab strip above the equipped-deck column that exposes all four `DeckModel` slots (`deckIndex` 0..3). Each tab represents one slot. For librarians whose equipped key page reports `isMultiDeck === false`, the editor SHALL render no tab strip and SHALL operate exclusively on `deckIndex === 0` (existing single-deck behavior).

#### Scenario: Multi-deck key page renders four tabs

- **WHEN** the user opens the deck editor for a librarian whose key page has `isMultiDeck === true`
- **THEN** four tabs are rendered above the equipped-deck column, one per `deckIndex` 0..3
- **AND** each tab displays its deck's label and an `(N/9)` count badge

#### Scenario: Single-deck key page renders no tabs

- **WHEN** the user opens the deck editor for a librarian whose key page has `isMultiDeck === false`
- **THEN** no tab strip is rendered
- **AND** the editor reads from and writes to `deckIndex === 0` only
- **AND** the visible UX is identical to the pre-change single-deck experience

### Requirement: Active tab SHALL determine the deckIndex of all add/remove actions

The deck editor SHALL maintain a local active-tab index. While a tab is active, the equipped-deck column SHALL render the cards from `decks[activeIndex].cards`, and any `addCardToDeck` / `removeCardFromDeck` action dispatched from the editor SHALL carry `deckIndex === activeIndex`.

#### Scenario: Add targets the active tab

- **WHEN** the active tab is `deckIndex === 2`
- **AND** the user taps an inventory tile to add a card
- **THEN** an `addCardToDeck` action is dispatched with `deckIndex: 2`
- **AND** the pending-add tile is rendered in the equipped-deck column for tab 2

#### Scenario: Remove targets the active tab

- **WHEN** the active tab is `deckIndex === 1`
- **AND** the user taps an equipped-deck tile to remove a card
- **THEN** a `removeCardFromDeck` action is dispatched with `deckIndex: 1`
- **AND** the pending-remove hides one tile from the equipped-deck column for tab 1

#### Scenario: Switching tabs preserves pending state per-deck

- **WHEN** the active tab is `deckIndex === 0` and a pending-add tile exists for tab 0
- **AND** the user switches to `deckIndex === 1`
- **THEN** the equipped-deck column re-renders from `decks[1].cards` plus tab 1's pending state only
- **AND** tab 0's pending tile remains tracked but is not visible
- **AND** switching back to `deckIndex === 0` re-renders tab 0's pending state

### Requirement: Tab labels SHALL prefer known stance names with a generic fallback

For known multi-deck key pages identified by `(packageId, id)`, the editor SHALL render stance-specific labels. The label table SHALL at minimum cover The Purple Tear (`packageId: 0`, `id: 250035`) with labels `["Slash", "Penetrate", "Hit", "Defense"]` (in that order, matching the engine's `PurpleStance` enum). For multi-deck key pages whose `(packageId, id)` is absent from the label table, the editor SHALL render generic fallback labels `["Deck 1", "Deck 2", "Deck 3", "Deck 4"]`.

#### Scenario: Purple Tear uses stance labels

- **WHEN** the librarian's key page is `(packageId: 0, id: 250035)`
- **THEN** the four tabs are labeled `Slash`, `Penetrate`, `Hit`, `Defense` in deck-index order

#### Scenario: Unknown multi-deck key page uses fallback labels

- **WHEN** the librarian's key page has `isMultiDeck === true` but its `(packageId, id)` is not present in the label table
- **THEN** the four tabs are labeled `Deck 1`, `Deck 2`, `Deck 3`, `Deck 4`
- **AND** the editor functions identically to the known-label case in every other respect

### Requirement: Per-tab count badge SHALL reflect pending state for that tab only

Each tab's `(N/9)` count badge SHALL display the effective count of `decks[i].cards.length + pendingAddCount(i) - pendingRemoveCount(i)` where `i` is that tab's deck index. Pending edits to other deck indices SHALL NOT affect a tab's badge.

#### Scenario: Pending add to tab 1 increments tab 1's badge only

- **WHEN** `decks[0].cards.length === 5`, `decks[1].cards.length === 3`, no pending edits exist
- **AND** the user switches to tab 1 and adds a card
- **THEN** tab 0's badge displays `5/9`
- **AND** tab 1's badge displays `4/9`

#### Scenario: Pending remove on tab 2 decrements tab 2's badge only

- **WHEN** all four decks have 9 confirmed cards
- **AND** the user switches to tab 2 and taps to remove a card
- **THEN** tab 0, 1, 3 badges all display `9/9`
- **AND** tab 2's badge displays `8/9`

### Requirement: Server SHALL apply add/remove to the requested deck index transparently

The server SHALL accept `addCardToDeck` and `removeCardFromDeck` actions with an optional `deckIndex` field (default `0`). When processing such an action, the server SHALL:

1. Capture `prevIdx = book.GetCurrentDeckIndex()`.
2. Call `book.ChangeDeck(deckIndex)`.
3. Perform the mutation (`AddCardFromInventoryToCurrentDeck` / `MoveCardFromCurrentDeckToInventory`).
4. Restore via `book.ChangeDeck(prevIdx)`.

The mutation SHALL be observable to all sessions through the standard delta/state broadcast. The transient deck switch SHALL NOT be observable to any other code path because all librarian-edit handlers run on the Unity main thread (per `deck-edit-resilience`).

#### Scenario: Add to non-current deck index leaves active deck unchanged

- **WHEN** a librarian's key page has `isMultiDeck === true` and `book.GetCurrentDeckIndex() === 0`
- **AND** an `addCardToDeck` arrives with `deckIndex: 2`
- **THEN** the card is added to `_deckList[2]`
- **AND** after the handler returns, `book.GetCurrentDeckIndex() === 0` again
- **AND** the next state broadcast shows `decks[2].cards` includes the new card and `decks[0].cards` is unchanged

#### Scenario: deckIndex out of range is rejected

- **WHEN** an `addCardToDeck` or `removeCardFromDeck` arrives with `deckIndex < 0` or `deckIndex >= 4`
- **THEN** the server resolves the request with `{ ok: false, error: "deckIndex out of range" }`
- **AND** no mutation occurs

#### Scenario: deckIndex other than 0 on single-deck key page is rejected

- **WHEN** the librarian's key page has `isMultiDeck === false`
- **AND** an `addCardToDeck` or `removeCardFromDeck` arrives with `deckIndex !== 0`
- **THEN** the server resolves the request with `{ ok: false, error: "key page is not multi-deck" }`
- **AND** no mutation occurs

### Requirement: Per-card copy limit SHALL be enforced per deck slot, not per key page

Per-card copy limits (`DiceCardXmlInfo.Limit`) SHALL be enforced per `DeckModel` slot — i.e. each tab's deck independently caps a `Rare` card at 3 copies, mirroring the engine's `DeckModel.AddCardFromInventory` behavior. The editor's inventory-tile `unusable` check on a tab SHALL therefore use only that tab's confirmed + pending counts, not summed across all tabs.

#### Scenario: Same Rare card can be equipped at limit in multiple decks simultaneously

- **WHEN** the librarian's key page has `isMultiDeck === true` and 3 inventory copies of a `Rare` card
- **AND** the user equips 3 copies in deck 0
- **AND** then switches to deck 1 with sufficient inventory still available
- **THEN** the inventory tile for that card on deck 1 is enabled (not `unusable`)
- **AND** the user can equip another 3 copies in deck 1
