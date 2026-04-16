## ADDED Requirements

### Requirement: Gift data serialized per librarian

The game state SHALL include a `gifts` object on each librarian entry containing the equipped gift for each of the 9 `GiftPosition` slots and the list of available (unlocked, unequipped) gifts per position. Each gift entry SHALL include `id`, `name`, `desc`, `position` (enum string), `stat` (object with `hp`, `break`, `breakRecover`, `haste`, `damage` fields), and `visible` (boolean). Gifts with `NoAppear` set to true in the game data SHALL be excluded.

#### Scenario: Librarian with equipped gifts
- **WHEN** a librarian has gifts equipped in the Eye and Hood positions
- **THEN** the `gifts.equipped` array contains entries for those positions with correct id, name, desc, stat, and visible values, and `null` for unoccupied positions

#### Scenario: Librarian with no gifts
- **WHEN** a librarian has no gifts equipped or unlocked
- **THEN** the `gifts.equipped` array contains 9 `null` entries and `gifts.available` is an empty array

#### Scenario: Available gifts listed per librarian
- **WHEN** a librarian has unlocked gifts that are not currently equipped
- **THEN** those gifts appear in `gifts.available` with their position, id, name, desc, and stat

---

### Requirement: setGifts WebSocket action

The mod SHALL handle a `setGifts` WebSocket action that equips, unequips, and toggles visibility of gifts on a librarian. The action SHALL require the session to hold the librarian lock (same authorization as `setCustomization`). The payload SHALL contain `floorIndex`, `unitIndex`, and a `slots` array where each entry specifies `position` (0–8), `giftId` (int, -1 to unequip), and optionally `visible` (boolean). Omitted positions SHALL remain unchanged. After applying changes, the mod SHALL refresh the in-game character appearance and broadcast updated state.

#### Scenario: Equip a gift
- **WHEN** a `setGifts` action is received with `giftId` matching an unlocked gift for the librarian at the specified position
- **THEN** the gift is equipped in that position, any previously equipped gift in that position is moved to unequipped, and the character appearance updates in-game

#### Scenario: Unequip a gift
- **WHEN** a `setGifts` action is received with `giftId: -1` for a position
- **THEN** the currently equipped gift in that position is moved to unequipped

#### Scenario: Toggle visibility
- **WHEN** a `setGifts` action is received with a `visible` field for an equipped gift
- **THEN** the gift's `isShowEquipGift` is updated and the character appearance refreshes

#### Scenario: Unauthorized session
- **WHEN** a `setGifts` action is received from a session that does not hold the librarian lock
- **THEN** the action is rejected and no changes are made

---

### Requirement: Gift preview sprites extracted at runtime

The mod SHALL extract gift preview sprites from the game's `GiftAppearance` prefabs and save them as PNG files at `wwwroot/assets/gifts/gift_{id}.png`. Extraction SHALL occur at startup (same hook as `AppearanceCache`), skipping files that already exist. Gifts of type `GiftAppearance_Aura` SHALL use a generic aura icon. Failed extractions SHALL be logged and skipped.

#### Scenario: Gift sprite extracted
- **WHEN** the mod starts and a gift prefab at `Prefabs/Gifts/Gifts_NeedRename/Gift_{resource}` loads successfully
- **THEN** the front sprite is rendered to a PNG at `wwwroot/assets/gifts/gift_{id}.png`

#### Scenario: Already extracted sprite skipped
- **WHEN** a gift PNG file already exists on disk
- **THEN** extraction is skipped for that gift

---

### Requirement: BattleSymbolsTab in CustomizePanel

The CustomizePanel SHALL include a "Symbols" sub-tab that displays a 3×3 grid of the 9 gift positions. Each cell SHALL show the position label and, if a gift is equipped, the gift name and a visibility toggle (eye icon). Clicking an occupied cell SHALL allow unequipping; clicking any cell SHALL open a selection list of available gifts for that position below the grid. The selection list SHALL show each gift's name and stat summary. A stat summary of all equipped gifts SHALL be displayed, showing only non-zero bonuses.

#### Scenario: Empty slot display
- **WHEN** no gift is equipped in a position
- **THEN** the cell shows the position label and an "empty" indicator

#### Scenario: Equipped slot display
- **WHEN** a gift is equipped in a position
- **THEN** the cell shows the gift name, and a visibility toggle icon

#### Scenario: Selecting a gift
- **WHEN** the user clicks a grid cell and then selects a gift from the available list
- **THEN** the draft is updated with the new gift equipped in that position

#### Scenario: Unequipping a gift
- **WHEN** the user clicks "Unequip" on an occupied cell
- **THEN** the draft is updated with that position cleared

#### Scenario: Toggling visibility
- **WHEN** the user clicks the visibility toggle on an equipped gift
- **THEN** the draft is updated with the gift's visible state toggled

#### Scenario: Stat summary
- **WHEN** gifts are equipped with stat bonuses
- **THEN** a summary below the grid shows cumulative non-zero bonuses (e.g., "+5 HP, +3 Stagger Resist")

---

### Requirement: Gift description tooltip

Each gift in the selection list and in the equipped grid SHALL display its description text when interacted with. On desktop, hovering over a gift SHALL show the description. On mobile, the description SHALL be shown inline below the gift name in the selection list.

#### Scenario: Desktop hover description
- **WHEN** the user hovers over a gift entry on a desktop viewport
- **THEN** the gift's description text is displayed as a tooltip

#### Scenario: Mobile inline description
- **WHEN** the user views the selection list on a mobile viewport
- **THEN** each gift's description is shown inline below its name
