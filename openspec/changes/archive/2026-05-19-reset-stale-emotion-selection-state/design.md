## Context

The mod surfaces Library of Ruina's emotion-card level-up choices to remote clients. `StateBroadcaster.cs` tracks two process-lifetime statics, `AbnormalitySelectionState` and `EgoSelectionState`, each holding `IsActive`, the `Choices` list, and the owning `StageLibraryFloorModel` (`Floor`). They are populated in the `LevelUpUI.Init` / `LevelUpUI.InitEgo` Harmony prefixes and cleared in the `StageLibraryFloorModel.OnPickPassiveCard` / `OnPickEgoCard` postfixes. `GameStateSerializer` emits the `abnormalitySelection` / `egoSelection` wire fields whenever `IsActive && Choices != null`. `ActionInjector.DoSelectAbnormality` / `DoSelectEgo` commit a remote pick by calling `floor.OnPickPassiveCard(...)` / `floor.OnPickEgoCard(...)` on the stored `Floor`.

The clear path only runs on a successful pick. No scene transition resets the statics, so abandoning a selection (exit to title without picking) leaves `IsActive == true` with a `Floor` reference to the now-abandoned reception.

## Goals / Non-Goals

**Goals:**

- A selection abandoned without a pick must not persist onto the frontend into the next reception.
- Committing an emotion-card pick that belongs to a previous reception must be impossible, including from a malicious client that replays a `selectAbnormality` / `selectEgo` message.
- Reuse the existing, already-proven clear-and-broadcast mechanism (the same path `OnPickPassiveCard` uses) so no new frontend or wire behavior is introduced.

**Non-Goals:**

- No change to the successful-pick audio/canvas cleanup contract (`abnormality-select-dismissal`) or the EGO selection contract (`ego-card-selection`).
- No wire-schema change and no frontend code change.
- No new automated C# test (the project has no C# test harness; validation is build + manual in-game).

## Decisions

**Decision 1 — Reset on scene transitions, not on a battle-end hook.**
Clear both statics in the existing `GameSceneManager` scene-activation postfixes: `ActivateBattleScene` (so every reception begins clean regardless of how the prior one ended), `ActivateTitleScene`, and `ActivateUIController` (the library/management scene reached on a normal battle exit). These postfixes already exist and already call `Broadcast()`; the reset runs immediately before that `Broadcast()` so the resulting snapshot omits the stale field and the frontend clears via the same field-removal/delta path used after a normal pick.
*Alternative considered:* patching a `StageController` battle-end method. Rejected — battle-end has several exit paths (win, retreat, quit-to-title) and an abrupt quit may not run them, whereas scene activation is the guaranteed chokepoint. `LevelUpUI.Init` only runs mid-battle, well after `ActivateBattleScene`, so resetting at battle-scene activation never clobbers a legitimately-open selection.

**Decision 2 — A single shared reset helper.**
Add `internal static void ResetEmotionSelectionState()` in `StateBroadcaster` that nulls both `AbnormalitySelectionState` and `EgoSelectionState`. Both branches share the same lifecycle defect and the same fix, so one helper keeps the three call sites consistent.

**Decision 3 — Live-floor authorization check in the action handlers (defense in depth).**
In `DoSelectAbnormality` and `DoSelectEgo`, after fetching the stored `Floor`, reject the action unless it is reference-equal to `Singleton<StageController>.Instance?.GetCurrentStageFloorModel()`. `StageLibraryFloorModel` is a plain model, so `!=` is reference equality; outside a battle the live floor is null and any stored floor fails the check. This closes the window where a stale `IsActive` could otherwise be acted upon (e.g. a race between a replayed message and the scene reset), independent of Decision 1.
*Alternative considered:* relying solely on Decision 1. Rejected — the user explicitly requires that applying a previous reception's page be impossible "in any case," which warrants validation at the action boundary as well as state cleanup.

## Risks / Trade-offs

- [Resetting on `ActivateUIController` could clear a legitimately-open selection] → The library/management scene (`UIController`) is distinct from the battle scene; an emotion-card selection is only ever open during a battle round, so this scene never fires while a valid selection is live. No mitigation needed beyond confirming scene separation, which the existing scene-aware serializer already relies on.
- [`GetCurrentStageFloorModel()` semantics outside battle] → Returns null when no stage is active, which correctly fails the equality check rather than throwing. The handler already null-guards `Floor`; the new check is an additional `!=` comparison.

## Migration Plan

Pure additive behavior fix; no migration. Deploy by rebuilding the mod. Rollback is reverting the two source edits.

## Open Questions

None.
