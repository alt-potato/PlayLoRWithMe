## ADDED Requirements

### Requirement: Speed-die inner hex SHALL be filled with the unit's faction colour

The inner hex element of every speed die in `DieRow.vue` MUST be filled with the per-faction colour shipped via `theme.factionDieColors`: ally rows use `--die-ally-fill`, enemy rows use `--die-enemy-fill`. Both vars MUST be declared in `app.vue`'s `:root` with hardcoded fallback values so the UI renders correctly before the `hello` payload arrives.

This faction fill is the *base* colour for the inner hex. Existing dice states that previously coloured the inner hex (open / pending / clash / unopposed / etc.) continue to override it for those states. The faction fill applies to the normal (empty / rolled / locked / untargetable) display path.

#### Scenario: Ally die renders with the ally faction fill

- **WHEN** a `DieRow` is rendered for an ally unit with `isAlly === true`, no card slotted, and no special state
- **THEN** the `.hex-inner` element's background colour resolves to `var(--die-ally-fill, <hardcoded-default>)`

#### Scenario: Enemy die renders with the enemy faction fill

- **WHEN** a `DieRow` is rendered for an enemy unit with `isAlly === false` and no special state
- **THEN** the `.hex-inner` element's background colour resolves to `var(--die-enemy-fill, <hardcoded-default>)`

#### Scenario: Existing committed-state colours still take precedence

- **WHEN** a die is in clash state (a slotted card with `clash === true`)
- **THEN** the `.hex-wrap` outer background uses the existing clash colour
- **AND** the inner hex follows the existing clash rule, not the faction fill

### Requirement: Locked dice SHALL display a lock overlay that preserves the faction fill

When a `SpeedDie` payload has `locked === true` and the die is not in a committed combat state (slotted with a target / clash / broken-and-not-locked), the `DieRow.vue` rendering MUST overlay a lock glyph on top of the inner hex. The glyph MUST be additive — the underlying faction fill and rolled value MUST remain visible behind it.

When a die is *both* `locked === true` and `staggered === true`, the broken state still renders (`✕` on the crimson hex), but with the lock glyph also overlaid so the player can see why the die was destroyed (mirroring vanilla LoR's `_lockDiceRoot` activation for the `breaked && locked` case).

The lock glyph MUST be styled in pure CSS / inline SVG and MUST NOT depend on a prefab-extracted sprite. The glyph MUST have `pointer-events: none` so clicks pass through to the existing slot interaction layer.

#### Scenario: Locked-and-not-broken die

- **WHEN** an ally `SpeedDie` has `locked === true` and `staggered === false`
- **THEN** the die renders with the ally faction fill on the inner hex
- **AND** a lock glyph overlay is positioned on top of the inner hex
- **AND** the rolled value remains visible at reduced opacity behind the glyph
- **AND** the underlying faction fill colour is visible around the glyph (the overlay does not completely mask the colour)

#### Scenario: Locked-and-broken die

- **WHEN** an enemy `SpeedDie` has `locked === true` and `staggered === true`
- **THEN** the die renders in the broken state (`✕` on the crimson hex)
- **AND** the lock glyph is overlaid on top so the player can see the die is locked

#### Scenario: Unlocked die has no lock overlay

- **WHEN** a `SpeedDie` has `locked === false` (or the field is absent)
- **THEN** no lock glyph is rendered on the die

#### Scenario: Lock overlay does not interfere with slot clicks

- **WHEN** an ally die has `locked === true` and the user taps the underlying slot
- **THEN** the click is handled by the slot interaction layer
- **AND** the lock overlay does not consume the event

### Requirement: Untargetable units SHALL display row-level and per-die affordances

When `unit.targetable === false`, the unit's row in `Stage.vue` (or wherever the unit name and die rows are co-rendered) MUST display two affordances:

1. A small "⚠ untargetable" chip near the unit's name
2. A crosshatch SVG mask overlay on each die in the row's `DieRow` components

The row MUST also reduce its opacity to approximately `0.6` (or another value that signals "not selectable" without obscuring information). The crosshatch overlay MUST be additive — the underlying faction fill and rolled value MUST remain visible underneath the stripes. The crosshatch overlay MUST have `pointer-events: none`.

The untargetable affordances MUST compose with the lock overlay: a die that is both locked and on an untargetable unit shows both the lock glyph and the crosshatch.

The untargetable affordances MUST NOT replace the existing committed-state rendering: a broken die on an untargetable unit still renders as ✕; a slotted clash die still shows the clash colour. The crosshatch overlay applies on top of all states.

#### Scenario: Untargetable enemy displays chip and crosshatch

- **WHEN** an enemy unit has `unit.targetable === false`
- **THEN** the unit's name area carries a "⚠ untargetable" chip
- **AND** every die in the unit's `DieRow` has a crosshatch SVG mask overlay
- **AND** the row's overall opacity is approximately `0.6`
- **AND** each die's rolled value remains readable through the crosshatch

#### Scenario: Untargetable + locked composes both overlays

- **WHEN** a unit is untargetable AND one of its dice has `locked === true`
- **THEN** that die shows both the lock glyph and the crosshatch overlay
- **AND** both overlays preserve the underlying faction fill

#### Scenario: Targeting flow still respects untargetable

- **WHEN** the user has a card pending target selection AND tries to target an untargetable enemy's die
- **THEN** the click MUST NOT register as a valid target (existing `canBeTargeted = isTargeting && unit.targetable` rule unchanged)

#### Scenario: Targetable unit shows no untargetable affordance

- **WHEN** a unit has `unit.targetable === true` (or the field is absent / defaults to true)
- **THEN** no chip is rendered near the name
- **AND** no crosshatch overlay is rendered on the dice
- **AND** the row's opacity is unchanged

### Requirement: Out-of-battle preview dice SHALL use the faction fill but no overlays

Preview dice rendered outside a battle context (the `SettingView` formation/deck preview, `KeyPageDetail` panels, any other surface that renders a speed-die shape without a `BattleUnitModel` backing) MUST use the same faction-coloured base fill so the visual vocabulary stays consistent. These surfaces MUST NOT render lock or untargetable overlays — there is no battle state to consult.

#### Scenario: SettingView preview die uses faction fill

- **WHEN** `SettingView` renders a preview die for an ally key page
- **THEN** the die's inner hex uses `var(--die-ally-fill, <default>)`
- **AND** no lock glyph or crosshatch overlay is rendered

#### Scenario: KeyPageDetail preview die uses faction fill

- **WHEN** `KeyPageDetail` renders a preview die
- **THEN** the die's inner hex uses the corresponding faction fill
- **AND** the surface emits no overlay elements
