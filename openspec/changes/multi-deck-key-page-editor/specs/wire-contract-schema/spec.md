## ADDED Requirements

### Requirement: `LibrarianEntry.decks` SHALL replace `deckPreview`

`LibrarianEntrySchema` SHALL expose a `decks` field of type `DeckPreview[]` and SHALL NOT carry the legacy `deckPreview` field.

`DeckPreview` is defined as:

```ts
{
  index: number;          // 0..3
  label?: string;         // present iff the key page is multi-deck
  cards: CardId[];
}
```

For single-deck key pages the array MUST contain exactly one entry with `index === 0` and no `label`. For multi-deck key pages (`KeyPage.isMultiDeck === true`), the array MUST contain exactly four entries with `index` values `0..3` in ascending order.

The C# serializer MUST iterate `book.GetDeckAll_nocopy()` to produce `decks` for librarian-context payloads. The serializer MUST NOT emit `decks` on battle-unit `keyPage` payloads — battle-time UI surfaces the active deck through other channels and remains unaffected by this change.

#### Scenario: Single-deck librarian carries one entry

- **WHEN** the serializer writes a `LibrarianEntry` whose key page has `isMultiDeck === false`
- **THEN** `decks.length === 1`
- **AND** `decks[0].index === 0`
- **AND** `decks[0].label` is absent
- **AND** `decks[0].cards` matches the single deck's contents

#### Scenario: Multi-deck librarian carries four entries

- **WHEN** the serializer writes a `LibrarianEntry` whose key page has `isMultiDeck === true`
- **THEN** `decks.length === 4`
- **AND** `decks[i].index === i` for `i` in `0..3`
- **AND** each `decks[i].cards` matches `_deckList[i]`'s contents

#### Scenario: Schema rejects legacy `deckPreview`

- **WHEN** a payload contains a top-level `deckPreview` field on a `LibrarianEntry`
- **THEN** `LibrarianEntrySchema.parse` either ignores the field (if the schema is permissive) or rejects it
- **AND** consumers SHALL NOT read from `lib.deckPreview`

### Requirement: `KeyPage.isMultiDeck` SHALL be exposed on librarian-context payloads

`KeyPageSchema` SHALL carry a required `isMultiDeck: boolean` field on librarian-management payloads (the `LibrarianEntry.keyPage` slot). The C# serializer MUST source this from `BookModel.IsMultiDeck()`.

The field SHALL NOT appear on battle-unit `keyPage` payloads, consistent with the existing pattern for librarian-only fields (e.g. `rarity`).

#### Scenario: Librarian-owned key page carries isMultiDeck

- **WHEN** the serializer writes a `LibrarianEntry`'s `keyPage`
- **THEN** the resulting JSON object includes `isMultiDeck: true | false` sourced from `BookModel.IsMultiDeck()`

#### Scenario: Battle-unit key page omits isMultiDeck

- **WHEN** the serializer writes a battle-unit `keyPage`
- **THEN** the resulting JSON object does NOT include `isMultiDeck`

### Requirement: `addCardToDeck` and `removeCardFromDeck` SHALL accept optional `deckIndex`

`ClientActionSchema`'s `addCardToDeck` and `removeCardFromDeck` variants SHALL each carry an optional `deckIndex?: number` field. When omitted the server MUST treat it as `0`. When present, the value MUST satisfy `0 <= deckIndex < 4`.

The mod MUST validate the index range and the librarian's `IsMultiDeck` status before mutating, and MUST resolve invalid requests with a structured `ok: false` response.

#### Scenario: Action without deckIndex defaults to deck 0

- **WHEN** a client sends `{ type: "addCardToDeck", floorIndex: 0, unitIndex: 0, cardId: { id: 1, packageId: 0 } }` with no `deckIndex`
- **THEN** the server treats the action as targeting `deckIndex: 0`

#### Scenario: Action with deckIndex 0..3 targets that slot

- **WHEN** a client sends `addCardToDeck` with `deckIndex: 2` for a multi-deck librarian
- **THEN** the server mutates `_deckList[2]`

#### Scenario: Action with out-of-range deckIndex is rejected

- **WHEN** a client sends `addCardToDeck` or `removeCardFromDeck` with `deckIndex: 4` or `deckIndex: -1`
- **THEN** the server resolves the request with `{ ok: false, error: "deckIndex out of range" }`
- **AND** no mutation occurs

#### Scenario: Action with deckIndex !== 0 on single-deck book is rejected

- **WHEN** a client sends `addCardToDeck` with `deckIndex: 1` for a librarian whose key page has `isMultiDeck === false`
- **THEN** the server resolves the request with `{ ok: false, error: "key page is not multi-deck" }`
- **AND** no mutation occurs
