# combat-targeting Specification

## Purpose
TBD - created by archiving change targeting-fixes. Update Purpose after archive.
## Requirements
### Requirement: Frontend enemy-die targetability SHALL mirror vanilla `BattleUnitModel.IsTargetableUnit` for cross-faction attacks

When a player is choosing the target of an ally card (`isTargeting === true`), every enemy speed die in `DieRow.vue` MUST be a valid click target whenever its owning unit passes the unit-level targetability gate, regardless of the die's `staggered` or `locked` (Stun-overlay) state. The frontend MUST NOT block target-selection clicks based on those per-die visual states alone.

Per-die `controllable === false` (the wire's representation of vanilla's per-die `!isControlable`, e.g. clock-EGO) MUST still block the click, mirroring `LOR_BattleUnit_UI.SpeedDiceUI.OnClickSpeedDice`'s early return. Rejected clicks MUST play the existing red rejection flash on the die.

The unit-level gate (`canBeTargeted`) for an enemy unit MUST require all of: a target selection is in progress, the wire's `targetable === true`, the unit is NOT dead (`!isDead(unit)`), and the unit is NOT a restricted target (`!isRestrictedTarget(unitId)`).

#### Scenario: Player targets a stun-locked enemy die

- **WHEN** the player has a non-Instance card selected and taps an enemy speed die whose owning unit has Stun (`d.locked === true`) and whose `controllable` is unset or `true`
- **THEN** the die is treated as a valid target
- **AND** the click triggers `onTargetDieClick`, posting a `playCard` action with `targetUnitId` and `targetDiceSlot`
- **AND** the red rejection flash does NOT play

#### Scenario: Player targets a staggered enemy die

- **WHEN** the player has a non-Instance card selected and taps an enemy speed die whose `staggered === true` and whose `controllable` is unset or `true`
- **THEN** the die is treated as a valid target
- **AND** the click triggers `onTargetDieClick`
- **AND** the red rejection flash does NOT play

#### Scenario: Player taps a clock-EGO disabled enemy die

- **WHEN** the player has a card selected and taps an enemy die whose `controllable === false`
- **THEN** the die is NOT a valid target
- **AND** the click triggers the red rejection flash on that die
- **AND** no action is posted

#### Scenario: Player taps a die on an `targetable === false` enemy

- **WHEN** the player has a card selected and taps any die on an enemy with the wire's `targetable === false` (Justitia-style invincibility, removed-NotTargetable buff)
- **THEN** the die is NOT a valid target
- **AND** the click triggers the red rejection flash on that die

### Requirement: Dead enemies SHALL NOT show the green targetable-die outline

A speed die belonging to a unit where `isDead(unit) === true` (`unit.hp <= 0`) MUST NOT receive the `.hex-target` highlight or the `.slot-target` row class, even when an ally is selecting a target. This MUST hold regardless of whether the wire's `unit.targetable` flag has transitioned to `false` yet — the death-transition window (during which `hp <= 0` can be observed before vanilla sets `_isKnockout`) MUST behave the same as steady-state death.

This requirement complements (does not replace) the existing dead-unit affordance convention (DEAD badge, hidden slotted cards, hidden incoming chips, hidden arrows). All four affordances MUST hide together.

#### Scenario: Targeting is active and an enemy is dead

- **WHEN** the player is selecting a target with a non-Instance card, and the candidate enemy unit has `hp <= 0`
- **THEN** none of that enemy's speed dice receive the green `.hex-target` highlight
- **AND** none of the dice's rows receive the `.slot-target` class (no pointer cursor)
- **AND** clicking the dead enemy's dice does nothing (neither posts an action nor flashes the rejection cue)

#### Scenario: Death transition with stale `targetable` flag

- **WHEN** the player is targeting and a state push arrives with `hp: 0` but `targetable: true` (transient window before `_isKnockout` flips)
- **THEN** the green targetable outline still does NOT appear on that unit's dice (the `isDead` guard wins)

#### Scenario: Living staggered enemy still shows the green outline

