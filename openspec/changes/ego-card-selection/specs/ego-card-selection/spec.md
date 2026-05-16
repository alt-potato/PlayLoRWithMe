## ADDED Requirements

### Requirement: The mod SHALL surface every `LevelUpUI.InitEgo` open event as an `egoSelection` payload on the next state snapshot

The mod SHALL apply a Harmony patch to `LevelUpUI.InitEgo(int count, List<EmotionEgoXmlInfo> egoList)` such that, on entry, it captures the current `egoList` and the active `StageLibraryFloorModel`, marks an `EgoSelectionState` as active, and broadcasts a fresh state snapshot. The mod SHALL apply a Harmony patch to `StageLibraryFloorModel.OnPickEgoCard` such that, on exit (whether triggered by the in-game UI or by a `selectEgo` action from a remote client), it clears the state and broadcasts again.

The `egoSelection` snapshot field SHALL be omitted when no EGO selection is active.

#### Scenario: Game opens the EGO selection menu

- **WHEN** an act unlocks EGO selection on a floor and the in-game `RoundEndPhase_ChoiceEmotionCard` calls `LevelUpUI.InitEgo(...)`
- **THEN** the next broadcast `GameState` has a populated `egoSelection` field
- **AND** `egoSelection.choices` contains one entry per element in the `egoList` argument

#### Scenario: Player commits an EGO pick in-game

- **WHEN** the host clicks an EGO card via the in-game `LevelUpUI`, causing `OnPickEgoCard` to fire
- **THEN** the next broadcast `GameState` has no `egoSelection` field

#### Scenario: Remote client commits an EGO pick via the wire

- **WHEN** a connected client sends `{ "type": "selectEgo", "choiceId": <id> }` with a valid choice id matching one of the active selection's entries
- **THEN** the mod calls `StageLibraryFloorModel.OnPickEgoCard(matched_xmlInfo)` on the Unity main thread
- **AND** the next broadcast `GameState` has no `egoSelection` field
- **AND** the in-game `LevelUpUI` is dismissed using the same `OnSelectHide(force: true)` path used for `selectAbnormality`

### Requirement: The `egoSelection` payload SHALL carry enough metadata for the frontend to render an informed pick

Each entry in `egoSelection.choices` SHALL include:

- `id: number` — the `EmotionEgoXmlInfo.id`, used as the `choiceId` of the `selectEgo` action.
- `cardId: EntryId` — the resolved `LorId` of the EGO card (`{ id: number, packageId: number }`).
- `name: string` — the card's display name.
- `cost: number` — the EGO card's light cost.
- `range: CardRange` — one of `Near | Far | FarArea | FarAreaEach | Special | Instance`.
- `rarity: string` — the EGO rarity string (`ZAYIN | TETH | HE | WAW | ALEPH`).
- `sephirah: string` — the `EmotionEgoXmlInfo.Sephirah` value as a string.
- `dice: DieFace[]` — the EGO card's dice array, same shape as `BattleDiceCardSchema.dice`.
- `desc?: string` — the ability description, omitted when empty or unresolved.

The payload SHALL also include the team-emotion-state header fields when the floor model is available: `teamEmotionLevel`, `teamCoin`, `teamCoinMax`, `teamPositiveCoins`, `teamNegativeCoins` — same set the existing `abnormalitySelection` exposes.

#### Scenario: A choice resolves to a battle-card with dice and cost

- **WHEN** the mod serializes an `EmotionEgoXmlInfo` whose `_CardId` resolves to a `BattleDiceCardModel` with non-empty `dice` and a positive `cost`
- **THEN** the corresponding `EgoChoice` payload has `dice` length matching the model and `cost` matching `BattleDiceCardModel.GetCost()`
- **AND** `range` matches `BattleDiceCardModel.GetSpec().Ranges` (the resolved `CardRange` enum)

#### Scenario: A choice resolves to a battle-card with no recognized ability description

- **WHEN** the resolved card's ability-description lookup returns `null`, empty string, or `"Not found"`
- **THEN** the choice payload omits the `desc` field entirely (the field is optional in the schema)

### Requirement: The frontend SHALL render the `egoSelection` overlay through the existing emotion-upgrade picker chrome

When `gameState.egoSelection` is populated, `frontend/app/components/battle/Stage.vue` SHALL mount the emotion-upgrade picker overlay using the same backdrop, panel, header, and team-emotion bar it uses for `abnormalitySelection`. The picker MAY be a single mode-dispatching component or a sibling component, but the chrome SHALL be visually consistent across both modes (same backdrop opacity, same panel dimensions, same header layout).

The card grid inside the panel SHALL render one tile per `egoSelection.choices` entry, surfacing at minimum: name, cost, range, rarity (with floor-color affordance), and dice faces. Clicking a tile SHALL emit a selection that triggers `BATTLE_CTX.onSelectEgo(choiceId)`, which dispatches `{ type: "selectEgo", choiceId }` over the WebSocket.

The EGO mode SHALL NOT prompt for an ally target — `OnPickEgoCard` takes no target argument, so tapping a tile commits immediately.

#### Scenario: Both abnormality and ego selection fields are present (defensive)

- **WHEN** a snapshot somehow arrives with both `abnormalitySelection` and `egoSelection` populated (unexpected per the in-game state machine)
- **THEN** the frontend renders only one picker at a time (renderer SHALL choose deterministically — abnormality takes precedence to match the in-game `StartPickEmotionCard` order)
- **AND** no overlapping double-overlay is shown

#### Scenario: Tapping an EGO tile commits immediately

- **WHEN** the player taps a tile in the EGO mode picker
- **THEN** the frontend dispatches `{ "type": "selectEgo", "choiceId": <tile.id> }`
- **AND** the picker does NOT enter an ally-target step

#### Scenario: Ownership gating

- **WHEN** session claims are enabled and the connected client has not claimed any units on the active floor
- **THEN** the frontend MAY still display the picker as a read-only view (so unclaimed players see what their teammates are choosing) but the click handler MUST be a no-op
- **AND** the action MUST NOT be dispatched

### Requirement: A dev mock fixture SHALL allow mounting the EGO picker without a live mod connection

A dev-mode fixture under `frontend/app/dev/fixtures/` SHALL allow the dev page to mount the picker in EGO mode without a live mod connection. It MAY be a sibling of the existing `emotion-upgrade.json` fixture or a new `ego-upgrade.json`.

#### Scenario: Dev fixture mounts the picker

- **WHEN** the dev mock backend is loaded with the EGO fixture selected
- **THEN** `EmotionUpgradePicker` (or the EGO sibling component) mounts visibly
- **AND** the team-emotion header renders as it does in abnormality mode
- **AND** the card grid renders one tile per choice with the metadata listed in the payload requirement
