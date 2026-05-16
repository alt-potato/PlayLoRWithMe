## Context

`LevelUpUI` (the in-game emotion-level abnormality/EGO picker) plays an ambience sample when it opens:

```csharp
// LevelUpUI.Init(int count, List<EmotionCardXmlInfo> cardList)
SingletonBehavior<BattleSoundManager>.Instance.PlaySound(EffectSoundType.ABNORMALITY_SELECT_START);
_loopSound = SingletonBehavior<BattleSoundManager>.Instance.PlaySound(
    EffectSoundType.ABNORMALITY_SELECT_LOOP, loop: true);
SingletonBehavior<BattleSoundManager>.Instance.SetBgmVolumeRatio(0.5f);
```

The game has three paths that release `_loopSound` and restore BGM volume:

1. `Unity OnDisable()` — fires only when the GameObject deactivates. Just releases the loop sound.
2. `OnSelectPassive(EmotionPassiveCardUI picked)` — inlines the cleanup before starting the dismissal coroutine. Triggered by an in-game card click.
3. `OnSelectHide(bool force)` — same cleanup as path 2, plus starts `HideRoutine` and `TranslateRoutine`. Public method.

The mod's `ActionInjector.DoSelectAbnormality` short-circuits the UI: it calls `floor.OnPickPassiveCard(card, target)` directly (skipping `OnSelectPassive`) and then dismisses with `levelup.SetRootCanvas(false)`. `SetRootCanvas(false)` only flips `_canvas.enabled` — the GameObject stays active, so `OnDisable` never fires, and none of the audio-cleanup paths run. The loop sample plays forever and BGM stays ducked at 0.5×.

## Goals / Non-Goals

**Goals:**

- After a `selectAbnormality` action commits, the `ABNORMALITY_SELECT_LOOP` sample stops and BGM volume returns to 1.0 — matching the audio state the game leaves behind after an in-game card click.
- The dismissal mechanism mirrors the base game's process as closely as possible (per user direction) — no reflection into private state when a public method does the same job.
- The `RoundEndPhase_ChoiceEmotionCard` re-queueing for multi-level acts (the reason `SetRootCanvas(false)` was originally added) keeps working.

**Non-Goals:**

- No wire-contract changes, no schema regeneration, no frontend changes.
- Not changing how `OnPickPassiveCard` is invoked. The mod still bypasses `OnSelectPassive` because we already know the target from the action payload and want to skip the in-game ally-picker UI for `SelectOne` cards.
- Not addressing the EGO selection feature gap — tracked in a separate proposal.

## Decisions

### D1: Hybrid dismissal — `OnSelectPassive` for `All`/`AllIncludingEnemy`, inline cleanup for `SelectOne`

The base game has two distinct dismissal codepaths, one per `EmotionTargetType` branch in `LevelUpUI.OnSelectPassive`:

| Target type | In-game path | Mirror feasibility |
|---|---|---|
| `All` | `OnSelectPassive` → `floor.OnPickPassiveCard(card)` + inline audio cleanup + `OnSelectRoutine` → `DisableRoutine` (animated `SetRootCanvas(false)`) | Cleanly mirrorable by calling `levelup.OnSelectPassive(picked_ui)` |
| `AllIncludingEnemy` | Same as `All`, plus `wave.ApplyEmotionCard(card)` | Cleanly mirrorable by calling `levelup.OnSelectPassive(picked_ui)` |
| `SelectOne` | `OnSelectPassive` (sets `_selectedCard`, `_needUnitSelection = true`, audio cleanup, starts `OnSelectRoutine`) → 0.1s yield → per-ally `abCardSelector.Init` → user clicks ally → `OnClickTargetUnit(unit)` → `floor.OnPickPassiveCard(_selectedCard, unit)` + `SetRootCanvas(false)` | **No base-game shortcut.** The flow *requires* a per-unit `abCardSelector` click; there is no SelectOne-with-target codepath in the game. |

The mod takes the hybrid:

- **For `All` and `AllIncludingEnemy`:** locate the `EmotionPassiveCardUI` in `LevelUpUI.candidates` whose `Card.id` matches the action's `cardId`, then call `levelup.OnSelectPassive(picked_ui)`. The game commits via `OnPickPassiveCard`, runs the inline audio cleanup (`SetBgmVolumeRatio(1f)` + `_loopSound.Release()`), and dismisses the canvas through `OnSelectRoutine` → `DisableRoutine` over ~0.5s. The mod does NOT manually call `floor.OnPickPassiveCard` in this branch — `OnSelectPassive` does it. This is the truest base-game mirror available, and (bonus) it correctly invokes `wave.ApplyEmotionCard` for `AllIncludingEnemy` cards, which the previous manual path silently skipped.

