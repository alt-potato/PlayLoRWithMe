# wire-contract-schema Specification

## Purpose
TBD - created by archiving change wire-contract-schema. Update Purpose after archive.
## Requirements
### Requirement: The WebSocket wire format SHALL have a single canonical machine-checkable schema

The JSON payload of every `ServerMessage` and `ClientAction` MUST be describable by a Zod schema authored in `frontend/app/types/game.ts`. The set of schemas MUST collectively cover every field the mod emits and every field the frontend emits. Every TypeScript type currently exported from `frontend/app/types/game.ts` (including but not limited to `Die`, `Card`, `Unit`, `AllyUnit`, `SessionState`, `GameState`, `ServerMessage`, `ClientAction`) MUST be derived from its corresponding Zod schema via `z.infer<>` rather than hand-written as a standalone `interface` or `type` declaration.

Consumer imports of these named types (from any Vue component, composable, or utility) MUST continue to resolve to structurally equivalent shapes after the migration. No downstream file is permitted to change its import specifier as a result of this change.

#### Scenario: A Vue component imports GameState

- **WHEN** a Vue component declares `import type { GameState } from "~/types/game"`
- **THEN** `GameState` resolves to `z.infer<typeof GameStateSchema>`
- **AND** the inferred shape is structurally equivalent to the pre-migration `GameState` interface (every pre-existing field is present with the same type and optionality)

#### Scenario: Adding a new field to the wire contract

- **WHEN** a developer adds a field to the wire contract
- **THEN** the only authoring change required on the frontend side is updating the Zod schema
- **AND** `z.infer<>` propagates the field to the TypeScript type automatically
- **AND** regenerating `schema/gamestate.schema.json` includes the new field

### Requirement: The schema SHALL be exported to a canonical JSON Schema artifact, enforced by CI

A build script at `frontend/scripts/generate-schema.mjs` MUST export the root Zod schemas (`GameStateSchema`, `ServerMessageSchema`, `ClientActionSchema`) to a single committed file at `schema/gamestate.schema.json`, using JSON Schema 2020-12 via `zod-to-json-schema`. The exporter MUST be wired into the frontend's `prebuild`, `pregenerate`, and `pretest` lifecycles so that any run of `npm run build`, `npm run generate`, or `npm test` regenerates the artifact first.

A drift test MUST run the exporter in memory, compare its output against the committed `schema/gamestate.schema.json`, and fail with a readable diff when they disagree. The test MUST run as part of `npm test` so CI catches any uncommitted schema changes.

#### Scenario: Committed schema.json is up to date

- **WHEN** `npm test` runs and the committed `schema/gamestate.schema.json` matches what `generate-schema.mjs` would emit
- **THEN** the drift test passes silently

#### Scenario: Committed schema.json is stale

- **WHEN** a developer modifies a Zod schema in `types/game.ts` and runs `npm test` without running `npm run generate-schema`
- **THEN** the drift test fails
- **AND** the failure message includes the JSON-path diff identifying the drifted field(s)
- **AND** the message points the developer at `npm run generate-schema`

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

### Requirement: The frontend SHALL validate incoming WebSocket payloads in development builds

When `import.meta.dev === true`, `useWebSocket` MUST run every incoming WebSocket payload through `ServerMessageSchema.safeParse` before acting on it. On `safeParse` failure the hook MUST log a structured error to the browser console with the `[wire-contract]` prefix and the formatted Zod error, but MUST NOT throw, reject, or disconnect — the existing downstream `handleMessage` path still runs so the frontend keeps working while the developer diagnoses.

When `import.meta.dev === false`, no validation occurs. The Zod-powered branch MUST tree-shake out of the production bundle such that the literal string `[wire-contract]` does not appear anywhere under `frontend/.output/public/_nuxt/*.js` after `npm run build` or `npm run generate`.

#### Scenario: Dev build receives a valid payload

- **WHEN** `import.meta.dev` is true and the mod sends a valid `state` message
- **THEN** `ServerMessageSchema.safeParse` returns `success: true`
- **AND** no console errors are emitted
- **AND** downstream message handling runs normally

#### Scenario: Dev build receives a drifted payload

