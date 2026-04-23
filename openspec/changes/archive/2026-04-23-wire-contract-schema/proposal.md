## Why

The WebSocket JSON contract between the mod and the frontend is currently specified in two places — imperative `JsonWriter` calls in `GameStateSerializer.cs` and ~680 lines of hand-written TypeScript interfaces in `frontend/app/types/game.ts` — with nothing linking them. When either side changes, drift is silent until a component mis-renders at runtime, and edge cases (optional fields, enum variants, discriminated unions like `ClientAction`) are especially easy to diverge on. We also cannot meaningfully hand-author static fixtures (for the deferred `dev-mock-backend` work, or for a future C# test harness) because there is no machine-checkable shape to validate them against.

The frontend already carries the richer, better-factored description of the contract. Promoting those types to a runtime-parseable schema (Zod) gives us: (1) a canonical, version-controlled JSON Schema artifact, (2) live validation of incoming WebSocket payloads in dev mode so drift surfaces immediately in DevTools, and (3) a foundation any future C# refactor can generate DTOs from. No runtime library is added to the mod; the validation layer is purely frontend + CI.

## What Changes

- Add `zod` (v4+) as a frontend runtime dependency. Zod v4 ships a native `z.toJSONSchema()` converter — no separate codegen package is needed.
- Refactor `frontend/app/types/game.ts` so every exported type is derived from a Zod schema via `z.infer<>`. Consumer imports (`GameState`, `AllyUnit`, `Card`, `ClientAction`, etc.) keep their names and shapes — the migration is internal.
- Add `frontend/scripts/generate-schema.mjs` (run as a `prebuild` / `pretest` hook) that writes a canonical `schema/gamestate.schema.json` to the repo root via `z.toJSONSchema()`. The committed file must match what the script would emit — enforced by a CI check.
- Add dev-mode runtime validation in `useWebSocket`: when `import.meta.dev`, incoming `state` and `delta` payloads are parsed by the Zod schema; parse failures log a structured error to the console with the offending path. Production builds skip this cost entirely.
- Add a reference fixture at `schema/reference-state.json` (hand-curated, covers the common scenes — title, main, battle) and a unit test that parses it through the Zod schema. Any future schema change that invalidates the reference fixture fails CI until the fixture is regenerated from live output.
- **Deferred to a follow-up proposal** (not in this change): generated C# POCOs from `schema.json`, refactor of `GameStateSerializer.cs` to serialize from DTOs, C# runtime validation.

## Capabilities

### New Capabilities

- `wire-contract-schema`: canonical specification for the WebSocket JSON wire format. Defines the authoring source (Zod), the exported artifact (`schema.json`), the frontend validation semantics (dev-mode parse, prod-mode skip), and the CI enforcement rules (reference fixture + schema-diff check).

### Modified Capabilities

_(none — additive. The C# serializer and frontend consumers are untouched at the API level.)_

## Impact

- **Frontend code touched**:
  - `frontend/app/types/game.ts` — refactored to Zod schemas + `z.infer<>` exports. Consumer API is identical.
  - `frontend/app/composables/useWebSocket.ts` — dev-mode parse hook.
  - `frontend/scripts/generate-schema.mjs` (new) — build-time schema exporter.
  - `frontend/package.json` — adds `zod` (runtime); no separate build-time package needed.
- **Repo-level artifacts**:
  - `schema/gamestate.schema.json` (new) — canonical JSON Schema, committed.
  - `schema/reference-state.json` (new) — hand-curated reference payload, committed.
- **Frontend tests**:
  - `schema/reference-state.test.ts` — asserts the reference fixture parses cleanly.
  - Drift test — runs the exporter and compares output against the committed `schema.json`.
- **Mod**: no C# code changes, no new runtime deps, no DLL size change.
- **Bundle size**: adds `zod` to the frontend runtime (~12KB gzipped core). The `z.toJSONSchema()` converter is only imported by the build script, not app code, so it tree-shakes out of the production bundle.
- **Backwards compatibility**: the `GameState` interface exported from `types/game.ts` stays name-compatible with today's shape; any call site that compiled before continues to compile.