- **For `SelectOne`:** the mod still calls `floor.OnPickPassiveCard(card, target)` directly — this is byte-identical to what `OnClickTargetUnit` does after the in-game ally click. It then inlines the same two-line audio cleanup that `OnSelectPassive` runs:

  ```csharp
  // SingletonBehavior<BattleSoundManager>.Instance.SetBgmVolumeRatio(1f);
  // if (_loopSound != null) { _loopSound.Release(); _loopSound = null; }
  ```

  via the public `levelup.OnDisable()` (releases `_loopSound`) plus an explicit `BattleSoundManager.SetBgmVolumeRatio(1f)`. Then `SetRootCanvas(false)` snap-dismisses the canvas — the same call `OnClickTargetUnit` makes after its commit.

**Why not route SelectOne through `OnSelectPassive` too?**

Calling `OnSelectPassive(picked_ui)` for `SelectOne` sets `_needUnitSelection = true` and starts `OnSelectRoutine`. After a 0.1s yield, the coroutine initializes per-ally `abCardSelector` overlays — which would flash on screen *after* the pick has already committed via a subsequent `OnClickTargetUnit(target)` call, because Unity coroutines do not stop when a canvas is disabled. Suppressing them requires either reflecting on the private `_needUnitSelection` field before the yield resumes or `StopAllCoroutines()` on `LevelUpUI` (which also cancels `BlinkRoutine` and any in-flight `TranslateRoutine`). The reflection workaround is what the user explicitly preferred to avoid — and the inline cleanup is functionally identical to what `OnSelectPassive` runs anyway, so there is no behavioral cost to skipping it for this branch.

**Why `OnDisable()` and not reflection?**

`LevelUpUI.OnDisable()` is a public method whose entire body is the `_loopSound`-release block we need:

```csharp
public void OnDisable() {
    if (_loopSound != null) { _loopSound.Release(); _loopSound = null; }
}
```

Calling it directly does exactly the work the base game's `OnSelectPassive` inlines for the loop sound, with no reflection. Unity may also invoke `OnDisable` later as a lifecycle callback (e.g. on scene change); a second call is a no-op because `_loopSound` is already null.

**Alternatives considered:**

- **`OnSelectHide(force: true)` for all branches.** Initial choice. Rejected after testing revealed `HideRoutine` is a *state transition* (card panel → "please select a librarian" prompt), not a dismissal — it never disables the canvas. Calling it after a remote pick stranded the user on the librarian-selection prompt with no `abCardSelector` overlays to click, because we never went through `OnSelectPassive` to initialize them.
- **`OnSelectPassive` uniformly + reflection on `_needUnitSelection`.** Single code path but requires reflection on private state and has a tight coroutine race window.
- **`gameObject.SetActive(false)`.** Triggers Unity `OnDisable` (releases loop sound) but leaves BGM ducked at 0.5×. Half-fix.

### D2: Keep the direct `OnPickPassiveCard` call for `SelectOne`

For the `SelectOne` branch, the mod continues to call `floor.OnPickPassiveCard(card, target)` directly — this is byte-identical to the call `OnClickTargetUnit` makes after an in-game ally click. The only difference from the previous implementation is the addition of the inline audio cleanup before the canvas dismissal.

For `All`/`AllIncludingEnemy`, the direct `OnPickPassiveCard` call is *removed* — `OnSelectPassive(picked_ui)` is invoked instead, which subsumes it.

## Risks / Trade-offs

- **Dismissal animation now plays.** Previously the canvas snap-disappeared; now there is a brief hide animation. This is the same animation the base game shows for `All`-target abnormality cards, so it is consistent with in-game behavior. → Accepted, no mitigation needed.
- **`HideRoutine` could in theory leave the canvas enabled if the coroutine is interrupted (e.g., scene change mid-animation).** Library of Ruina's level-up flow happens during `RoundEndPhase`, which does not change scenes; the coroutine reliably finishes. → No mitigation; if this surfaces, fall back to reflection-release + `SetRootCanvas(false)`.
- **`HideRoutine` is private and not decompiled in this design doc.** We rely on it eventually disabling the canvas based on the documented `RoundEndPhase_ChoiceEmotionCard` re-queue working in current builds for the All-target case. The apply-phase verification step is a live-game test that confirms post-pick BGM/sound state and multi-level re-queueing.

## Migration Plan

Single-commit code change in `mod/ActionInjector.cs`. No data migration. No rollback complexity — revert is a single-line restore.

## Open Questions

None. The behavior is fully specified by the existing in-game `OnSelectPassive` cleanup; we are aligning the mod with it.