- **WHEN** `import.meta.dev` is true and the mod sends a payload with an unexpected field shape (e.g. a string where a number is expected)
- **THEN** a single `console.error` entry with the `[wire-contract]` prefix is emitted containing the Zod error path
- **AND** the frontend does NOT throw, close the connection, or stop processing subsequent messages

#### Scenario: Production build

- **WHEN** the frontend is built with `npm run build` or `npm run generate`
- **THEN** the built bundle under `frontend/.output/public/_nuxt/*.js` contains zero occurrences of the string `[wire-contract]`
- **AND** the production bundle does not pay the Zod-parse cost at runtime on WebSocket messages

### Requirement: The mod DLL SHALL NOT grow as a result of adopting the schema contract

This change MUST NOT add any runtime dependency to the compiled mod. No `Newtonsoft.Json`, `System.Text.Json`, or other JSON library may be added to `PlayLoRWithMe.csproj` or its NuGet package references. The file `mod/GameStateSerializer.cs` and `mod/JsonWriter.cs` remain the production serialization path; their contents are not required to change as part of this requirement.

Drift between the mod's emitted JSON and the canonical schema is treated as a bug surfaced by the dev-mode validator (Requirement above) and by future capture-and-replay fixture updates, not by compile-time enforcement on the C# side. A future proposal may add generated C# DTOs or library-backed serialization; that work is explicitly out of scope here.

#### Scenario: Mod build produces unchanged DLL size

- **WHEN** `cd mod && dotnet build` runs before this change and again after this change is applied
- **THEN** `mod/bin/Debug/PlayLoRWithMe/Assemblies/PlayLoRWithMe.dll` differs only in build metadata (timestamp, mvid) — the uncompressed code size changes by no more than a few hundred bytes attributable to recompilation of untouched source files
- **AND** the `Assemblies/` directory contains no new DLLs

#### Scenario: Backend drift is detected in development

- **WHEN** `GameStateSerializer.cs` is edited to emit a payload that no longer matches the Zod schema, and a developer runs the mod with `npm run dev` in the frontend
- **THEN** the browser console shows a `[wire-contract]` structured error on the first WebSocket message that carries the drifted shape
- **AND** the frontend continues to function (no crash, no disconnect)

### Requirement: Dev-mode fixtures SHALL parse through `GameStateSchema`

Every file exported from `frontend/app/dev/fixtures/index.ts` that surfaces a `GameState`-shaped payload MUST return the result of `GameStateSchema.parse(<fixture>)` rather than an `as unknown as GameState` cast. Parse failure MUST throw with the Zod error path so the developer is immediately blocked.

Fixtures are dev-only and are tree-shaken from production bundles; the parse cost is incurred only when a developer explicitly loads a mock scenario.

#### Scenario: A valid fixture loads

- **WHEN** a dev-mode consumer calls `fixtures["battle-sampler"]()`
- **THEN** `GameStateSchema.parse` returns a fully-typed `GameState`
- **AND** the call site receives a value whose TypeScript type is `GameState` without any cast

#### Scenario: A drifted fixture loads

- **WHEN** a fixture JSON has a field whose shape does not match `GameStateSchema` (e.g. an integer where a string is expected)
- **THEN** `GameStateSchema.parse` throws a `ZodError`
- **AND** the thrown error includes the JSON path to the bad field (e.g. `allies[0].light`)

### Requirement: `applyDelta` SHALL validate its result against `GameStateSchema` in dev builds

`frontend/app/utils/deltaApply.ts::applyDelta` MUST run its return value through `GameStateSchema.safeParse` when `import.meta.dev === true`. On `safeParse` failure, the function MUST emit a single `console.error` with the `[wire-contract]` prefix and the formatted Zod error path, but MUST NOT throw, reject, or change the returned value — the existing merged result is returned unchanged so the UI keeps rendering while the developer diagnoses.

When `import.meta.dev === false`, no validation runs. The dev-only branch MUST tree-shake from production bundles such that the literal string `applyDelta produced invalid` does not appear anywhere under `frontend/.output/public/_nuxt/*.js` after `npm run build` or `npm run generate`.

#### Scenario: Dev build merges a valid delta

- **WHEN** `import.meta.dev` is true and `applyDelta` receives a delta whose merged result conforms to `GameStateSchema`
- **THEN** `safeParse` returns `success: true`
- **AND** no console errors are emitted
- **AND** the merged `GameState` is returned

