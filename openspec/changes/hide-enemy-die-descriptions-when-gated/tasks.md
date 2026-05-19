## 1. Thread the die-mask gate through the serializer

- [x] 1.1 In `mod/GameStateSerializer.cs::WriteBattleScene`, probe `Singleton<StageController>.Instance.IsHideEnemyDiceAbilityInfo()` once (with `try/catch` fail-open mirroring the existing `IsVisibleEnemyTarget` probe) and store the result as a local boolean `dieDescriptionsHidden`.
- [x] 1.2 Extend `WriteUnit` to accept `bool dieDescriptionsHidden = false` and forward it to `WriteSlottedCards`.
- [x] 1.3 Extend `WriteSlottedCards` to accept `bool dieDescriptionsHidden`. Compute `bool maskDie = dieDescriptionsHidden && !isAlly` and pass it to `WriteCardFields`.
- [x] 1.4 Extend `WriteCardFields` to accept `bool maskDie = false` and forward it to `WriteDiceBehaviours`.
- [x] 1.5 Extend `WriteDiceBehaviours` to accept `bool maskDie = false`; when true, emit the literal string `"???"` for each die's `desc` instead of looking up the real description.
- [x] 1.6 Build the mod from `mod/`: `dotnet build` should report `0 Warning(s) 0 Error(s)`.

## 2. Schema fixture coverage

- [x] 2.1 Add a `battle_hiddenEnemyDieDescriptions` case to `frontend/schema/reference-state.json` with at least one enemy slotted card whose every die has `desc: "???"` and an ally slotted card whose dice carry real descriptions.
- [x] 2.2 Run `npm test` from `frontend/`; the schema round-trip test SHALL pass on the new fixture.

## 3. Dev-mode fixture

- [x] 3.1 Add a corresponding `frontend/app/dev/fixtures/battle-hidden-enemy-die-descriptions.json` mirroring the schema fixture so the new gated wire shape is observable in the dev fixture loader.
- [x] 3.2 Register the fixture in `frontend/app/dev/fixtures/index.ts` under `FIXTURE_LOADERS`.

## 4. Verification

- [x] 4.1 Re-run `dotnet build` from `mod/` after schema regen to confirm full pipeline is clean.
- [x] 4.2 User verifies in-game that, during the Crying Children encounter, every enemy slotted card displays `???` in every per-die description while ally cards still show real text. When the Unseeing Child holder is staggered, descriptions return to normal.
- [x] 4.3 User approves the change for commit and archive.
