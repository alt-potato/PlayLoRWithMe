## ADDED Requirements

### Requirement: Card / key page / passive payloads SHALL accept optional rarity colour overrides

The following schemas in `frontend/app/types/game.ts` MUST each declare four new optional fields:

- `rarityColor?: string` — `#rrggbb` hex
- `rarityRangeIconColor?: string` — `#rrggbb` hex
- `rarityAbilityColor?: string` — `#rrggbb` hex
- `rarityKeywordColor?: string` — `#rrggbb` hex

Schemas affected:

- `CardSchema`
- `SlottedCardEntrySchema`
- `PassiveSchema`
- `KeyPageSchema`
- `AvailableKeyPageSchema`
- `AvailableCardSchema`
- `DeckCardPreviewSchema`

All four fields MUST be optional so that older snapshots, vanilla-rarity payloads, and battle-context payloads continue to parse unchanged.

The C# serializer MUST emit these fields only when the corresponding `Rarity` is non-vanilla AND `CustomRarityProbe.TryGet` returns a non-null result. Vanilla rarities and missing probe results MUST omit all four fields entirely.

#### Scenario: Schema parses a payload with all four overrides

- **WHEN** `GameStateSchema` parses a payload whose hand card carries `rarityColor: "#ff0000"`, `rarityRangeIconColor: "#ff8888"`, `rarityAbilityColor: "#ffffff"`, `rarityKeywordColor: "#ffaa00"`
- **THEN** the parse succeeds and all four fields are present on the resulting `Card` object

#### Scenario: Schema parses a payload with no override fields

- **WHEN** `GameStateSchema` parses a payload whose hand card omits every `rarity*Color` field
- **THEN** the parse succeeds and all four fields are `undefined`

#### Scenario: Schema parses a payload with a partial override set

- **WHEN** `GameStateSchema` parses a payload whose passive carries only `rarityColor: "#ff0000"`
- **THEN** the parse succeeds with `rarityColor === "#ff0000"` and the other three fields `undefined`

#### Scenario: Mod emits no override for vanilla rarity

- **WHEN** the C# serializer writes a card whose `Rarity` is `Common`
- **THEN** the emitted JSON object does NOT contain any of the four `rarity*Color` fields
- **AND** the wire format for the card is byte-for-byte identical to the pre-change wire format

#### Scenario: Mod emits all four overrides for a probe-resolved custom rarity

- **WHEN** the C# serializer writes a card whose `Rarity` is past the vanilla maximum and `CustomRarityProbe.TryGet` returns a non-null `RarityOverride`
- **THEN** the emitted JSON object contains all four `rarity*Color` fields as `#rrggbb` lowercase hex strings sourced from the probe result