#### Scenario: Dev build merges a drifted delta

- **WHEN** `import.meta.dev` is true and the delta merge produces a shape that violates `GameStateSchema` (e.g. an ally gains an invalid enum value)
- **THEN** a single `console.error` with the `[wire-contract]` prefix is emitted containing the Zod error path
- **AND** `applyDelta` returns the merged result without throwing
- **AND** downstream consumers continue to process subsequent deltas

#### Scenario: Production build

- **WHEN** the frontend is built with `npm run build` or `npm run generate`
- **THEN** the bundle under `frontend/.output/public/_nuxt/*.js` contains zero occurrences of the string `applyDelta produced invalid`
- **AND** the production `applyDelta` does not invoke any Zod parser on delta messages

### Requirement: `LibrarianActions.sendAction` SHALL accept only `ClientAction`

The `sendAction` field of the `LibrarianActions` interface in `frontend/app/composables/useLibrarianActions.ts` MUST be typed as `(action: ClientAction) => Promise<ActionResult>`. The previous permissive type `(action: Record<string, unknown>) => Promise<ActionResult>` MUST be removed.

All call sites within the librarian-management subtree (e.g. `LibrarianManager.vue`'s `setCustomization` / `setGifts` calls) MUST pass literals that structurally satisfy the `ClientActionSchema` discriminated union. If any action type used by those call sites is not yet represented in `ClientActionSchema`, the schema MUST be extended as part of the same change so the callers compile.

#### Scenario: A caller passes a valid ClientAction

- **WHEN** `LibrarianManager.vue` invokes `actions.sendAction({ type: "setCustomization", floorIndex: 0, unitIndex: 0, ... })`
- **AND** `"setCustomization"` is a member of `ClientActionSchema`'s discriminated union
- **THEN** the call typechecks without `any` or cast
- **AND** removing a required field from the literal triggers a TypeScript error at the call site

#### Scenario: A caller passes an untyped record

- **WHEN** a caller attempts `actions.sendAction({} as Record<string, unknown>)`
- **THEN** TypeScript rejects the call because `Record<string, unknown>` is not assignable to `ClientAction`

### Requirement: `KeyPage` and `AvailableKeyPage` SHALL carry an optional `rarity` field

`KeyPageSchema` and `AvailableKeyPageSchema` MUST expose an optional
`rarity?: string` field whose value, when present, is one of the strings
emitted by the C# `Rarity` enum (`"Common"`, `"Uncommon"`, `"Rare"`,
`"Unique"`, `"Special"`).

The C# serializer MUST emit `rarity` on:

- every entry in `availableKeyPages`
- the librarian-owned `keyPage` field on each `LibrarianEntry`

The C# serializer MUST NOT emit `rarity` on battle-unit `keyPage` payloads,
so the field is naturally absent from combat contexts.

#### Scenario: Available key page includes rarity

- **WHEN** the serializer writes an `availableKeyPages` entry
- **THEN** the resulting JSON object includes a `rarity` string sourced
  from `BookXmlInfo.Rarity.ToString()`

#### Scenario: Librarian-owned key page includes rarity

- **WHEN** the serializer writes a `LibrarianEntry`'s `keyPage` field
- **THEN** the resulting JSON object includes a `rarity` string sourced
  from `BookXmlInfo.Rarity.ToString()`

#### Scenario: Battle-unit key page omits rarity

- **WHEN** the serializer writes a battle-unit `keyPage` object
- **THEN** the resulting JSON object does NOT include a `rarity` field

#### Scenario: Schema accepts payload without rarity

- **WHEN** `GameStateSchema` parses a payload whose `keyPage` lacks
  `rarity` (e.g. an older snapshot or a battle payload)
- **THEN** parsing succeeds and the field is `undefined` on the resulting
  object

### Requirement: `LibrarianEntry.decks` SHALL replace `deckPreview`

`LibrarianEntrySchema` SHALL expose a `decks` field of type `DeckPreview[]` and SHALL NOT carry the legacy `deckPreview` field.

`DeckPreview` is defined as:

```ts
{
  index: number;          // 0..3
  label?: string;         // present iff the key page is multi-deck
  cards: CardId[];
}
```

