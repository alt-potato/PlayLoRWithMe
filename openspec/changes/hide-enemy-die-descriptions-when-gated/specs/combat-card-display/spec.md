## ADDED Requirements

### Requirement: Mod-side serializer SHALL mirror `StageController.IsHideEnemyDiceAbilityInfo()` when emitting per-die descriptions

When `Singleton<StageController>.Instance.IsHideEnemyDiceAbilityInfo()` returns `true` during the `battle` scene branch of `GameStateSerializer.BuildJson` (equivalent to any active `EnemyTeamStageManager.IsHideDiceAbilityInfo()` override returning `true` — including `EnemyTeamStageManager_TheCrying` while any alive enemy holds an undestroyed `PassiveAbility_240328`), the serializer MUST mask the per-die description on enemy-owned slotted cards:

- For every unit where `unit.faction == Faction.Enemy`, each die entry inside every `slottedCards[].dice[]` MUST emit `desc: "???"` rather than the real description string returned by `BattleCardAbilityDescXmlList.GetAbilityDesc` / `DiceBehaviour.Desc`.
- For every unit where `unit.faction == Faction.Player`, each die entry MUST continue to emit the real `desc` value exactly as today. Vanilla only gates enemy-owned card previews.
- All other fields on the slotted card (`cardId`, `name`, `cost`, `range`, `abilityDesc`, `rarity`, `emotionLimit`, `baseCost`, `bufs`, `options`, `dice[].type`, `dice[].detail`, `dice[].min`, `dice[].max`) MUST emit unchanged in both factions.

When `IsHideEnemyDiceAbilityInfo()` returns `false` (the steady-state default), all per-die `desc` fields MUST emit exactly as they do today — this requirement is gated on the suppression condition only.

If `Singleton<StageController>.Instance` is null or the call throws, the serializer MUST treat the gate as `false` (i.e. emit real `desc` values). This matches the vanilla `try/catch` fail-open behavior inside `StageController.IsHideEnemyDiceAbilityInfo` and prevents unrelated runtime errors from masking descriptions outside the gated encounter.

#### Scenario: Crying Children encounter is active and an enemy holds Unseeing Child

- **WHEN** a battle state is built while an alive enemy has `PassiveAbility_240328` (passive id 240328) and it is not destroyed
- **THEN** every enemy unit's `slottedCards[].dice[].desc` is the literal string `"???"`
- **AND** every ally unit's `slottedCards[].dice[].desc` is the real description string from `BattleCardAbilityDescXmlList`
- **AND** every other field on every slotted card (name, cost, range, card-level `abilityDesc`, dice type/detail/min/max, bufs, options) emits exactly as it does outside the gated encounter

#### Scenario: Unseeing Child holder becomes Staggered

- **WHEN** the holder of `PassiveAbility_240328` reaches stagger (`OnBreakGageZero` flips the passive's `destroyed` to `true`) and a subsequent state push is built
- **THEN** `IsHideEnemyDiceAbilityInfo()` returns `false`
- **AND** the masking rule no longer applies
- **AND** enemy `slottedCards[].dice[].desc` again carries the real description string

#### Scenario: Standard encounter with no `IsHideDiceAbilityInfo` override

- **WHEN** a battle state is built in any encounter where no alive enemy gates `IsHideEnemyDiceAbilityInfo()` to `true`
- **THEN** both enemy and ally slotted cards emit per-die `desc` exactly as before this change

#### Scenario: `StageController` is unavailable

- **WHEN** the serializer is invoked but `Singleton<StageController>.Instance` is null or `IsHideEnemyDiceAbilityInfo()` throws
- **THEN** the serializer behaves as if the gate is open (`false`)
- **AND** enemy and ally slotted cards emit real per-die `desc` values
- **AND** no exception is propagated out of `BuildJsonSafe`

### Requirement: Frontend rendering of per-die descriptions SHALL pass the masked string through without additional gating

Existing frontend card surfaces MUST render whatever string the wire emits for `dice[].desc` without applying any per-encounter mask of their own. When the mod emits `"???"` per the rule above, every surface that renders die descriptions (notably `unit/DisplayCard.vue` detail pane, `CardDetail.vue`, `SlottedCard.vue` tooltip if any) MUST display the literal `"???"` text in place of the usual effect description. This locks in the no-frontend-change posture of this change so future contributors do not introduce a redundant frontend gate.

#### Scenario: Enemy slotted card carries `"???"` descriptions on every die

- **WHEN** state arrives with an enemy unit's slotted card where every `dice[].desc === "???"`
- **THEN** the rendered card surface shows `???` (verbatim) in the per-die description area for every die
- **AND** the card name, cost, range, card-level `abilityDesc`, and dice icons/values render normally

#### Scenario: Schema accepts the masked payload

- **WHEN** a wire payload representing the Crying Children die-description-masked state is parsed by `GameStateSchema`
- **THEN** parsing succeeds without validation errors
- **AND** the parsed shape exposes enemy slots whose every die has `desc: "???"` and ally slots whose dice carry the real description strings
