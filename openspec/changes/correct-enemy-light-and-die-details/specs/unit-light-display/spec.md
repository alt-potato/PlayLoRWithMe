## ADDED Requirements

### Requirement: Every battle unit's light pool SHALL be carried on a single unit-level wire field set

`UnitSchema` SHALL declare `light: number`, `maxLight: number`, and `reservedLight: number` as required fields. These fields apply uniformly to allies and enemies — the C# serializer (`GameStateSerializer.WriteUnit`) sources them from `BattleUnitModel.PlayPoint`, `MaxPlayPoint`, and `cardSlotDetail?.ReservedPlayPoint`, which exist on every battle unit regardless of faction. `AllyUnitSchema` SHALL inherit these fields from `UnitSchema` (no longer declaring them locally) so ally and enemy unit objects share one canonical light-pool shape on the wire.

A unit with no light pool (the common enemy case) SHALL be represented by `maxLight: 0`. The frontend MUST NOT treat the absence of light fields as valid — every unit object carries them, even when the values are zero.

#### Scenario: Enemy with a light pool survives schema parsing

- **WHEN** the mod serializes an enemy whose `MaxPlayPoint > 0` and the frontend parses the payload through `GameStateSchema`
- **THEN** the resulting enemy `Unit` carries `light`, `maxLight`, and `reservedLight` matching the engine values
- **AND** the fields are not stripped during parse

#### Scenario: Enemy without a light pool carries zero values

- **WHEN** the mod serializes an enemy whose `MaxPlayPoint == 0`
- **THEN** the wire payload still includes `light: 0`, `maxLight: 0`, `reservedLight: 0`
- **AND** the parsed `Unit` carries those three fields as numeric zeros

#### Scenario: Ally unit shape is unchanged for consumers

- **WHEN** a Vue component reads `ally.light`, `ally.maxLight`, or `ally.reservedLight` on an `AllyUnit`
- **THEN** the values resolve through `AllyUnitSchema`'s inherited field set
- **AND** no consumer site needs to be updated as a result of the relocation

### Requirement: The unit card header SHALL render a light-pip row for every unit with a non-zero light pool

`DisplayCard.vue` SHALL mount `<UnitLightDisplay>` for every unit object — ally or enemy — without gating on `isAlly`. `UnitLightDisplay` itself SHALL render its pip row only when `max > 0`, so the row stays hidden for units whose light pool is empty. The pip row shape (lit / reserved / unlit, gold / dim-gold / border-hi) is unchanged from the existing ally rendering — enemies use the same component, the same colours, and the same `Math.max` clamping for current/reserved/max math.

#### Scenario: Ally with a light pool shows the pip row (unchanged)

- **WHEN** an ally's `maxLight > 0`
- **THEN** the unit card header includes a pip row with `current - reserved` gold pips, `reserved` dim-gold pips, and `max - current` unlit pips

#### Scenario: Enemy with a light pool shows the pip row

- **WHEN** an enemy's `maxLight > 0`
- **THEN** the unit card header includes the same pip row layout used for allies
- **AND** the row is visually consistent with the ally rendering (same component, same colours, same hex clip-path)

#### Scenario: Unit with an empty light pool hides the row

- **WHEN** any unit (ally or enemy) has `maxLight == 0`
- **THEN** the unit card header does NOT render a light-pip row
- **AND** the layout flows as if the row did not exist (no empty spacer)
