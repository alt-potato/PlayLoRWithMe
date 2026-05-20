# battle-die-state Specification

## Purpose
Speed dice on the battle stage must communicate, at a glance, the states the
vanilla game shows but remote-play players cannot see: which faction a die
belongs to (via its colour), whether a die is locked/immobilized, and whether
a unit is untargetable. This capability defines how `DieRow.vue` renders those
states — faction-derived colours, the lock glyph, and the untargetable
affordances — and how they compose with the existing committed-combat states
(clash, broken, etc.) without erasing identifying colour or the rolled value.

## Requirements

### Requirement: Speed-die colours SHALL be derived from the faction tint to match in-game sprite tinting

The mod samples a single tint per faction (`theme.factionDieColors.ally` / `.enemy`). In-game that tint is multiplied onto pre-shaded die sprites, so the rendered background reads much darker than the tint and the rendered numeral reads brighter and more saturated. The frontend MUST reproduce this split rather than painting the raw tint flat: a `deriveDieColors` helper (in `utils/color.ts`) MUST convert a tint into a darkened background and a brightened, saturated numeral, both keeping the tint's hue.

The inner hex of every speed die in `DieRow.vue` MUST use the derived background (`--die-faction-fill`) and the derived numeral colour (`--die-accent-color`). The faction-level derived values MUST be declared in `app.vue`'s `:root` (`--die-{ally,enemy}-bg` / `--die-{ally,enemy}-num`, computed from the vanilla base tints) so the UI renders correctly before the `hello` payload arrives, and `applyTheme` MUST overwrite them from the sampled tints at runtime. A per-unit `dieColor` (CDC) is itself a single tint and MUST be split the same way; an explicit per-unit `dieAccentColor` overrides the derived numeral.

This derived background is the *base* colour for the inner hex. Existing dice states that previously coloured the inner hex (open / pending / clash / unopposed / etc.) continue to override it for those states. The derived colours apply to the normal (empty / rolled / locked / untargetable) display path. Because the background is darkened and the numeral is brightened, the pair carries its own contrast — no outline or halo on the numeral is required.

#### Scenario: Ally die renders with the derived ally colours

- **WHEN** a `DieRow` is rendered for an ally unit with `isAlly === true`, no card slotted, and no special state
- **THEN** the `.hex-inner` background resolves to the ally derived background (`--die-faction-fill`, defaulting to `var(--die-ally-bg)`)
- **AND** the numeral colour resolves to the ally derived numeral (`--die-accent-color`, defaulting to `var(--die-ally-num)`)

#### Scenario: Enemy die renders with the derived enemy colours

- **WHEN** a `DieRow` is rendered for an enemy unit with `isAlly === false` and no special state
- **THEN** the `.hex-inner` background resolves to the enemy derived background and the numeral to the enemy derived numeral

#### Scenario: deriveDieColors darkens the background and brightens the numeral

- **WHEN** `deriveDieColors` is given a faction tint (e.g. the vanilla enemy tint `#e2a3c4`)
- **THEN** it returns a background whose hue matches the tint and whose lightness is markedly lower (≈half)
- **AND** a numeral whose hue matches the tint and whose saturation and lightness are higher than the background, so the value stays legible against it

#### Scenario: Existing committed-state colours still take precedence

- **WHEN** a die is in clash state (a slotted card with `clash === true`)
- **THEN** the `.hex-wrap` outer background uses the existing clash colour
- **AND** the inner hex follows the existing clash rule, not the derived faction colours

### Requirement: Locked dice SHALL display a lock glyph in place of the rolled value, and SHALL NOT be selectable for slotting

When a `SpeedDie` payload has `locked === true` AND `staggered === false`, the `DieRow.vue` rendering MUST replace the rolled value with a centred lock glyph. The underlying faction-coloured inner-hex fill MUST remain visible around the glyph (the glyph itself is monochrome and additive, satisfying the "do not completely hide the colour" constraint). The rolled value MUST NOT be visible — the lock glyph is the sole indicator.

Locked dice MUST NOT respond to slot-selection clicks. The early-return guard in `handleSlotClick` MUST treat `locked` identically to `staggered` and broken-unit states: the slot does not enter the "open" / "available" affordance, no green pulse, no pointer cursor, and the slot remove-button MUST NOT appear on a locked slot.

The broken state takes priority over the locked state. When a die is *both* `locked === true` AND `staggered === true`, the die renders in the broken state only (`✕` on the crimson hex). The lock glyph MUST NOT be rendered on a broken die — the destroyed-die cue is the dominant signal and the lock affordance is moot because a broken die is already unusable.

The lock glyph MUST be styled in pure CSS / inline SVG and MUST NOT depend on a prefab-extracted sprite. The glyph element MUST have `pointer-events: none` so the underlying slot interaction layer continues to handle (and now reject) the clicks.

#### Scenario: Locked-and-not-broken die hides the value

- **WHEN** an ally `SpeedDie` has `locked === true` and `staggered === false`
- **THEN** the die renders with the ally faction fill on the inner hex
- **AND** a lock glyph is rendered in the centre of the hex
- **AND** the rolled value is NOT visible
- **AND** the underlying faction fill colour is visible around the glyph (the glyph does not completely mask the colour)

#### Scenario: Locked die rejects slot-selection clicks

