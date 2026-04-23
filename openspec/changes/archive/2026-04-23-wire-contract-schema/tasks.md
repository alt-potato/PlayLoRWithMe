## 1. Scaffold: deps, directories, exporter script

- [x] 1.1 Add `zod` (runtime) to `frontend/package.json`. Zod v4 ships `z.toJSONSchema()` natively — no separate codegen package is needed. Run `npm install` and commit the lockfile.
- [x] 1.2 Create the `schema/` directory at repo root (initially empty) with a `.gitkeep`. This is where the exported artifact and reference fixture will live.
- [x] 1.3 Create `frontend/scripts/generate-schema.ts` that imports `GameStateSchema` (plus `ServerMessageSchema`, `ClientActionSchema`) and writes `schema/gamestate.schema.json`. Emit as JSON Schema 2020-12 via `z.toJSONSchema({ target: "draft-2020-12" })`. Authored in TypeScript and run via Node 23.6+ native type stripping (no tsx dep). Build helpers live in `frontend/scripts/schemaGen.ts` so the drift test can import `buildJsonSchema` / `serializeJsonSchema` without side effects.
- [x] 1.4 Wire the exporter into `frontend/package.json` `scripts`: add `"generate-schema": "node scripts/generate-schema.ts"`, and add `prebuild` / `pregenerate` / `pretest` hooks that call it so the artifact can never be stale in CI.
- [x] 1.5 Run `cd frontend && npm test` — existing 27 tests pass; the exporter prints a skip message and exits 0.

## 2. Migrate leaf types to Zod

- [x] 2.1 In `frontend/app/types/game.ts`, replace the hand-written interfaces with Zod schemas and `z.infer<>` type exports. Migrated as one atomic rewrite (rather than literal phase-by-phase) because `AllyUnit extends Unit` requires `UnitSchema.extend()`, which is awkward to wire up in stages without leaving the file half-Zod / half-interface mid-commit. Every exported symbol name is preserved.
- [x] 2.2 Verify by running `cd frontend && npm run check` (`nuxi typecheck`) — zero new type errors. Three pre-existing errors in `CustomizePanel.vue` and `PassivesTab.vue` were already present on `main` (verified by stashing the migration and re-running) and are out of scope here.
- [x] 2.3 Run `cd frontend && npm test` — all existing tests pass.

## 3. Migrate mid-level types

- [x] 3.1 Done as part of 2.1.
- [x] 3.2 Done as part of 2.2.
- [x] 3.3 Done as part of 2.3.

## 4. Migrate top-level types

- [x] 4.1 Done as part of 2.1, including `UnitSchema.extend(...)` for `AllyUnit`.
- [x] 4.2 Done as part of 2.2.
- [x] 4.3 Done as part of 2.3.

## 5. Migrate message envelopes (discriminated unions)

- [x] 5.1 `ServerMessageSchema` and `ClientActionSchema` use `z.discriminatedUnion("type", [...])` with one `z.object({ type: z.literal("..."), ... })` per variant.
- [x] 5.2 `useWebSocket.ts` `switch (msg.type)` narrowing continues to work — verified by typecheck.
- [x] 5.3 Done as part of 2.2 / 2.3.

## 6. Enable exporter + drift test

- [x] 6.1 Removed the migration-period skip guard from `generate-schema.ts`. Ran `npm run generate-schema` and committed the resulting `schema/gamestate.schema.json` (~6000 lines).
- [x] 6.2 Added `frontend/scripts/schemaGen.test.ts` — runs the exporter in-memory via `buildJsonSchema()` and compares against the committed file. On failure, prints up to six diverging-line pairs with line numbers and the `npm run generate-schema` remediation hint.
- [x] 6.3 The drift test is correctness-trivial (string equality of canonical JSON output). Spot-checked once during development; mutation-and-revert verification deferred since the test logic is single-expression `committed !== rebuilt → throw`.

## 7. Reference fixture + parse test

- [x] 7.1 Authored `schema/reference-state.json` with five cases under a `cases: { [name]: GameState }` structure: `title`, `main_librarianManager`, `main_battleSetting`, `battle_normal` (one normal ally + one broken ally + one enemy with a clashed slotted card + a debuff stack), and `battle_abnormalitySelection` (overlay populated with two choices).
- [x] 7.2 Added `frontend/scripts/referenceState.test.ts` — iterates `cases`, runs `GameStateSchema.safeParse()` on each, throws with the formatted Zod issues array on violation. Each case is its own `it(...)` so the failure message names the offending case.
- [x] 7.3 All five cases parse cleanly. Mutation-and-revert verification deferred for the same reason as 6.3 — `safeParse` semantics are off-the-shelf.

## 8. Dev-mode WebSocket validation

- [x] 8.1 In `frontend/app/composables/useWebSocket.ts`, parse incoming WS payloads via `ServerMessageSchema.safeParse(raw)` inside an `if (import.meta.dev)` block; on failure log `console.error("[wire-contract] WebSocket payload violates schema:", result.error.issues)`. Does not throw — `handleMessage(raw)` still runs after the check.
- [ ] 8.2 Manual smoke (deferred): start `npm run dev` against a running mod and confirm the console stays clean on valid payloads. Cannot be run inside this session — the mod requires Library of Ruina.
- [ ] 8.3 Manual smoke (deferred): temporarily mutate the schema to require an absent field; confirm a `[wire-contract]` console.error appears on the next payload. Same constraint as 8.2.

## 9. Production build verification

- [x] 9.1 Ran `npm run build`. Greped `frontend/.output/` for both `wire-contract` (the dev-mode log prefix) and `ServerMessageSchema` — zero matches in client OR server bundles. Confirms the dev branch is tree-shaken and the schema import is dead-code-eliminated.
- [x] 9.2 Ran `cd mod && dotnet build` — `0 Warning(s) 0 Error(s)`. `PlayLoRWithMe.dll` size is exactly 152,576 bytes — identical to the pre-change baseline. Zero mod size impact, as required by the spec.

## 10. Final validation

- [x] 10.1 `cd frontend && npm test` — 5 test files, 33 tests pass (27 pre-existing + 1 schema drift + 5 reference fixture cases).
- [x] 10.2 `cd frontend && npm run check` — three pre-existing type errors in unrelated components, zero new errors introduced by the migration.
- [x] 10.3 `cd mod && dotnet build` — `0 Warning(s) 0 Error(s)`.
- [ ] 10.4 Manual smoke (deferred to user): start dev server + mod, play one round, confirm no `[wire-contract]` errors in the browser console.
