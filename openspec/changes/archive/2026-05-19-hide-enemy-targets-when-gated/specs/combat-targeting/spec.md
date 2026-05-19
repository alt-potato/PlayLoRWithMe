## ADDED Requirements

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
