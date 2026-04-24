## Why

Frontend iteration currently requires a running LoR instance with the mod loaded — every UI change means launching the game, clicking through the title screen, and navigating to the scene you want to inspect. That loop is slow, and certain edge cases (every `CardRange` × rarity combination, broken/dead units, overflow-prone descriptions, hybrid key pages) are hard or impossible to reproduce on demand from a live game.

The frontend already drives its entire render from a single `gameState` ref populated by `useWebSocket`. Replacing that one source with a static fixture unlocks full-fidelity UI work — `BATTLE_CTX`, injection, delta patching (no-op in mock mode), ownership gating — all exercised without the game running.

## What Changes

- Add a dev-only mock mode to `useWebSocket`, gated by `import.meta.dev` so the entire code path is tree-shaken out of production builds.
- When enabled (via `?mock=<fixture>` query param, persisted to `localStorage`), skip the WebSocket connection and populate `gameState`/`session`/`players` from a static JSON fixture under `frontend/app/dev/fixtures/`.
- Action functions (`sendAction`, `claimUnit`, …) return `{ok: true}` and log the payload to the console, so interactions don't hang but don't mutate state.
- Add a small dev-only fixture picker overlay (`DevFixturePicker.vue`) that lists available fixtures and swaps between them live, without a page reload.
- Seed the catalog with fixtures covering the key views:
  - `battle-sampler` — battle scene with enemies + allies, a hand showing every `CardRange` value and every rarity, broken/dead units, mass-range attacks with `subTargets`, overflow-prone descriptions.
  - `main-librarian` — librarian manager scene with a mix of locked/unlocked librarians and a populated card/key-page inventory.
  - `battle-setting` — pre-battle formation/deck phase (`uiPhase === "BattleSetting"`).
  - `emotion-upgrade` — battle scene with `abnormalitySelection` populated so `EmotionUpgradePicker` renders.

## Capabilities

### New Capabilities

- `dev-mock-backend`: dev-only mechanism for driving the frontend from static fixtures instead of the live WebSocket. Defines the activation surface (query param + localStorage), the fixture loader contract, action-call stubbing semantics, and the requirement that the mechanism compile to zero production bytes.

### Modified Capabilities

_(none — additive. The production WebSocket path is unchanged.)_

## Impact

- **Frontend code touched**:
  - `frontend/app/composables/useWebSocket.ts` — adds an `import.meta.dev` guarded mock branch.
  - `frontend/app/dev/useMockBackend.ts` — new: fixture loader, mock action handlers, reactive fixture switch.
  - `frontend/app/dev/fixtures/*.json` — new: four seed fixtures matching the `GameState` shape.
  - `frontend/app/dev/DevFixturePicker.vue` — new: dev-only overlay, mounted only when `import.meta.dev`.
  - `frontend/app/app.vue` — conditionally mounts `DevFixturePicker`.
- **Frontend tests**: unit test for the fixture-name parser (`parseMockFixtureName`) and the mock action handler.
- **No backend changes**: C# mod is untouched.
- **No new runtime dependencies**.
- **Production bundle**: verified via `npm run build` that `dev/` modules are not present in the output. Nuxt strips `if (import.meta.dev)` branches; the entire mock subsystem is dynamically imported inside that branch so the JSON fixtures and UI code tree-shake.
