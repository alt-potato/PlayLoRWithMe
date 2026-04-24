## Context

Frontend rendering flows from one reactive source:

```
useWebSocket → gameState ref → app.vue scene router → BATTLE_CTX/LIBRARIAN_ACTIONS → children
```

Every user-visible surface (battle, librarian manager, emotion upgrade, battle setup) is derived from `gameState.value`. Injection and delta patching are downstream of that ref. To drive the UI from a fixture, we only need to replace the _source_ — everything below the ref keeps working.

Existing primitives:

- `useWebSocket` returns a well-defined shape: three reactive refs (`gameState`, `session`, `players`, `status`) and ~10 async action functions. All actions resolve to `ActionResult` (`{ ok: boolean; error?: string }`).
- Nuxt 4 statically strips `if (import.meta.dev)` branches and their inner imports during production build. Dynamic imports (`await import("…")`) inside such a branch produce an unused module that Rollup drops.
- The frontend build is triggered by `cd mod && dotnet build` via an `AfterBuild` step running `npm run generate`. That runs in production mode by default, so the mock subsystem is automatically stripped.

## Goals / Non-Goals

**Goals:**

- Zero production cost: no mock-related bytes in the built bundle, no runtime flag checks on the hot path.
- One source of truth for hook shape: `useWebSocket`'s return type stays identical; callers don't know mock mode exists.
- Opt-in per page load: `?mock=<name>` query param activates mock mode; absence = normal behaviour.
- Fixture swapping without reload: switching fixtures updates the `gameState` ref in place.
- Fixture coverage for the combinatorial surfaces (all card ranges × rarities) that are impractical to produce from live gameplay.

**Non-Goals:**

- **Mock delta streaming** — fixtures are full-state snapshots. Switching fixtures swaps state wholesale. Delta patching is already tested via live play.
- **Mock WebSocket emulation** — we skip the socket entirely; we do not pretend to be an RFC 6455 server.
- **Fidelity to real game physics** — fixtures may contain impossible combinations (every CardRange in one hand) that a real game would never produce. That is the point.
- **Production fallback or "try mock if offline" behaviour** — mock mode is a dev tool. If `import.meta.dev` is false, the flag is ignored.

## Decisions

### 1. Activation surface

Mock mode is active iff **all** hold:

- `import.meta.dev === true`
- A fixture name is present as either a `?mock=<name>` query param or the value of `localStorage["plwm_mock_fixture"]`

Query param takes precedence and, when present, is written to localStorage so a refresh preserves the fixture. Passing `?mock=` (empty) clears both.

Rationale: query param is bookmarkable and shareable; localStorage means the dev picker's selection survives HMR.

### 2. Code organisation

```
frontend/app/
  composables/
    useWebSocket.ts           # adds small `import.meta.dev` guarded branch
  dev/
    useMockBackend.ts         # fixture loader, mock action handlers, registry
    DevFixturePicker.vue      # floating overlay, dev-only
    fixtures/
      index.ts                # registry of fixture names + lazy loaders
      battle-sampler.json     # battle scene covering range×rarity combinatorics
      main-librarian.json     # librarian manager
      battle-setting.json     # pre-battle formation/deck
      emotion-upgrade.json    # abnormality-selection overlay
```

Everything under `dev/` is dynamically imported from inside `if (import.meta.dev)` branches so the bundler tree-shakes it for production.

### 3. `useWebSocket` integration

The hook's top-level signature is unchanged. At the start of the function body:

```ts
if (import.meta.dev) {
  const mockName = resolveMockFixture();
  if (mockName) {
    // Dynamic import so the mock module is only pulled into dev builds.
    const { useMockBackend } = await import("~/dev/useMockBackend");
    return useMockBackend(mockName);
  }
}
```

**Problem:** composables can't be `async` if their return value is consumed synchronously by the setup script. Solution: `useMockBackend` is a plain function (not async), imported statically inside the `import.meta.dev` branch. Nuxt handles `import.meta.dev` branch stripping at build time, so the static import disappears from production.

```ts
// useWebSocket.ts
export function useWebSocket() {
  if (import.meta.dev) {
    const mockName = resolveMockFixture();
    if (mockName) return useMockBackend(mockName);
  }
  // ...real WebSocket path
}
```

`useMockBackend` returns an object with the **same keys and types** as `useWebSocket`'s return. No ad-hoc cast.

### 4. `useMockBackend` shape

```ts
export function useMockBackend(fixtureName: string): UseWebSocketReturn {
  const gameState = ref<GameState | null>(null);
  const session = ref<SessionState | null>(mockSession());
  const status = ref<Status>("connected");   // pretends to be live
  const players = ref<PlayerInfo[]>(mockPlayers());

  const currentFixture = ref(fixtureName);
  watchEffect(async () => {
    gameState.value = await loadFixture(currentFixture.value);
  });

  // Provide a global hook so DevFixturePicker can swap fixtures live.
  if (typeof window !== "undefined") {
    (window as any).__plwmMock = {
      setFixture: (name: string) => { currentFixture.value = name; },
      listFixtures: () => Object.keys(FIXTURE_LOADERS),
    };
  }

  const sendAction = mockActionHandler(/* logs + returns ok:true */);
  // …all other action functions delegate to sendAction

  return { gameState, session, status, players, sendAction, /* … */ };
}
```

### 5. Fixture loader

Fixtures live as JSON files for authoring convenience but are loaded via static imports in a registry:

```ts
// dev/fixtures/index.ts
import battleSampler from "./battle-sampler.json";
import mainLibrarian from "./main-librarian.json";
// ...

export const FIXTURE_LOADERS: Record<string, () => Promise<GameState>> = {
  "battle-sampler": () => Promise.resolve(battleSampler as GameState),
  "main-librarian": () => Promise.resolve(mainLibrarian as GameState),
  // ...
};
```

JSON import is preferred over dynamic `fetch` because (a) Nuxt inlines small JSON at build time, (b) it type-checks against the imported shape, and (c) it participates in HMR.

### 6. `DevFixturePicker` overlay

A small floating panel in the lower-right of the viewport, visible only when `import.meta.dev` and a fixture is active. Lists available fixtures as buttons; clicking swaps `window.__plwmMock.setFixture(name)`. A "close" button hides the picker for the session. Minimal styling (matches the existing `.debug-info` details panel in `app.vue`).

Mounted conditionally:

```vue
<!-- app.vue -->
<ClientOnly v-if="import.meta.dev">
  <DevFixturePicker />
</ClientOnly>
```

### 7. Production stripping verification

After implementation, run `npm run build` and confirm via `grep` that no `dev-mock` / `useMockBackend` / `battle-sampler` strings appear in `frontend/.output/public/_nuxt/*.js`. If they do, the tree-shaking failed and the branch guarding is broken — this is a blocking bug, not a warning.

## Risks / Trade-offs

- **Fixture drift**: static JSON files fall out of sync with `GameState` type changes. Mitigation: type-checked imports catch structural mismatches at build time. A small CI check (running `tsc` over fixture types) can catch schema evolution regressions.
- **Hidden coupling via `window.__plwmMock`**: a global is not ideal but the alternative (prop-drilling or provide/inject from `useWebSocket`) pollutes the production type surface. Since it's gated to `import.meta.dev` it doesn't affect prod.
- **Mock mode ≠ integration test**: a passing fixture doesn't prove the live path works. This is a dev-loop accelerator, not a test substitute.

## Migration Plan

No migration required — additive. On merge, devs can opt in by appending `?mock=battle-sampler` to the dev-server URL; normal `npm run dev` behaviour is unchanged.