For single-deck key pages the array MUST contain exactly one entry with `index === 0` and no `label`. For multi-deck key pages (`KeyPage.isMultiDeck === true`), the array MUST contain exactly four entries with `index` values `0..3` in ascending order.

The C# serializer MUST iterate `book.GetDeckAll_nocopy()` to produce `decks` for librarian-context payloads. The serializer MUST NOT emit `decks` on battle-unit `keyPage` payloads — battle-time UI surfaces the active deck through other channels and remains unaffected by this change.

#### Scenario: Single-deck librarian carries one entry

- **WHEN** the serializer writes a `LibrarianEntry` whose key page has `isMultiDeck === false`
- **THEN** `decks.length === 1`
- **AND** `decks[0].index === 0`
- **AND** `decks[0].label` is absent
- **AND** `decks[0].cards` matches the single deck's contents

#### Scenario: Multi-deck librarian carries four entries

- **WHEN** the serializer writes a `LibrarianEntry` whose key page has `isMultiDeck === true`
- **THEN** `decks.length === 4`
- **AND** `decks[i].index === i` for `i` in `0..3`
- **AND** each `decks[i].cards` matches `_deckList[i]`'s contents

#### Scenario: Schema rejects legacy `deckPreview`

- **WHEN** a payload contains a top-level `deckPreview` field on a `LibrarianEntry`
- **THEN** `LibrarianEntrySchema.parse` either ignores the field (if the schema is permissive) or rejects it
- **AND** consumers SHALL NOT read from `lib.deckPreview`

### Requirement: `KeyPage.isMultiDeck` SHALL be exposed on librarian-context payloads

`KeyPageSchema` SHALL carry a required `isMultiDeck: boolean` field on librarian-management payloads (the `LibrarianEntry.keyPage` slot). The C# serializer MUST source this from `BookModel.IsMultiDeck()`.

The field SHALL NOT appear on battle-unit `keyPage` payloads, consistent with the existing pattern for librarian-only fields (e.g. `rarity`).

#### Scenario: Librarian-owned key page carries isMultiDeck

- **WHEN** the serializer writes a `LibrarianEntry`'s `keyPage`
- **THEN** the resulting JSON object includes `isMultiDeck: true | false` sourced from `BookModel.IsMultiDeck()`

#### Scenario: Battle-unit key page omits isMultiDeck

- **WHEN** the serializer writes a battle-unit `keyPage`
- **THEN** the resulting JSON object does NOT include `isMultiDeck`

### Requirement: `addCardToDeck` and `removeCardFromDeck` SHALL accept optional `deckIndex`

`ClientActionSchema`'s `addCardToDeck` and `removeCardFromDeck` variants SHALL each carry an optional `deckIndex?: number` field. When omitted the server MUST treat it as `0`. When present, the value MUST satisfy `0 <= deckIndex < 4`.

The mod MUST validate the index range and the librarian's `IsMultiDeck` status before mutating, and MUST resolve invalid requests with a structured `ok: false` response.

#### Scenario: Action without deckIndex defaults to deck 0

- **WHEN** a client sends `{ type: "addCardToDeck", floorIndex: 0, unitIndex: 0, cardId: { id: 1, packageId: 0 } }` with no `deckIndex`
- **THEN** the server treats the action as targeting `deckIndex: 0`

#### Scenario: Action with deckIndex 0..3 targets that slot

- **WHEN** a client sends `addCardToDeck` with `deckIndex: 2` for a multi-deck librarian
- **THEN** the server mutates `_deckList[2]`

#### Scenario: Action with out-of-range deckIndex is rejected

- **WHEN** a client sends `addCardToDeck` or `removeCardFromDeck` with `deckIndex: 4` or `deckIndex: -1`
- **THEN** the server resolves the request with `{ ok: false, error: "deckIndex out of range" }`
- **AND** no mutation occurs

#### Scenario: Action with deckIndex !== 0 on single-deck book is rejected

- **WHEN** a client sends `addCardToDeck` with `deckIndex: 1` for a librarian whose key page has `isMultiDeck === false`
- **THEN** the server resolves the request with `{ ok: false, error: "key page is not multi-deck" }`
- **AND** no mutation occurs

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
