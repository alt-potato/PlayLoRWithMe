## MODIFIED Requirements

### Requirement: Frontend SHALL support a dev-only mock mode that drives `gameState` from a static fixture instead of the WebSocket

The frontend MUST expose a dev-only mock mode when built with `import.meta.dev === true`. Mock mode MUST activate whenever a fixture name is resolved from either the `mock` query-string parameter on `location.search` or the `plwm_mock_fixture` key in `localStorage`. The query parameter MUST take precedence and, when present and non-empty, MUST overwrite the localStorage value. When the query parameter is present and empty, both the query parameter and the localStorage value MUST be cleared.

When mock mode is active, `useWebSocket` MUST NOT open a WebSocket connection. Instead, it MUST return an object whose public shape is identical to the non-mock `useWebSocket` return (same reactive refs, same action functions, same return-value types), with the following behavioural changes:

- `gameState` is populated from the fixture JSON file associated with the resolved fixture name.
- `session` and `players` are populated with synthetic values sufficient for downstream components (including `SessionPanel`, `BATTLE_CTX`, and `LIBRARIAN_ACTIONS`) to render without errors.
- `status` is set to `"connected"` at all times.
- Every action function (`sendAction`, `claimUnit`, `releaseUnit`, `renamePlayer`, `lockLibrarian`, `unlockLibrarian`, `renameLibrarian`, `equipKeyPage`, `unequipKeyPage`, `addCardToDeck`, `removeCardFromDeck`, `equipSourceBook`, `unequipSourceBook`, `attributePassive`, `removeAttributedPassive`) MUST resolve with `{ok: true}` without mutating state and MUST log the received payload to the browser console with a `[mock]` prefix.

When `import.meta.dev === false`, the mock mode code path MUST NOT exist in the compiled output. Specifically, the `frontend/app/dev/` directory contents (fixtures, mock composable, fixture picker) MUST be tree-shaken out of the production bundle such that none of the tokens `useMockBackend`, `battle-sampler`, `DevFixturePicker`, or `__plwmMock` appear in `frontend/.output/public/_nuxt/*.js`.

#### Scenario: Dev build, no fixture selected

- **WHEN** `import.meta.dev` is true and neither `?mock=` query param nor `plwm_mock_fixture` localStorage key is set
- **THEN** `useWebSocket` behaves exactly as in production — opening a WebSocket, tracking sessions, handling deltas
- **AND** no fixture is loaded

#### Scenario: Dev build, fixture activated by query param

- **WHEN** the page is loaded at `/?mock=battle-sampler`
- **THEN** `useWebSocket` does NOT open a WebSocket
- **AND** `gameState.value` is populated from `dev/fixtures/battle-sampler.json`
- **AND** `localStorage.plwm_mock_fixture` is set to `"battle-sampler"`
- **AND** `status.value` is `"connected"`

#### Scenario: Dev build, fixture activated by localStorage on refresh

- **WHEN** `localStorage.plwm_mock_fixture` is `"battle-sampler"` and the URL has no `?mock=` param
- **THEN** the battle-sampler fixture is loaded
- **AND** no WebSocket connection is opened

#### Scenario: Dev build, fixture swapped at runtime via DevFixturePicker

- **WHEN** mock mode is active and the dev picker calls `window.__plwmMock.setFixture("main-librarian")`
- **THEN** `gameState.value` updates to the `main-librarian` fixture content
- **AND** no page reload occurs
- **AND** the URL's `?mock=` query param is updated to `main-librarian` via `history.replaceState`

#### Scenario: Dev build, mock action dispatched

- **WHEN** mock mode is active and a user interaction triggers `sendAction({type: "playCard", ...})`
- **THEN** the promise resolves to `{ok: true}` without mutating `gameState`
- **AND** the payload is logged to the browser console with a `[mock]` prefix

#### Scenario: Dev build, query param cleared

- **WHEN** the page is loaded at `/?mock=` (empty value)
- **THEN** both the query-param-resolved name and `localStorage.plwm_mock_fixture` are cleared
- **AND** mock mode is not active on this load

#### Scenario: Production build, mock code stripped

- **WHEN** the frontend is built with `npm run build` (or `npm run generate`)
- **THEN** `grep` on the output chunks for `useMockBackend`, `battle-sampler`, `DevFixturePicker`, or `__plwmMock` yields zero matches
- **AND** the `?mock=` query-string value is ignored at runtime
