## ADDED Requirements

### Requirement: Card surfaces SHALL honour rarity colour overrides when present

When a card payload includes `rarityColor`, `rarityRangeIconColor`, `rarityAbilityColor`, or `rarityKeywordColor`, the corresponding card surfaces (`HandCard`, `SlottedCard`, `CardDetail`) MUST set the matching CSS custom property inline on the card's root element so descendant CSS can consume it:

| Payload field | CSS var set inline | Consumed by |
|---|---|---|
| `rarityColor` | `--rarity-color` | rarity-bordered chrome (panel/border) |
| `rarityRangeIconColor` | `--rarity-range-icon-color` | `CardRangeIcon` glyph fill |
| `rarityAbilityColor` | `--rarity-ability-color` | `abilityDesc` body text |
| `rarityKeywordColor` | `--rarity-keyword-color` | bracketed-keyword highlights inside descriptions |

Each surface MUST consume each var with a default fallback so absent values render exactly as they did before this change.

#### Scenario: Hand card with full override set

- **WHEN** a hand card payload includes all four `rarity*Color` fields
- **THEN** the rendered `HandCard` root element carries inline styles setting all four CSS vars
- **AND** the rarity-bordered chrome uses `rarityColor`
- **AND** the `CardRangeIcon` glyph uses `rarityRangeIconColor`
- **AND** the `abilityDesc` body text uses `rarityAbilityColor`
- **AND** bracketed keywords inside the descriptions use `rarityKeywordColor`

#### Scenario: Hand card with only `rarityColor` set

- **WHEN** a hand card payload includes only `rarityColor`, with the other three override fields absent
- **THEN** the rarity-bordered chrome uses `rarityColor`
- **AND** the range glyph, ability text, and keyword highlights render with their pre-change default colours

#### Scenario: Hand card with no overrides falls back to defaults

- **WHEN** a hand card payload omits all four `rarity*Color` fields
- **THEN** the card renders exactly as it did before this change, with no inline `--rarity-*-color` overrides emitted

## MODIFIED Requirements

### Requirement: Card range SHALL be displayed as a glanceable glyph in the upper-right of every card surface

The `HandCard`, `SlottedCard`, and `CardDetail` header surfaces MUST replace the raw `CardRange` enum text with a glyph rendered by a shared `CardRangeIcon` component. The mapping is:

| `range` value  | Glyph                                                |
| -------------- | ---------------------------------------------------- |
| `Near`         | sword (outline-only SVG)                             |
| `Far`          | rifle (outline-only SVG)                             |
| `Instance`     | downward triangle with horizontal lightning bolt SVG |
| `Special`      | sword with small superscript `+` (outline-only SVG)  |
| `FarArea`      | `Σ` (Unicode U+03A3)                                 |
| `FarAreaEach`  | `∀` (Unicode U+2200)                                 |

Glyphs MUST inherit their foreground colour through `var(--rarity-range-icon-color, var(--gold))`, allowing per-card colour overrides via the inline-var pattern while preserving the default gold-on-dark rendering. The component MUST expose the original `range` string via the HTML `title` attribute (for desktop hover) and via `aria-label` (for assistive tech). Unknown values MUST fall back to rendering the raw `range` string as text without producing a runtime error.

#### Scenario: Mass-summation card in hand

- **WHEN** a hand card with `range === "FarArea"` is rendered with no rarity override
- **THEN** the upper-right of the tile shows the `Σ` glyph
- **AND** the glyph's foreground colour resolves to `var(--gold)`
- **AND** hovering the glyph reveals the tooltip text `"FarArea"`

#### Scenario: Self-targeting card in hand

- **WHEN** a hand card with `range === "Instance"` is rendered
- **THEN** the upper-right of the tile shows the downward triangle + lightning bolt SVG
- **AND** the glyph's `aria-label` is `"Instance"`

#### Scenario: Custom-rarity card recolours the glyph

- **WHEN** a hand card payload supplies `rarityRangeIconColor: "#9b00ff"` along with the card's range
- **THEN** the rendered glyph's foreground colour is `#9b00ff`
- **AND** the glyph shape itself is unchanged from the table above

#### Scenario: Unknown range value

- **WHEN** a card has a `range` value that is not in the supported mapping
- **THEN** `CardRangeIcon` falls back to rendering the raw `range` string as text
- **AND** no console error or icon-not-found warning is produced
