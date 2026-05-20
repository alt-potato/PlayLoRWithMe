## ADDED Requirements

### Requirement: Emotion-card selection state SHALL be cleared on scene transitions that leave or re-enter a battle

The mod's emotion-card selection state for both the abnormality branch (`AbnormalitySelectionState`) and the EGO branch (`EgoSelectionState`) SHALL be cleared (active flag false, choices and floor references released) on each scene transition that leaves or re-enters a battle: battle-scene activation, title-scene activation, and main/library (`UIController`) scene activation. The clear SHALL occur before the broadcast those transitions already emit, so the resulting state snapshot omits the `abnormalitySelection` / `egoSelection` fields. The clear MUST NOT depend on the player having committed a pick.

#### Scenario: Exit to title without picking clears the selection

- **WHEN** an emotion-card selection is open (its active flag is set) and the player returns to the title scene without picking a card
- **THEN** both `AbnormalitySelectionState` and `EgoSelectionState` are cleared
- **AND** the broadcast emitted on title-scene activation does not include `abnormalitySelection` or `egoSelection`

#### Scenario: Next reception starts without a stale picker

- **WHEN** a selection was abandoned without a pick and a new battle scene is subsequently activated
- **THEN** the selection state has been cleared by the time the new battle broadcasts state
- **AND** the new reception's frontend does not show the previous reception's emotion-card picker

#### Scenario: Returning to the library without picking clears the selection

- **WHEN** an emotion-card selection is open and the main/library (`UIController`) scene is activated
- **THEN** both selection states are cleared before that scene's broadcast

### Requirement: A selectAbnormality or selectEgo action SHALL only be honored against the live current floor

When the mod handles a `selectAbnormality` or `selectEgo` action, it SHALL reject the action unless the floor model stored in the selection state is reference-equal to the live current floor (`StageController.GetCurrentStageFloorModel()`). A pick whose stored floor does not match the live floor — including a stale or replayed action referencing a previous reception's floor — SHALL fail with an error and SHALL NOT call `OnPickPassiveCard` / `OnPickEgoCard`.

#### Scenario: Stale pick from a previous reception is rejected

- **WHEN** a client sends `selectAbnormality` or `selectEgo` whose stored floor model is not the live current floor (e.g. a previous reception's floor)
- **THEN** the action is rejected with an error
- **AND** no abnormality or EGO page is applied to any floor

#### Scenario: Valid in-reception pick is honored

- **WHEN** a client sends `selectAbnormality` or `selectEgo` while a selection is genuinely open in the current battle and the stored floor matches the live current floor
- **THEN** the action commits normally via `OnPickPassiveCard` / `OnPickEgoCard`
