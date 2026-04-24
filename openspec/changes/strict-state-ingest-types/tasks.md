# Tasks: Tighten type safety on state-ingest paths

## 1. Audit ClientActionSchema for librarian-escape-hatch payloads

- [ ] 1.1 Inspect `ClientActionSchema` in `frontend/app/types/game.ts` and confirm `setCustomization` and `setGifts` entries exist with fields matching the call sites at `LibrarianManager.vue:208` and `LibrarianManager.vue:214`.
- [ ] 1.2 If either entry is missing or does not match the caller's literal, extend `ClientActionSchema` to cover it. Regenerate `schema/gamestate.schema.json` via `npm run generate-schema` and verify the drift test passes.

## 2. Dev fixtures → GameStateSchema.parse

- [ ] 2.1 Replace the four `as unknown as GameState` casts in `frontend/app/dev/fixtures/index.ts:21-24` with `GameStateSchema.parse(<fixture>)` calls. Import `GameStateSchema` from `~/types/game`.
- [ ] 2.2 Run `cd frontend && npm test`. If any fixture throws a `ZodError`, fix the fixture JSON to match the schema — DO NOT loosen the schema.
- [ ] 2.3 Add a unit test under `frontend/app/dev/fixtures/index.test.ts` that iterates every entry in the exported fixtures map and asserts each returns without throwing. This locks the contract for future fixture additions.

## 3. applyDelta → dev-mode schema check

- [ ] 3.1 In `frontend/app/utils/deltaApply.ts`, wrap the return statement with a dev-gated `GameStateSchema.safeParse` call. On failure, `console.error` with the `[wire-contract]` prefix and the formatted Zod error. Return the merged result unchanged regardless of validation outcome.
- [ ] 3.2 Update the comment above the return to reflect the new dev-time safety net while explaining the prod-side cast remains justified (upstream validation in `useWebSocket`).
- [ ] 3.3 Extend `frontend/app/utils/deltaApply.test.ts` with a dev-mode test: stub `import.meta.dev = true`, merge a delta that injects an invalid enum value into an ally, assert `console.error` is called with the `[wire-contract]` prefix and the merged result is still returned.
- [ ] 3.4 Build with `cd frontend && npm run build`, grep the generated bundle under `.output/public/_nuxt/*.js` for `applyDelta produced invalid`, and confirm zero matches (tree-shake check).

## 4. LibrarianActions.sendAction → ClientAction

- [ ] 4.1 In `frontend/app/composables/useLibrarianActions.ts:10`, change `sendAction: (action: Record<string, unknown>) => Promise<ActionResult>` to `sendAction: (action: ClientAction) => Promise<ActionResult>`. Add the `ClientAction` import from `~/types/game`.
- [ ] 4.2 Run `cd frontend && npm run typecheck` (or `nuxi typecheck`). Fix any call-site errors — if task 1.2 was needed, this should now compile. Confirm the `LibrarianManager.vue:208,214` call sites still typecheck.
- [ ] 4.3 In `app.vue` (the provider), verify the provided `sendAction` implementation remains compatible with the narrowed type.

## 5. Validation

- [ ] 5.1 `cd frontend && npm test` — all tests pass including the new fixture and delta tests.
- [ ] 5.2 `cd frontend && npm run build` — build succeeds.
- [ ] 5.3 `cd mod && dotnet build` — build is clean (`0 Warning(s) 0 Error(s)`).
- [ ] 5.4 `openspec validate strict-state-ingest-types` — change is valid.
- [ ] 5.5 Manual smoke: launch frontend in dev mode, load each of the four fixtures via `useMockBackend`, confirm no `[wire-contract]` errors in the console.
