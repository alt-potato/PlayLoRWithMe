## Why

When a floor unlocks EGO pages, the in-game `RoundEndPhase_ChoiceEmotionCard` opens `LevelUpUI` via `InitEgo(int count, List<EmotionEgoXmlInfo> egoList)` — a sibling entry point to the abnormality-selection `Init` the mod already surfaces. The EGO branch never reaches the frontend: the mod has no Harmony patches on `LevelUpUI.InitEgo` or `StageLibraryFloorModel.OnPickEgoCard`, no wire payload for EGO choices, no action handler, and `EmotionUpgradePicker` has no rendering path for them. Remote players see nothing while the host's `LevelUpUI` waits indefinitely for input.

The EGO flow is similar enough to the abnormality flow that the existing surface area (full-viewport picker overlay, team-emotion header, dismissal cleanup) can be reused, but the choice payload differs materially: EGO selections are battle-card data (cost, dice, range, rarity, Sephirah affiliation), not abnormality data (`targetType`, `state`, ability-desc XML), and there is no target step — `OnPickEgoCard(EmotionEgoXmlInfo egoCard)` takes no `BattleUnitModel` argument.

## What Changes

- **Mod**: New Harmony patches on `LevelUpUI.InitEgo` (open) and `StageLibraryFloorModel.OnPickEgoCard` (commit), modelled on the existing abnormality patches. New `EgoSelectionState` container alongside `AbnormalitySelectionState`. New `selectEgo` action handler in `ActionInjector`.
- **Wire**: New optional `egoSelection` field on `GameStateSchema`. The choice shape carries battle-card metadata (id, name, cost, range, rarity, Sephirah, ability description, dice faces) plus the team emotion header that abnormality selection already exposes. New `selectEgo` action payload (`{ type: "selectEgo", choiceId: number }`).
- **Frontend**: Extend `EmotionUpgradePicker.vue` (or sibling `EgoUpgradePicker.vue` — design decision) to render the EGO branch, dispatched on whichever selection field is populated. Action wiring through `useBattleActions.onSelectEgo(choiceId)` and `BATTLE_CTX.onSelectEgo`.
- **Reference fixture**: Extend `schema/reference-state.json` with a `battle_egoSelection` case so the wire-contract drift test covers the new field. Extend `frontend/app/dev/fixtures/` with an EGO-mode preset so the dev mock backend can mount the picker in isolation.

No removals. No breaking changes — the abnormality flow is untouched.

## Capabilities

### New Capabilities

- `ego-card-selection`: covers the end-to-end EGO-page emotion-level pick flow: the mod-side Harmony hooks that surface `LevelUpUI.InitEgo` state, the wire payload shape (`egoSelection`), the frontend overlay rendering, the `selectEgo` action handler, and the dismissal cleanup behavior (delegated to the contract already defined in `abnormality-select-dismissal` once that proposal is merged).

### Modified Capabilities

- `wire-contract-schema`: the reference-fixture requirement currently enumerates the scene branches the UI can enter; the EGO selection is a new branch that needs a corresponding `schema/reference-state.json` case so the drift test exercises the new wire shape.

## Impact

- **Code**:
  - `mod/StateBroadcaster.cs` — two new Harmony patches, one new state class.
  - `mod/GameStateSerializer.cs` — new `WriteEgoSelection` block, ~40 lines, modelled on the existing `WriteAbnormalitySelection` block.
  - `mod/ActionInjector.cs` — new `selectEgo` case in the action dispatcher, ~20 lines.
  - `frontend/app/types/game.ts` — new `EgoChoiceSchema`, `EgoSelectionSchema`, new optional `egoSelection` field, new `selectEgo` `ClientAction` variant.
  - `frontend/app/components/EmotionUpgradePicker.vue` — accept either selection field, dispatch the appropriate card grid. Or a sibling component if the divergence justifies it (decided in `design.md`).
  - `frontend/app/components/battle/Stage.vue` — mount the picker when `egoSelection` is populated.
  - `frontend/app/composables/useBattleContext.ts`, `useBattleActions.ts` — add `onSelectEgo` to `BATTLE_CTX`.
  - `schema/reference-state.json` — new `battle_egoSelection` case.
  - `frontend/app/dev/fixtures/` — new fixture (or extend `emotion-upgrade.json` with an EGO variant).
- **Schemas**: `schema/gamestate.schema.json` regenerates via the existing `pretest` hook.
- **Tests**: existing wire-contract drift test plus reference-fixture test pick up the new case automatically.
- **Game compatibility**: the changes are additive; nothing about the existing abnormality flow is touched.
- **Dependency on `stop-abnormality-select-loop-sound`**: not a hard dependency — the EGO flow doesn't play `ABNORMALITY_SELECT_LOOP` (only `LevelUpUI.Init` does, not `InitEgo`). The two proposals can land in either order.
