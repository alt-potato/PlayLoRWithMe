## MODIFIED Requirements

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
