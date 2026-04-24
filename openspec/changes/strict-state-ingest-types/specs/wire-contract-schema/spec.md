# wire-contract-schema Specification Delta

## ADDED Requirements

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
