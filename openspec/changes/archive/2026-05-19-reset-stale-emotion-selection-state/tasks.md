## 1. State lifecycle reset (mod/StateBroadcaster.cs)

- [x] 1.1 Add `internal static void ResetEmotionSelectionState()` that nulls/false-sets `AbnormalitySelectionState` and `EgoSelectionState` (IsActive, Choices, Floor).
- [x] 1.2 Call `ResetEmotionSelectionState()` before the existing `Broadcast()` in the `Patch_ActivateBattle` postfix.
- [x] 1.3 Call `ResetEmotionSelectionState()` before the existing `Broadcast()` in the `Patch_ActivateTitle` postfix.
- [x] 1.4 Call `ResetEmotionSelectionState()` before the existing `Broadcast()` in the `Patch_ActivateUI` (`ActivateUIController`) postfix.

## 2. Action authorization hardening (mod/ActionInjector.cs)

- [x] 2.1 In `DoSelectAbnormality`, after fetching `floor` from `AbnormalitySelectionState`, reject with an error unless `floor` is reference-equal to `Singleton<StageController>.Instance?.GetCurrentStageFloorModel()`.
- [x] 2.2 In `DoSelectEgo`, apply the same live-floor equality check against `EgoSelectionState.Floor` before calling `OnPickEgoCard`.

## 3. Validation

- [x] 3.1 Run `dotnet build` from `mod/` and confirm `0 Warning(s) 0 Error(s)`.
- [x] 3.2 Manual in-game check: open an emotion-card selection, exit to title without picking, start a new battle, confirm the picker does not reappear and a replayed `selectAbnormality`/`selectEgo` for the prior floor is rejected.
