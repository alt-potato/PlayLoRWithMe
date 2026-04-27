# Design: Tighten type safety on state-ingest paths

## Context

This change is a direct follow-up to the archived `wire-contract-schema` change. That change established:
- Zod schemas as the source of truth in `frontend/app/types/game.ts`
- Dev-mode validation gated on `import.meta.dev`, tree-shaken from production
- A committed `schema/gamestate.schema.json` artifact with a drift test
- A `[wire-contract]` logging prefix for structured Zod errors

This change applies the same discipline to three ingest sites that predated or were missed by the original migration.

## Goals

1. Eliminate the three `as unknown as` casts in the frontend (one in `deltaApply.ts`, four in `dev/fixtures/index.ts`).
2. Tighten `LibrarianActions.sendAction` signature to match `BattleActions.sendAction`.
3. Keep production bundle size and runtime cost unchanged — all new validation is dev-only.
4. Fail loud when a fixture or merged delta violates `GameStateSchema`, with a Zod error path pointing at the bad field.

## Non-Goals

- Rewriting `applyDelta` to use a schema-derived `PartialGameState` input type (would require deriving a recursive partial from `GameStateSchema`, out of scope).
- Removing the prod-side cast in `applyDelta` (the comment at line 45-47 correctly identifies it as structurally sound given valid inputs; prod validation already runs in `useWebSocket` before `applyDelta` is called).
- Tightening `useWebSocket.sendAction`'s `ClientAction | Record<string, unknown>` escape hatch (requires migrating every caller in one go; deferred).

## Decisions

### Decision 1: Fixtures parse eagerly at load time

Replace:
```ts
"battle-sampler": () => battleSampler as unknown as GameState,
```
with:
```ts
"battle-sampler": () => GameStateSchema.parse(battleSampler),
```

**Rationale:** Fixtures are dev-only (tree-shaken from production via the existing `dev/` isolation). A parse cost per fixture load is negligible. A malformed fixture throwing with a Zod path is strictly better than a silent shape-mismatch deep inside a component render.

**Alternative considered:** `safeParse` + log. Rejected — fixtures are authored locally; if one is wrong the developer should be blocked until it's fixed.

### Decision 2: `applyDelta` dev-mode post-merge validation

The prod path stays unchanged. The dev path wraps the return value:

```ts
if (import.meta.dev) {
  const parsed = GameStateSchema.safeParse(result);
  if (!parsed.success) {
    console.error("[wire-contract] applyDelta produced invalid GameState", parsed.error.format());
  }
}
return result as unknown as GameState;
```

**Rationale:**
- Matches the `[wire-contract]` prefix and `safeParse`-don't-throw pattern established in `useWebSocket`.
- Keeps the existing comment honest — the cast IS justified given valid inputs; this adds a runtime check that inputs stayed valid through the merge.
- Dev-only branch tree-shakes from production per the wire-contract spec (`import.meta.dev` is a literal `false` in prod, Vite eliminates the branch).

**Alternative considered:** Replace the cast with `GameStateSchema.parse(result)` unconditionally. Rejected — adds parse cost to every delta in production, and duplicates work already done upstream in `useWebSocket`.

### Decision 3: `sendAction` signature tightening

Change:
```ts
sendAction: (action: Record<string, unknown>) => Promise<ActionResult>;
```
to:
```ts
sendAction: (action: ClientAction) => Promise<ActionResult>;
```

Callers (`LibrarianManager.vue:208,214`) pass object literals for `setCustomization` and `setGifts`. Both types must exist in `ClientActionSchema` for the signature to compile; verify during implementation and add to the schema if missing (note: if missing, that is a pre-existing wire-contract gap — flag and handle as a schema addition).

**Rationale:** Parity with `BattleActions.sendAction`. Restores type-checking on the one librarian-action escape hatch.

**Alternative considered:** Delete `sendAction` entirely, require all action types to have a typed wrapper method (like `lockLibrarian`, `renameLibrarian`). Rejected — `setCustomization`/`setGifts` take freeform payload objects; a typed wrapper adds boilerplate without catching bugs the union already catches.

## Risks

- **Fixture parse failure on existing fixtures** — if any of the four dev fixtures does not currently match `GameStateSchema`, this change will break dev-mock-backend until the fixture is fixed. Mitigation: run `npm test` immediately after the fixture change; fix the fixture, not the schema.
- **`setCustomization` / `setGifts` missing from `ClientActionSchema`** — if either is absent, tightening `sendAction` will fail typecheck. Mitigation: audit the schema first during task 3; if missing, add the schema entries as part of the same change.

## Verification

- `npm test` (frontend) — all existing tests pass, plus a new fixture-parse test and a new applyDelta-returns-valid-shape test.
- `npm run build` + grep on `frontend/.output/public/_nuxt/*.js` — no occurrences of `applyDelta produced invalid` (dev-only branch tree-shook out).
- Manual smoke in dev mode — load each fixture, confirm no `[wire-contract]` errors; trigger a battle state change, confirm delta merge still works.
