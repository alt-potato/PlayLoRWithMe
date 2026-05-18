# card-keyword-highlighting Specification

## Purpose
TBD - created by archiving change card-keyword-highlighting. Update Purpose after archive.
## Requirements
### Requirement: Bracketed keywords in combat-card description text SHALL be visually highlighted

Every combat-card description surface that renders `Card.abilityDesc` or per-die `desc` text MUST highlight bracketed keyword runs (substrings matching `/\[[^\]]+\]/`) in a distinct bright-gold colour, matching the base-game convention. The surrounding brackets themselves MUST be stripped from the display — only the inner keyword text (e.g. `On Use`, `On Clash Win`, `Counter`, `Reroll`) is rendered, in the highlight colour.

The highlight colour MUST resolve through `var(--rarity-keyword-color, <default>)`, where `<default>` is the existing bright-gold value declared in `app.vue`'s `:root` block. When the surrounding card surface sets `--rarity-keyword-color` inline (because the card payload supplied a `rarityKeywordColor` override), the keyword colour MUST follow that override.

The highlight MUST apply at every site where description text is shown: the hand card's detail pane (both the card-level `abilityDesc` and per-die `desc`) and the `CardDetail` modal sheet (both card-level and per-die). The highlight colour MUST override any parent colour tint (including the per-die-type tints of `hcard-die-desc--atk/def/standby`), so a keyword reads as a keyword regardless of which die row it sits in.

Descriptions that contain no bracketed runs MUST render exactly as they did before this capability — a single plain text segment with the container's inherited colour and weight.

#### Scenario: Card ability text with a single keyword

- **WHEN** a card has `abilityDesc === "[On Use] Gain 1 Haste."` and its detail is shown in either the hand-card detail pane or the `CardDetail` modal, with no rarity override in effect
- **THEN** the rendered text reads `On Use Gain 1 Haste.`
- **AND** `On Use` is rendered in the bright-gold highlight colour (the default `--rarity-keyword-color` value)
- **AND** ` Gain 1 Haste.` is rendered in the container's default description colour
- **AND** no literal `[` or `]` character is visible

#### Scenario: Die description with multiple keywords

- **WHEN** a die's `desc` is `"[On Clash Win] Inflict 2 Bleed. [On Hit] Draw 1 card."`
- **THEN** the rendered text reads `On Clash Win Inflict 2 Bleed. On Hit Draw 1 card.`
- **AND** both `On Clash Win` and `On Hit` are rendered in the bright-gold highlight colour
- **AND** the non-keyword text is rendered in the container's colour

#### Scenario: Keyword inside a per-die-tinted paragraph

- **WHEN** an Atk die's `desc` is `"[On Use] Inflict 2 damage."` rendered inside the hand-card detail pane
- **THEN** ` Inflict 2 damage.` is rendered with the light-red atk tint
- **AND** `On Use` is rendered in the bright-gold highlight colour (overriding the parent tint)

#### Scenario: Custom-rarity card recolours its keywords

- **WHEN** a card payload supplies `rarityKeywordColor: "#33ddff"` and its `abilityDesc` is `"[On Use] Gain 1 Haste."`
- **THEN** the rendered `On Use` keyword colour is `#33ddff`
- **AND** the non-keyword text is rendered in the container's default colour (or the card's `rarityAbilityColor` if also supplied)

#### Scenario: Description with no bracketed keywords

- **WHEN** a card's `abilityDesc` is `"Deal damage equal to Power."`
- **THEN** the rendered text is exactly `Deal damage equal to Power.` in the container's default colour
- **AND** no highlighted spans are produced

#### Scenario: Malformed bracket in description text

- **WHEN** a card's `abilityDesc` is `"[On Use Gain 1 Haste."` (unclosed bracket)
- **THEN** the full string is rendered as plain (non-highlighted) text
- **AND** no runtime error is produced

