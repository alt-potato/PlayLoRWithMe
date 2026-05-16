## ADDED Requirements

### Requirement: Mod-side selectAbnormality SHALL restore in-game audio to the same state the base game leaves behind after an in-game pick

When `ActionInjector.DoSelectAbnormality` successfully commits a player's abnormality choice (whether via `OnSelectPassive(picked_ui)` for `All`/`AllIncludingEnemy` cards or via a direct `StageLibraryFloorModel.OnPickPassiveCard(card, target)` call for `SelectOne` cards), the mod SHALL ensure that the `LevelUpUI`'s looping selection-ambience sample (`EffectSoundType.ABNORMALITY_SELECT_LOOP`, played into `LevelUpUI._loopSound` by `LevelUpUI.Init`) is released, and that `BattleSoundManager`'s BGM volume ratio is restored to `1.0` (undoing the `0.5` duck `LevelUpUI.Init` applied when the menu opened). The end-state SHALL be identical to the audio state observed after an in-game card click that runs `LevelUpUI.OnSelectPassive`.

The mod MUST NOT rely on Unity's `OnDisable` firing automatically from a canvas-only disable (`SetRootCanvas(false)`), because that path does not deactivate the GameObject. The mod MUST NOT leave `LevelUpUI` in a state-transition (e.g. via `OnSelectHide`, which swaps the card-selection panel for the "please select a librarian" prompt without disabling the canvas).

#### Scenario: Looping ambience stops after a remote pick

- **WHEN** a connected client sends a `selectAbnormality` action and the mod calls `floor.OnPickPassiveCard(...)` successfully
- **THEN** within the same Unity main-thread tick the `LevelUpUI._loopSound` reference is released (no further audio frames from `ABNORMALITY_SELECT_LOOP` reach the mixer)
- **AND** the in-game audio state is indistinguishable from the state after an in-game card click on the same card

#### Scenario: BGM volume restores to full after a remote pick

- **WHEN** a `selectAbnormality` action commits
- **THEN** `BattleSoundManager.SetBgmVolumeRatio` has been called with `1f` (or an equivalent state-restore path has run)
- **AND** subsequent BGM playback is not ducked

### Requirement: Mod-side selectAbnormality SHALL still allow `RoundEndPhase_ChoiceEmotionCard` to re-queue subsequent emotion-level choices

The dismissal mechanism the mod uses after a successful `selectAbnormality` SHALL result in `LevelUpUI`'s root canvas being disabled by the time `RoundEndPhase_ChoiceEmotionCard` next polls UI state. This preserves the multi-selection flow used when a single act raises the team emotion level by more than one (multiple consecutive picks).

The dismissal MAY be animated (matching the in-game `All`-target `DisableRoutine` path, ~0.5s) when routing through `OnSelectPassive`, or instantaneous (matching the in-game `SelectOne` `OnClickTargetUnit` path) when calling `SetRootCanvas(false)` directly. Either is acceptable provided the canvas is reliably disabled at the end of the path.

The mod MUST NOT leave a `LevelUpUI` coroutine in a state where it would later flash per-ally `abCardSelector` overlays after the canvas has been dismissed. Specifically, the mod MUST NOT invoke `OnSelectPassive` on a `SelectOne` card without also suppressing the subsequent `OnSelectRoutine`-driven `abCardSelector.Init` calls.

#### Scenario: Multi-level emotion gain produces a follow-up selection prompt

- **WHEN** an act raises the team emotion level by more than one and the mod commits the first abnormality pick via `selectAbnormality`
- **THEN** `RoundEndPhase_ChoiceEmotionCard` re-opens `LevelUpUI` with the next level's choices
- **AND** the first level's loop sound does not bleed over into the second level (any new ambience starts from a clean state)
