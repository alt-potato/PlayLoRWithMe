## ADDED Requirements

### Requirement: `LibrarianEntry` SHALL carry a `baseKeyPage` field

`LibrarianEntrySchema` SHALL expose a required `baseKeyPage: KeyPage` field on every librarian entry. The C# serializer MUST source it from `UnitDataModel.defaultBook` and emit the same field set it already writes for the equipped `keyPage` (identifiers, stats, range type, rarity, multi-deck flag, resistances). The field MUST NOT appear on battle-unit `keyPage` payloads — it is exclusive to librarian-management context.

The base key page is structurally identical to the equipped key page schema. When the librarian is currently on their base, `keyPage` and `baseKeyPage` MUST agree on `instanceId` and all detail fields.

#### Scenario: Librarian entry parses with baseKeyPage

- **WHEN** `GameStateSchema` parses a `LibrarianEntry` produced by the C# serializer
- **THEN** the parsed object's `baseKeyPage` field is present and structurally valid as `KeyPage`
- **AND** removing `baseKeyPage` from the payload causes `LibrarianEntrySchema.parse` to fail in dev builds

#### Scenario: Battle-unit keyPage carries no baseKeyPage sibling

- **WHEN** the serializer writes a battle-unit object
- **THEN** the surrounding object does NOT include a `baseKeyPage` field

### Requirement: `ClientActionSchema` SHALL include an `unequipKeyPage` variant

`ClientActionSchema` SHALL accept a discriminated-union variant of the form:

```ts
{ type: "unequipKeyPage", floorIndex: number, unitIndex: number }
```

`LibrarianActions.unequipKeyPage(floorIndex, unitIndex)` MUST be added to the `LibrarianActions` interface with signature `(floorIndex: number, unitIndex: number) => Promise<ActionResult>`. The dev mock backend (`useMockBackend.ts`) MUST route this action through its existing log-only handler pattern — matching the established no-mutation contract documented in the `dev-mock-backend` spec.

#### Scenario: Action typechecks with required fields

- **WHEN** a caller invokes `actions.unequipKeyPage(0, 1)`
- **THEN** the call typechecks
- **AND** omitting either `floorIndex` or `unitIndex` raises a TypeScript error

#### Scenario: Action passes schema validation

- **WHEN** `ClientActionSchema` parses `{ type: "unequipKeyPage", floorIndex: 0, unitIndex: 1 }`
- **THEN** validation succeeds
- **AND** a payload missing `floorIndex` or `unitIndex` is rejected

#### Scenario: Mock backend routes the action without mutation

- **WHEN** the dev mock backend receives an `unequipKeyPage` action
- **THEN** the promise resolves to `{ ok: true }` without mutating `gameState`
- **AND** the payload is logged with the `[mock]` prefix, matching every other mock action
