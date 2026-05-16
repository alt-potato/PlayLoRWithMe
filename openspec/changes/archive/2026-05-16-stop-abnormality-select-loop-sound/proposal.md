## Why

When the player clicks an abnormality card through the frontend, the in-game `LevelUpUI` plays a looping selection-ambience sound effect that never stops. The mod's `selectAbnormality` action handler dismisses the UI via `LevelUpUI.SetRootCanvas(false)`, which only toggles `_canvas.enabled` — it neither fires Unity's `OnDisable` (the GameObject stays active) nor invokes any of the game's audio-cleanup paths (`OnSelectPassive`, `OnSelectHide`). As a result the `ABNORMALITY_SELECT_LOOP` sample keeps playing and the BGM stays ducked at the 0.5× volume that `LevelUpUI.Init` set when the menu opened.

In-game, when the player clicks a card directly, `OnSelectPassive` inlines the cleanup (`BattleSoundManager.SetBgmVolumeRatio(1f)` + `_loopSound.Release()`) before the dismissal animation runs. The mod short-circuits the UI entirely (`OnPickPassiveCard(...)` then `SetRootCanvas(false)`), bypassing that cleanup.

## What Changes

- After a successful `selectAbnormality` action commits via `floor.OnPickPassiveCard(...)`, dismiss the in-game `LevelUpUI` through `OnSelectHide(force: true)` instead of `SetRootCanvas(false)`. `OnSelectHide` is the public method that already inlines the same `SetBgmVolumeRatio(1f)` + `_loopSound.Release()` sequence the base game runs, plus the hide animation that eventually disables the canvas — matching every in-game dismissal path.
- No wire-contract, frontend, or schema changes. No new action payloads. No tests change shape.

## Capabilities

### New Capabilities

- `abnormality-select-dismissal`: requirements governing what the mod must do after a `selectAbnormality` action commits — specifically, that the in-game `LevelUpUI` audio (the `ABNORMALITY_SELECT_LOOP` sample) and BGM ducking are restored to the same state the base game would leave them in after an in-game pick.

### Modified Capabilities

None. The wire contract (`wire-contract-schema`) is unchanged: the `selectAbnormality` payload shape, the `abnormalitySelection` state field, and the post-commit broadcast are all unaffected.

## Impact

- **Code**: One method body in `mod/ActionInjector.cs::DoSelectAbnormality` — replace the `SetRootCanvas(false)` call with `OnSelectHide(force: true)`. The trailing comment about `RoundEndPhase_ChoiceEmotionCard` detection remains accurate because `OnSelectHide`'s coroutine eventually disables the canvas.
- **Runtime**: A small dismissal animation now plays after a remote pick (matching the in-game All/AllIncludingEnemy target-type path). Acceptable — it's the same visual the base game shows.
- **Audio**: Loop sound stops; BGM volume restores to 1.0 — matching the in-game post-pick state.
- **No risk to multi-selection flow**: `OnSelectHide` does not interfere with `RoundEndPhase_ChoiceEmotionCard` re-queueing the next level's choices when an act raises the team emotion level by more than one — the canvas is still disabled at the end of the coroutine, just animated rather than snap-dismissed.