- **WHEN** the player is targeting and the candidate enemy unit is staggered (`turnState === "BREAK"`) but still alive (`hp > 0`)
- **THEN** that enemy's non-broken dice still receive the green targetable highlight as today
- **AND** the dice remain valid click targets (mirrors vanilla's allowance)

### Requirement: The "green targetable" affordance SHALL be limited to dice that can actually accept a target click

A speed die SHALL only render the green `.hex-target` background and the `.slot-target` pointer-cursor row class when all of the following hold: targeting is in progress, the row belongs to an enemy unit (`!isAlly`), `canBeTargeted === true` (per the unit-level gate above), and the die's own state does not prevent the click for reasons other than its faction-side targetability (i.e. per-die `controllable !== false`). The redundant `!die.staggered` filter that today still drives the highlight MUST be replaced with the per-die `controllable !== false` filter so the visual cue matches the click logic exactly.

#### Scenario: Highlight and click logic agree

- **WHEN** the player is targeting and an enemy die is rendered with the green `.hex-target` highlight
- **THEN** clicking that die successfully posts a `playCard` action (no rejection flash)

#### Scenario: Highlight is suppressed for non-targetable dice

- **WHEN** the player is targeting and an enemy die has `controllable === false`
- **THEN** the die does NOT receive the green `.hex-target` highlight
- **AND** clicking the die plays the rejection flash

### Requirement: Mod-side serializer SHALL mirror `StageController.IsVisibleEnemyTarget()` when emitting slotted-card target data

When `Singleton<StageController>.Instance.IsVisibleEnemyTarget()` returns `false` during the `battle` scene branch of `GameStateSerializer.BuildJson` (equivalent to any active `EnemyTeamStageManager.HideEnemyTarget()` override returning `true` — including `EnemyTeamStageManager_TheCrying` while any alive enemy holds an undestroyed `PassiveAbility_240428`), the serializer MUST suppress enemy-side targeting fields from slotted cards in the JSON payload:

- For every unit where `unit.faction == Faction.Enemy`, each entry in `slottedCards[]` MUST omit `targetUnitId`, `targetSlot`, `clash`, and `subTargets` entirely, even when `slot.target != null`.
- For every unit where `unit.faction == Faction.Player`, each entry in `slottedCards[]` MUST continue to emit `targetUnitId`, `targetSlot`, and `subTargets` as today, but MUST force `clash: false` regardless of the computed clash check, because clash detection requires the enemy-side data that is being hidden.

When `IsVisibleEnemyTarget()` returns `true` (the steady-state default), all four fields MUST emit exactly as they do today — this change is gated on the suppression condition only.

If `Singleton<StageController>.Instance` is null or the call throws, the serializer MUST treat the gate as `true` (i.e. emit fields normally). This matches the vanilla `IsVisibleEnemyTarget` fail-open behavior and prevents unrelated runtime errors from suppressing target info outside the gated encounter.

#### Scenario: Crying Children encounter is active and an enemy holds Unhearing Child

- **WHEN** a battle state is built while an alive enemy has `PassiveAbility_240428` (passive id 240428) and it is not destroyed
- **THEN** every enemy unit's `slottedCards[]` entries are emitted without `targetUnitId`, `targetSlot`, `clash`, or `subTargets`
- **AND** every ally unit's `slottedCards[]` entries that have a target still include `targetUnitId` and `targetSlot`
- **AND** every ally unit's `slottedCards[]` entries report `clash: false` regardless of the underlying clash pair
- **AND** the frontend's `attackMap` produced from this state has no entries keyed by an ally unit id

#### Scenario: Unhearing Child holder becomes Staggered

- **WHEN** the holder of `PassiveAbility_240428` reaches stagger (`OnBreakGageZero` flips the passive's `destroyed` to `true`) and a subsequent state push is built
- **THEN** `IsVisibleEnemyTarget()` returns `true`
- **AND** the suppression rules above no longer apply
- **AND** enemy `slottedCards[]` entries again emit `targetUnitId`, `targetSlot`, `clash`, and `subTargets` exactly as in the default code path

#### Scenario: Standard encounter with no `HideEnemyTarget` override

- **WHEN** a battle state is built in any encounter where no alive enemy gates `IsVisibleEnemyTarget()` to `false`
- **THEN** both enemy and ally slotted cards emit `targetUnitId`, `targetSlot`, `clash`, and `subTargets` exactly as before this change

#### Scenario: `StageController` is unavailable

- **WHEN** the serializer is invoked but `Singleton<StageController>.Instance` is null or `IsVisibleEnemyTarget()` throws
- **THEN** the serializer behaves as if the gate is open (`true`)
- **AND** enemy and ally slotted cards emit all four target fields normally
- **AND** no exception is propagated out of `BuildJsonSafe`

### Requirement: Frontend rendering of incoming arrows and clash markers SHALL respect omitted target fields without additional gating

Existing frontend display code MUST continue to treat absent `targetUnitId` as "no incoming attack" and falsy `clash` as "no clash highlight," requiring no template or composable changes when the mod suppresses fields per the rule above. This requirement locks in the no-frontend-change posture of this change so future contributors do not introduce a redundant frontend gate.

Specifically, when an ally's slotted card has `targetUnitId` set and `clash: false`, `unit/DieRow.vue` MUST render the outgoing arrow chip with the `↗` prefix (not `⚔`) and `unit/DisplayCard.vue::dieColor` MUST select the `incoming` colour (not `clash`). The `ArrowOverlay.vue` arrow type for that slot MUST be `outgoing` (blue). `composables/useBattleContext.ts::attackMap` MUST NOT register an entry when `targetUnitId == null`.

#### Scenario: Ally slot has target but `clash: false`

- **WHEN** state arrives with an ally slotted card where `targetUnitId` is set, `targetSlot` is set, and `clash === false`
- **THEN** the ally's `DieRow` chip text begins with `↗` (not `⚔`)
- **AND** `DisplayCard.dieColor` resolves to `ARROW_COLORS.incoming`
- **AND** `ArrowOverlay` draws the arrow as `outgoing` type
- **AND** the ally die does NOT receive the `.clash` CSS class

#### Scenario: Enemy slot has no target field

- **WHEN** state arrives with an enemy slotted card where `targetUnitId` is undefined / absent
- **THEN** `attackMap` has no entry from this slot
- **AND** no incoming-attack chip appears on any ally die from this enemy slot
- **AND** `ArrowOverlay` draws no arrow originating at this enemy slot

#### Scenario: Schema accepts the gated payload

- **WHEN** a wire payload representing the Crying Children gated state is parsed by `GameStateSchema`
- **THEN** parsing succeeds without validation errors
- **AND** the parsed shape exposes ally slots with `targetUnitId`/`targetSlot` set and `clash: false`, and enemy slots without those four fields
