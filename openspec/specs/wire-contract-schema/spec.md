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

