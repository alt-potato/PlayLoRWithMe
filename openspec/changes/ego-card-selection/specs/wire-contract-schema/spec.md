## MODIFIED Requirements

### Requirement: A reference fixture SHALL exercise the schema against realistic payloads

A committed file `schema/reference-state.json` MUST contain at least one representative `GameState` payload per major scene branch the UI can enter:

- `scene === "title"` (minimum payload)
- `scene === "main"` with `uiPhase === "BattleSetting"` and without it
- `scene === "battle"` with both ally and enemy units present
- `scene === "battle"` with `abnormalitySelection` populated
- `scene === "battle"` with `egoSelection` populated

A unit test MUST iterate every case in the fixture and parse it through `GameStateSchema`. The test MUST fail with a visible Zod error path when any case violates the schema.

#### Scenario: Reference fixture round-trips cleanly

- **WHEN** `npm test` runs against an unmodified `schema/reference-state.json` and an unmodified Zod schema
- **THEN** every case in the fixture parses without error

#### Scenario: Fixture violates the schema

- **WHEN** the Zod schema adds a new required field that the fixture does not provide
- **THEN** the reference-fixture test fails
- **AND** the failure message includes the Zod error path (`cases.battle.allies[0].light`) identifying the missing field
- **AND** the failure message suggests regenerating the fixture
