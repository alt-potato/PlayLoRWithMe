## ADDED Requirements

### Requirement: `KeyPage` and `AvailableKeyPage` SHALL carry an optional `rarity` field

`KeyPageSchema` and `AvailableKeyPageSchema` MUST expose an optional
`rarity?: string` field whose value, when present, is one of the strings
emitted by the C# `Rarity` enum (`"Common"`, `"Uncommon"`, `"Rare"`,
`"Unique"`, `"Special"`).

The C# serializer MUST emit `rarity` on:

- every entry in `availableKeyPages`
- the librarian-owned `keyPage` field on each `LibrarianEntry`

The C# serializer MUST NOT emit `rarity` on battle-unit `keyPage` payloads,
so the field is naturally absent from combat contexts.

#### Scenario: Available key page includes rarity

- **WHEN** the serializer writes an `availableKeyPages` entry
- **THEN** the resulting JSON object includes a `rarity` string sourced
  from `BookXmlInfo.Rarity.ToString()`

#### Scenario: Librarian-owned key page includes rarity

- **WHEN** the serializer writes a `LibrarianEntry`'s `keyPage` field
- **THEN** the resulting JSON object includes a `rarity` string sourced
  from `BookXmlInfo.Rarity.ToString()`

#### Scenario: Battle-unit key page omits rarity

- **WHEN** the serializer writes a battle-unit `keyPage` object
- **THEN** the resulting JSON object does NOT include a `rarity` field

#### Scenario: Schema accepts payload without rarity

- **WHEN** `GameStateSchema` parses a payload whose `keyPage` lacks
  `rarity` (e.g. an older snapshot or a battle payload)
- **THEN** parsing succeeds and the field is `undefined` on the resulting
  object
