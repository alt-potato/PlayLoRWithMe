> **PAUSED** — blocked on `wire-contract-schema`. Once that change lands, the
> mock fixture files will be validated against the canonical Zod schema, so
> we resume after that contract is in place. Proposal/design remain valid;
> only the fixture-authoring tasks will pick up a Zod-parse step.

## 1. Mock backend composable + fixture registry

- [ ] 1.1 Create `frontend/app/dev/useMockBackend.ts` exporting `useMockBackend(fixtureName: string)` that returns the full `useWebSocket` shape (`gameState`, `session`, `status`, `players`, `sendAction`, and every action function). `gameState` resolves via a reactive `currentFixture` ref fed to a watchEffect; action handlers log `[mock] action: <json>` and resolve `{ ok: true }`.
- [ ] 1.2 Create `frontend/app/dev/fixtures/index.ts` with a `FIXTURE_LOADERS` record mapping fixture names to synchronous loaders (JSON is statically imported). Include only the names — fixture JSON files are populated in the next task group.
- [ ] 1.3 Install a `window.__plwmMock` debug hook inside `useMockBackend` exposing `setFixture(name)` and `listFixtures()`. Guard with `typeof window !== 'undefined'`.
- [ ] 1.4 Add a unit test at `frontend/app/dev/useMockBackend.test.ts` verifying action handlers resolve `{ok: true}` and that an unknown fixture name produces a visible error (`gameState.value` stays null + console.error).

## 2. Seed fixtures

- [ ] 2.1 Author `frontend/app/dev/fixtures/battle-sampler.json` — battle scene with 3 allies (one broken, one dead) and 3 enemies, ally hand including one card of every `CardRange` value (Near/Far/Instance/Special/FarArea/FarAreaEach) and every rarity (Common/Uncommon/Rare/Unique), at least one card with `subTargets`, at least one card with a long `abilityDesc` and long per-die descriptions.
- [ ] 2.2 Author `frontend/app/dev/fixtures/main-librarian.json` — main scene with `floors[]` populated (one floor, 3 librarians: one locked, one unlocked, one with no key page equipped), `availableKeyPages[]` with 2-3 entries, `availableCards[]` with 5-6 entries across rarities.
- [ ] 2.3 Author `frontend/app/dev/fixtures/battle-setting.json` — main scene with `uiPhase === "BattleSetting"`, formation data, a couple of allies assigned and unassigned.
- [ ] 2.4 Author `frontend/app/dev/fixtures/emotion-upgrade.json` — battle scene with `abnormalitySelection` populated (3 choices at emotion level 3 including one ally-targeting card).
- [ ] 2.5 Wire each fixture into `FIXTURE_LOADERS` in `dev/fixtures/index.ts`.

## 3. useWebSocket integration

- [ ] 3.1 Add `frontend/app/dev/resolveMockFixture.ts` with a pure `resolveMockFixture(): string | null` that reads `?mock=<name>` from `location.search`, writes a non-empty value to `localStorage["plwm_mock_fixture"]`, clears on empty, and falls back to localStorage when the query param is absent.
- [ ] 3.2 In `frontend/app/composables/useWebSocket.ts`, add an `if (import.meta.dev)` block at the top of `useWebSocket()` that calls `resolveMockFixture()`; when non-null, returns `useMockBackend(name)` and skips the rest of the function.
- [ ] 3.3 Add a unit test at `frontend/app/dev/resolveMockFixture.test.ts` covering: query-param wins over localStorage; empty query-param clears localStorage; absent query-param + absent localStorage → null; absent query-param + present localStorage → localStorage value.

## 4. DevFixturePicker overlay

- [ ] 4.1 Create `frontend/app/dev/DevFixturePicker.vue` — a fixed-position panel in the lower-right of the viewport with a heading "Mock fixture" and a button per fixture in `FIXTURE_LOADERS`. Clicking a button calls `window.__plwmMock.setFixture(name)` and updates the URL's query param via `history.replaceState`. Include a "close" button that hides the picker (state held locally in `useState` so HMR doesn't resurrect it).
- [ ] 4.2 In `frontend/app/app.vue`, add `<ClientOnly v-if="$dev"><DevFixturePicker /></ClientOnly>` (or the equivalent `import.meta.dev` compile-time check) below the main scene router, so the picker is rendered only on dev builds.
- [ ] 4.3 Verify visually in the browser that (a) hitting `localhost:3000/?mock=battle-sampler` shows the battle-sampler fixture rendered; (b) switching via the picker swaps the fixture in place without a reload; (c) the picker is visible only with a fixture active.

## 5. Production-stripping verification

- [ ] 5.1 Run `cd frontend && npm run build`. Inspect `frontend/.output/public/_nuxt/*.js` for any occurrence of `battle-sampler`, `useMockBackend`, `DevFixturePicker`, `__plwmMock`. There must be none. If there are, diagnose the dev-guard failure before proceeding.
- [ ] 5.2 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`. (The mod build triggers `npm run generate`, the production frontend build.)

## 6. Validation

- [ ] 6.1 Run `cd frontend && npm test` — all tests pass.
- [ ] 6.2 Start the dev server (`cd frontend && npm run dev`) and spot-check each fixture renders the expected scene (battle, librarian manager, battle setting overlay, emotion upgrade overlay).