- **WHEN** an ally `SpeedDie` has `locked === true` and `staggered === false` AND the player taps the slot
- **THEN** no slot-selection state is entered (`selectingSlot` stays unchanged)
- **AND** no green pulse / open affordance appears on the slot
- **AND** the remove-button does NOT appear on the slot

#### Scenario: Locked-and-broken die — broken takes priority

- **WHEN** an enemy `SpeedDie` has `locked === true` AND `staggered === true`
- **THEN** the die renders in the broken state (`✕` on the crimson hex)
- **AND** NO lock glyph is rendered on the die
- **AND** the slot behaviour mirrors the existing broken-die behaviour

#### Scenario: Unlocked die has no lock overlay

- **WHEN** a `SpeedDie` has `locked === false` (or the field is absent)
- **THEN** no lock glyph is rendered on the die

### Requirement: Per-unit speed-die colours SHALL be sampled live from the rendered UI

For each `BattleUnitModel` whose `view.speedDiceSetterUI` has at least one initialised `SpeedDiceUI`, the mod MUST sample the live `_rouletteImg.color` from the first slot via reflection and emit it as an optional `dieColor: string` (`#rrggbb` lowercase hex) on the unit's wire payload. The frontend MUST honour `unit.dieColor` by setting `--die-faction-fill` inline on the unit's container so every child `DieRow` reads the per-unit colour.

When the unit has no `SpeedDiceUI` yet (pre-battle preview, between waves, dead unit cleanup) the serializer MUST omit `dieColor` entirely. The frontend's per-faction `--die-ally-fill` / `--die-enemy-fill` defaults take over for those cases.

The sampling MUST go through reflection because `SpeedDiceUI._rouletteImg` is private. Field-info binding MUST happen once per session, cached, with a single warning logged on bind failure. The sampling MUST NOT take any compile-time dependency on a third-party mod's assembly — capturing whatever upstream applies (CustomSpeedDiceColor's per-floor fallback, an XML-list entry it loads, or any other speed-die tint mod) is the explicit goal.

Sprite swaps and other UI changes mods may apply are out of scope; only the inner-hex fill colour is captured.

#### Scenario: Live unit emits sampled colour

- **WHEN** the player has a speed-die tint mod installed (or any combination thereof) AND a battle unit's `SpeedDiceUI[0]._rouletteImg.color` resolves to `Color(0.47, 0.78, 0.16, 1.0)`
- **THEN** the unit's serialized payload includes `dieColor: "#78c828"` (rounded from the float RGB to the byte-hex form)
- **AND** the frontend renders that unit's dice with `--die-faction-fill: #78c828`
- **AND** other units in the same battle emit their own sampled colours independently

#### Scenario: Unit without a SpeedDiceUI yet — no override emitted

- **WHEN** a unit exists in the battle model but its `view.speedDiceSetterUI.SpeedDicesCount` is `0` (e.g. before the first Init pass or during BattleSetting preview)
- **THEN** the serializer omits `dieColor` from that unit's payload
- **AND** the frontend's faction-default CSS vars render the dice

#### Scenario: Reflection bind fails — graceful fallback

- **WHEN** `SpeedDiceUI._rouletteImg` cannot be located via reflection (LoR has renamed the private field in a future patch)
- **THEN** a single `[CustomDiceColorProbe]` warning is logged
- **AND** every subsequent `TryGet` call returns null
- **AND** the frontend falls back to the per-faction default colours
- **AND** the mod continues running without further error

#### Scenario: Mod has no compile-time dependency on speed-die tint mods

- **WHEN** a contributor inspects `mod/PlayLoRWithMe.csproj`
- **THEN** no `<Reference>` element names `Patty_SpeedDiceColor_MOD` or any other speed-die tint mod
- **AND** the mod builds and runs on systems without those subscriptions

### Requirement: Untargetable units SHALL display row-level and per-die affordances

When `unit.targetable === false`, the unit's card (`unit/DisplayCard.vue`) MUST display two affordances:

1. A row-level opacity reduction on the card body (header excepted), signalling "not selectable"
2. A crosshatch SVG mask overlay on each die in the row's `DieRow` components

The body opacity reduction MUST be approximately `0.6` (or another value that signals "not selectable" without obscuring information); the header (unit name and turn badge) stays at full opacity so the unit remains identifiable. The crosshatch overlay MUST be additive — the underlying faction fill and rolled value MUST remain visible underneath the stripes. The crosshatch overlay MUST have `pointer-events: none`.

A text chip near the unit name was prototyped but removed: at the available header sizing it crowded the name on narrow viewports without adding information the row dim and per-die crosshatch do not already convey.

The untargetable affordances MUST compose with the lock overlay: a die that is both locked and on an untargetable unit shows both the lock glyph and the crosshatch.

The untargetable affordances MUST NOT replace the existing committed-state rendering: a broken die on an untargetable unit still renders as ✕; a slotted clash die still shows the clash colour. The crosshatch overlay applies on top of all states.

#### Scenario: Untargetable enemy displays row dim and crosshatch

- **WHEN** an enemy unit has `unit.targetable === false`
- **THEN** the unit card's body opacity is reduced to approximately `0.6` while the header stays at full opacity
- **AND** every die in the unit's `DieRow` has a crosshatch SVG mask overlay
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
- **THEN** no crosshatch overlay is rendered on the dice
- **AND** the card body opacity is unchanged

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
