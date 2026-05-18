# key-page-rarity-indicator Specification

## Purpose
TBD - created by archiving change key-page-rarity-indicator. Update Purpose after archive.
## Requirements
### Requirement: Customization surfaces SHALL display key page rarity as a colored outline

Customization surfaces SHALL render the key page's rarity as a colored
border on the tile or panel that displays the page. The picker grid
(`KeyPageTab.vue` `.kp-tile`), the shared detail pane (`KeyPageDetail.vue`
`.kp-detail`), and the passive-source picker grid (`PassivesTab.vue`
`.source-tile`) MUST tint their tile/panel border via the `--rarity-color`
inline CSS custom property. Components MUST set `--rarity-color` from a
per-rarity class lookup against `--rarity-common`, `--rarity-uncommon`,
`--rarity-rare`, `--rarity-unique`, `--rarity-special`, OR from a
payload-supplied hex override (`rarityColor`) when present.

When neither the `rarity` field nor `rarityColor` is present in the
payload, surfaces MUST fall back to the default `--border` color and
render no rarity affordance.

#### Scenario: Picker tile shows rarity outline from vanilla rarity

- **WHEN** the EditPanel's Key Page tab renders an `AvailableKeyPage` whose
  `rarity` is `"Unique"` and no `rarityColor` is supplied
- **THEN** the corresponding `.kp-tile` element carries an inline style
  setting `--rarity-color` to `var(--rarity-unique)`
- **AND** its CSS resolves the tile's border colour to `var(--rarity-color, var(--border))`

#### Scenario: Picker tile shows rarity outline from override hex

- **WHEN** the EditPanel's Key Page tab renders an `AvailableKeyPage` whose
  payload includes `rarityColor: "#ff00ff"`
- **THEN** the corresponding `.kp-tile` element carries an inline style
  setting `--rarity-color` to `#ff00ff`
- **AND** the tile's rendered border colour is `#ff00ff`

#### Scenario: Detail pane shows rarity outline

- **WHEN** `KeyPageDetail.vue` is rendered with a key page that has a
  `rarity` value
- **THEN** the `.kp-detail` panel resolves its border colour through
  `--rarity-color` (either via a vanilla rarity-class lookup or via a
  payload `rarityColor` override)

#### Scenario: Passive-source tile shows rarity outline

- **WHEN** `PassivesTab.vue` renders a source `.source-tile` for a key
  page with a `rarity` value
- **THEN** the tile's border resolves through `--rarity-color` using the
  same vanilla-lookup or payload-override resolution

#### Scenario: Missing rarity falls back to default

- **WHEN** `KeyPageDetail.vue` receives a key page payload without a
  `rarity` field (e.g. a battle-unit key page)
- **THEN** the panel renders no rarity outline and uses the default
  `--border` color

### Requirement: Equipped indicator SHALL coexist with rarity outline

The existing `kp-tile--equipped` left-border (gold) MUST continue to mark
the equipped key page even when the rarity outline is present. The
`kp-tile--selected` full-gold border MUST continue to override the rarity
outline on all four sides while the tile is selected.

#### Scenario: Equipped tile keeps gold left-border

- **WHEN** an `.kp-tile` is both equipped and has a `rarity` of `"Rare"`
- **THEN** the tile's left border is gold (`--gold-bright`)
- **AND** the top, right, and bottom borders are colored by `--rarity-rare`

#### Scenario: Selected tile masks rarity outline

- **WHEN** an `.kp-tile` is selected
- **THEN** all four sides of the border are gold (`--gold-bright`),
  regardless of rarity

### Requirement: Combat surfaces SHALL NOT display key page rarity

Combat rendering paths SHALL NOT display a key page rarity indicator and
SHALL NOT receive a `rarity` field on their key page payloads. Specifically,
the BattleSetting detail panel, the in-battle display card, and any other
component that renders a battle-context key page MUST NOT show a rarity
outline. The serializer MUST enforce this by omitting `rarity` from
battle-context key page emission sites.

#### Scenario: Battle-unit key page omits rarity

- **WHEN** the C# serializer writes a battle-unit `keyPage` object
- **THEN** the `rarity` field is not included in the JSON payload

#### Scenario: SettingDetailPanel renders no rarity outline

- **WHEN** the BattleSetting view renders the formation/deck preview
- **THEN** no rarity outline appears on any rendered key page element

