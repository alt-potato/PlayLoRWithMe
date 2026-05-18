## ADDED Requirements

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
