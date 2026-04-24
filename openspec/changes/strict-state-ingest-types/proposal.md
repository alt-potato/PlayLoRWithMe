# Tighten type safety on state-ingest paths

## Why

The `wire-contract-schema` change landed a Zod-derived `GameStateSchema`, `ServerMessageSchema`, and `ClientActionSchema`, and wired dev-mode validation into `useWebSocket`. Three type-unsafe sites remain that bypass the contract:

1. `frontend/app/utils/deltaApply.ts:48` — returns `result as unknown as GameState`, a double-cast from `Record<string, unknown>`. Comment acknowledges this is a justified shortcut, but a malformed delta merge produces an invalid shape the type system cannot catch.
2. `frontend/app/dev/fixtures/index.ts:21-24` — four fixtures use `<fixture> as unknown as GameState` with no runtime validation. Mock payloads can drift from the wire contract silently, causing dev-mode feature testing to diverge from real-server behaviour.
3. `frontend/app/composables/useLibrarianActions.ts:10` — `sendAction(action: Record<string, unknown>)` accepts any record, while `useBattleActions.ts:39` uses the strongly-typed `ClientAction`. Inconsistent; librarian-action callers (`LibrarianManager.vue:208,214`) get no type-checking.

The infrastructure to fix all three already exists.

## What Changes

- Dev fixtures parse through `GameStateSchema.parse()` at load time; an invalid fixture throws with a Zod error path. Fixtures are dev-only, so the cost is acceptable.
- `applyDelta()` runs its output through `GameStateSchema.parse()` in dev builds (gated on `import.meta.dev`), matching the `useWebSocket` dev-validation pattern. Production keeps the existing cast to avoid the runtime parse cost; the dev gate must tree-shake from production bundles like the other `[wire-contract]` code paths.
- `LibrarianActions.sendAction` signature tightens from `(action: Record<string, unknown>) => Promise<ActionResult>` to `(action: ClientAction) => Promise<ActionResult>`. Callers (`setCustomization`, `setGifts` object literals) must conform to `ClientActionSchema`.

Out of scope:
- The existing justified cast in `deltaApply.ts` stays in production — only the dev-side validation is added.
- Strengthening the `delta` parameter type beyond `Record<string, unknown>` (would require a `DeepPartial<GameState>` scheme; larger architectural change).
- Typing `useWebSocket.sendAction`'s secondary `Record<string, unknown>` escape hatch (defer until all call sites migrate).

## Capabilities

- **Modified:** `wire-contract-schema` — adds three scenarios to the existing "dev-mode validation" requirement.

## Impact

- Affected code: 3 files (~10 lines changed).
- Affected tests: one new unit test per fix site; drift test already covers schema regressions globally.
- Risk: a currently-silent fixture drift will surface as a loud dev-time error. That is the desired behaviour, but confirm before archive that all 4 fixtures still parse cleanly.
