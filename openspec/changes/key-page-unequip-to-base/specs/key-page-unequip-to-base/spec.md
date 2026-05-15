## ADDED Requirements

### Requirement: The serializer SHALL emit each librarian's base (origin) key page

For every entry in `LibrarianEntrySchema`, the C# serializer SHALL emit a `baseKeyPage` field whose shape matches `KeyPageSchema`. The field SHALL be sourced from `UnitDataModel.defaultBook` and populated with the same fields the serializer already writes for `keyPage` (`instanceId`, `bookId`, optional `bookPackageId`, `name`, `speedMin`, `speedMax`, `hp`, `breakGauge`, `equipRangeType`, `rarity`, `isMultiDeck`, `resistances`).

`baseKeyPage.instanceId` SHALL be the integer `instanceId` of `defaultBook`. When the librarian is currently sitting on the base (i.e. `_bookItem` is null in the engine), `keyPage.instanceId` and `baseKeyPage.instanceId` SHALL be equal — the frontend uses this equality to detect the "on base" state and gate the Unequip affordance.

The base key page is NOT surfaced visually as a selectable tile; the field exists so the client can detect the on-base state. The frontend MAY use `baseKeyPage` for future diagnostic or fallback-stats UX, but the librarian editor's default surface SHALL NOT render the base page as a separate list entry.

#### Scenario: Librarian entry includes its base key page

- **WHEN** the serializer writes a `LibrarianEntry` for any non-empty librarian slot
- **THEN** the resulting JSON object contains a `baseKeyPage` object
- **AND** `baseKeyPage` carries the same fields as `keyPage` (name, speed range, HP, break gauge, equip range type, rarity, isMultiDeck, resistances, and identifiers)
- **AND** `baseKeyPage.instanceId` equals the engine's `unit.defaultBook.instanceId`

#### Scenario: Librarian currently on their base has matching identifiers

- **WHEN** the serializer writes a `LibrarianEntry` for a librarian whose engine `_bookItem` is null
- **THEN** `keyPage.instanceId === baseKeyPage.instanceId`
- **AND** all key-page detail fields on `keyPage` and `baseKeyPage` carry the same values

#### Scenario: Battle-unit key pages do not carry baseKeyPage

- **WHEN** the serializer writes a battle-unit `keyPage` object (combat context)
- **THEN** the surrounding battle-unit object does NOT include a `baseKeyPage` field

### Requirement: The server SHALL accept an `unequipKeyPage` action that returns the librarian to their base

The server SHALL handle a client-to-server WebSocket action of type `unequipKeyPage` carrying `floorIndex: number` and `unitIndex: number`. The handler SHALL run on the Unity main thread and SHALL require the caller to hold the librarian edit lock for the target unit (same validation as `equipKeyPage`).

On success, the handler SHALL invoke `unit.EquipBook(null)`, then verify that `unit.bookItem.instanceId === unit.defaultBook.instanceId`. If the post-condition holds, the handler SHALL refresh the character renderer (with card-inventory refresh, matching `equipKeyPage`), broadcast the new state, and reply with success. If the post-condition fails (e.g. an active change-item-lock forced a non-null book), the handler SHALL reply with failure and a human-readable error message.

If the librarian is already on their base when the action arrives, the handler SHALL treat the call as a successful no-op and reply with success without rebroadcasting.

#### Scenario: Successful unequip from an inventory key page

- **WHEN** the client sends `{ type: "unequipKeyPage", floorIndex, unitIndex }` while holding the edit lock
- **AND** the target librarian is currently on a non-base key page
- **THEN** the server invokes `unit.EquipBook(null)` on the Unity main thread
- **AND** the post-state has `unit.bookItem == unit.defaultBook`
- **AND** the server broadcasts the updated snapshot and replies `{ ok: true }`

#### Scenario: Unequip while already on base is a no-op

- **WHEN** the client sends `unequipKeyPage` for a librarian whose engine `_bookItem` is already null
- **THEN** the server replies `{ ok: true }` without broadcasting

#### Scenario: Unequip blocked by a change-item-lock

- **WHEN** the client sends `unequipKeyPage`
- **AND** the engine's `IsChangeItemLock()` path forces the book to a non-base value (e.g. Black Silence force-equip)
- **THEN** the post-condition `unit.bookItem == unit.defaultBook` fails
- **AND** the server replies `{ ok: false, error: <message> }` without leaving the state inconsistent

#### Scenario: Unequip without the edit lock is rejected

- **WHEN** the client sends `unequipKeyPage` for a librarian whose edit lock is held by another session (or by no one)
- **THEN** the server replies `{ ok: false, error: <lock-required message> }`
- **AND** no engine mutation occurs

### Requirement: The action button SHALL switch between Equip, Unequip, and hidden based on selection

The action button in `KeyPageTab.vue`'s detail panel SHALL render one of three states based on the relationship between the currently selected inventory page and the currently equipped page:

- **"Unequip"** — when the selected inventory tile IS the currently equipped page (and the librarian is therefore NOT already on their base). Pressing it dispatches `unequipKeyPage`. This is the sole entry point to the unequip flow: there is no separate base tile and no separate Unequip button.
- **"Equip"** — when the selected inventory tile is NOT the currently equipped page. Pressing it dispatches `equipKeyPage` with the inventory page's `bookInstanceId`.
- **Hidden** — when there is no valid selection (e.g. the librarian is on their base AND no inventory tile has been clicked yet, OR the selection points at an `instanceId` no longer present in inventory).

The button SHALL be disabled while an edit action is in flight (`editBusy`), matching the existing equip-button gating. The "Unequip" variant SHALL render with a destructive (red) accent so users see at a glance that pressing it removes their current key page rather than equipping something new. The librarian editor MUST NOT render a separate "Base" tile, badge, or selectable surface representing the origin key page.

When the librarian is on their base, no inventory tile SHALL carry the `kp-tile--equipped` class, but the detail panel SHALL render the base page's stats (sourced from `lib.keyPage`, which the engine populates with the base's data when nothing else is equipped). The user always sees what their librarian is currently wearing; the base just doesn't appear as a separate browse option.

#### Scenario: Equipped tile selection triggers Unequip

- **WHEN** the librarian is equipped with an inventory page
- **AND** the user selects that same equipped inventory tile (either the default selection on open or by explicit click)
- **THEN** the action button reads "Unequip"
- **AND** the action button renders with the destructive (red) accent
- **AND** pressing the button dispatches `unequipKeyPage`

#### Scenario: Non-equipped inventory tile triggers Equip

- **WHEN** the user selects an inventory tile other than the currently equipped page
- **THEN** the action button reads "Equip"
- **AND** pressing the button dispatches `equipKeyPage` with the tile's `bookInstanceId`

#### Scenario: On-base librarian shows base stats with no action button

- **WHEN** the librarian is on their base
- **AND** the user has not selected an inventory tile
- **THEN** the action button is hidden (no Equip/Unequip target makes sense in this state)
- **AND** the detail panel renders the stats of the librarian's current key page (which is the base — `lib.keyPage` carries the base's full data)

#### Scenario: No base tile is rendered

- **WHEN** the user opens the Key Page tab for any librarian
- **THEN** the rendered grid contains zero tiles representing the base/origin key page
- **AND** the chapter pill filter and inventory groups are the only browsable surfaces
