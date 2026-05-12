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

### Requirement: Per-unit speed-die colour overrides SHALL be honoured for CustomSpeedDiceColor mod compatibility

When the workshop mod `Patty_SpeedDiceColor_MOD` is loaded and a battle unit's book or default-book matches one of its `CustomSpeedDiceXML.SpeedDice` entries (book-ID + package-ID match, with faction match), the mod MUST emit an optional `dieColor: string` (`#rrggbb` lowercase hex) on the unit's wire payload. The frontend MUST honour `unit.dieColor` by setting `--die-faction-fill` inline on the unit's `DieRow` components, overriding the faction-class default for that unit only.

When the mod is not loaded, or no entry matches the unit's books, the serializer MUST omit `dieColor` entirely. Units without an override continue to use the per-faction `--die-ally-fill` / `--die-enemy-fill` CSS vars as the inner-hex base.

The mod MAY take a compile-time HintPath reference to `Patty_SpeedDiceColor_MOD.dll` for type-safe API access, but MUST mark it `Private=False` so the optional DLL is never bundled into our build output. The runtime soft-dep contract is enforced via an assembly-presence check (`AppDomain.CurrentDomain.GetAssemblies()`) that gates entry into a separate, non-inlined method whose body references the CDC types — when CDC is absent the gated method is never JIT'd and the CLR never resolves CDC types, so the mod stays loadable.

Only the dice colour is read; sprite swaps (`FolderName`, `BaseChange`) and other CDC-specific behaviour are out of scope.

#### Scenario: CDC is loaded — per-unit override emits `dieColor` on the wire

- **WHEN** the player has `Patty_SpeedDiceColor_MOD.dll` installed AND a battle unit's `Book.BookId` matches a CDC entry that declares RGBA `(120, 200, 40, 255)` for the ally faction
- **THEN** the unit's serialized payload includes `dieColor: "#78c828"`
- **AND** the frontend's `DieRow` rendering of that unit's dice uses `--die-faction-fill: #78c828`
- **AND** other units in the same battle without a matching CDC entry continue to use the per-faction default

#### Scenario: CDC is loaded — lookup uses BookID or DefaultBookID

- **WHEN** a CDC entry sets only `DefaultBookID + DefaultBookUniqueID` for a unit whose live key page (`unit.Book.BookId`) does not match
- **THEN** the lookup MUST still match via the unit's default book (`unit.UnitData.unitData.defaultBook.BookId`)
- **AND** the matching entry's `Faction` field MUST equal `unit.faction`

#### Scenario: CDC is not loaded — no override fields emitted

- **WHEN** the player does not have `Patty_SpeedDiceColor_MOD.dll` installed
- **THEN** the assembly-presence gate returns false
- **AND** the CDC-typed lookup method is never JIT'd
- **AND** no `dieColor` field appears in any wire payload
- **AND** the frontend renders speed dice using the per-faction default CSS vars
- **AND** the mod's own DLL loads and runs without error

#### Scenario: Mod build does not bundle CDC

- **WHEN** the mod is built and `mod/bin/Debug/PlayLoRWithMe/Assemblies/` is inspected
- **THEN** no copy of `Patty_SpeedDiceColor_MOD.dll` is present in our output
- **AND** the csproj reference is marked `Private=False`

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
