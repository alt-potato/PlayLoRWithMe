## Why

The mod's emotion-card selection state (`AbnormalitySelectionState`, `EgoSelectionState`) is set active when `LevelUpUI.Init`/`InitEgo` opens and cleared only when a card is actually picked. If a player exits to title (or otherwise leaves a battle) without picking, the state is never cleared: it persists across the reception boundary. This leaks a stale picker onto the frontend at the start of the next reception, and — more seriously — lets a client commit an abnormality/EGO page belonging to a previous reception, including via a malicious client replaying the action.

## What Changes

- Clear both `AbnormalitySelectionState` and `EgoSelectionState` on the scene transitions that leave or re-enter a battle (battle-scene activation, title-scene activation, main/library scene activation), so a selection abandoned without a pick cannot survive into another scene or reception.
- Reject `selectAbnormality` and `selectEgo` actions whose stored floor model no longer matches the live current floor, so a stale or replayed pick from a previous reception fails even if its active flag is momentarily set (defense in depth against a malicious client).
- No wire-schema change: this is a presence-only behavior change (the `abnormalitySelection`/`egoSelection` fields simply stop being emitted once the state is cleared). No frontend code change — the frontend already clears the picker when the field is absent.

## Capabilities

### New Capabilities

- `emotion-selection-lifecycle`: Defines when the mod considers an emotion-card selection (abnormality or EGO) active and valid — specifically that the active state is bounded to the battle it was opened in, is cleared on scene transitions that leave or re-enter battle, and that a commit action is only honored against the live current floor.

### Modified Capabilities

<!-- None. The existing `abnormality-select-dismissal` and `ego-card-selection` specs cover the successful-pick cleanup path; their requirements are unchanged. This change adds the abandoned-path and authorization requirements those specs do not cover. -->

## Impact

- `mod/StateBroadcaster.cs`: new `ResetEmotionSelectionState` helper; called from the `ActivateBattleScene`, `ActivateTitleScene`, and `ActivateUIController` postfixes before their existing `Broadcast()`.
- `mod/ActionInjector.cs`: `DoSelectAbnormality` and `DoSelectEgo` gain a live-floor equality check against `Singleton<StageController>.Instance?.GetCurrentStageFloorModel()`.
- No frontend, wire-schema, or `GameStateSerializer` changes.
- Validation: `dotnet build` from `mod/` (0 warnings / 0 errors); no C# test harness exists, so the exit-to-title-then-new-battle flow is verified manually in-game, consistent with prior sibling fixes.
