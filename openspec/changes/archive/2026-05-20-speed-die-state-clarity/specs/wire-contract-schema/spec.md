## ADDED Requirements

### Requirement: `UnitSchema` SHALL carry an optional `dieColor` override

`UnitSchema` (and by inheritance `AllyUnitSchema`) in `frontend/app/types/game.ts` MUST declare a new optional `dieColor: z.optional(z.string())` field. When present, the value is a `#rrggbb` lowercase hex string sourced from a `CustomSpeedDiceColor` mod entry whose `BookID/BookUniqueID` (or `DefaultBookID/DefaultBookUniqueID`) matches the unit and whose `Faction` matches the unit's faction.

The C# serializer MUST emit `dieColor` only when the CDC probe returns a non-null match. Absent values fall back to the per-faction CSS defaults declared in `app.vue`'s `:root`.

#### Scenario: Schema parses a unit with `dieColor`

- **WHEN** `GameStateSchema` parses a payload whose ally unit includes `dieColor: "#78c828"`
- **THEN** the parse succeeds and `unit.dieColor === "#78c828"`

#### Scenario: Schema parses a unit without `dieColor`

- **WHEN** `GameStateSchema` parses a payload whose unit omits `dieColor`
- **THEN** the parse succeeds and `unit.dieColor` is `undefined`

### Requirement: `SpeedDieSchema` SHALL carry an optional `locked` field

`SpeedDieSchema` in `frontend/app/types/game.ts` MUST declare a new optional field `locked: z.optional(z.boolean())`. The field's value, when present, indicates that the die cannot be commanded this turn, derived on the C# side as `(!unit.bufListDetail.IsControlable()) || (!die.isControlable)`.

The field MUST be optional so that older snapshots and out-of-battle preview dice continue to parse unchanged. The C# serializer MUST emit `locked` on every battle-context `SpeedDie` payload (both `unit.speedDice` and the pre-roll placeholder dice path in `WriteSpeedDice`). Preview-die emission sites outside `WriteSpeedDice` MAY omit the field.

#### Scenario: Schema parses a payload with `locked: true`

- **WHEN** `GameStateSchema` parses a payload whose `SpeedDie` includes `locked: true`
- **THEN** the parse succeeds and `die.locked === true` on the resulting object

#### Scenario: Schema parses a payload without `locked`

- **WHEN** `GameStateSchema` parses a payload whose `SpeedDie` omits `locked`
- **THEN** the parse succeeds and `die.locked` is `undefined`

#### Scenario: Mod emits `locked` for battle dice

- **WHEN** the C# serializer writes a battle-context `SpeedDie` whose owning unit has a paralysis buff with `IsControllable == false`
- **THEN** the emitted JSON object contains `locked: true`

#### Scenario: Mod emits `locked: false` for normal dice

- **WHEN** the C# serializer writes a battle-context `SpeedDie` whose owning unit has no immobilizing buffs and the die's own `isControlable` is true
- **THEN** the emitted JSON object contains `locked: false`

### Requirement: The `hello` message schema SHALL accept an optional `theme` block

The `ServerMessageSchema` variant corresponding to the `hello` message MUST accept an optional top-level `theme` field whose shape includes:

```ts
theme: z.optional(z.object({
  factionDieColors: z.optional(z.object({
    ally: z.string(),
    enemy: z.string(),
  })),
}))
```

The `theme` block MUST also be admissible as a delta entry in subsequent state-push messages, so the late-probe retry path can ship the colours after the hello has already been received.

The frontend MUST cache `theme.factionDieColors.ally` and `theme.factionDieColors.enemy` into CSS custom properties on the document root (`--die-ally-fill`, `--die-enemy-fill`) when present. Absent values leave the existing CSS-default fallback values in place.

#### Scenario: Hello with theme block parses cleanly

- **WHEN** the client receives a `hello` message containing `theme: {factionDieColors: {ally: "#3aaad8", enemy: "#d83a6d"}}`
- **THEN** `ServerMessageSchema.safeParse` returns success
- **AND** the frontend writes `#3aaad8` and `#d83a6d` to the corresponding CSS vars on the document root

#### Scenario: Hello without theme block parses cleanly

- **WHEN** the client receives a `hello` message with no `theme` field
- **THEN** `ServerMessageSchema.safeParse` returns success
- **AND** the document root CSS vars are not modified (the fallback declared in `app.vue`'s `:root` remains active)

#### Scenario: State push with theme delta updates vars

- **WHEN** the client receives a state message whose payload includes `theme: {factionDieColors: {...}}` after the hello has already been processed
- **THEN** the frontend overrides the cached CSS vars with the new values
- **AND** the rest of the state delta is applied normally
