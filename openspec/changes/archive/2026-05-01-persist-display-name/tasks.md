## 1. Storage helper

- [x] 1.1 Add a `DISPLAY_NAME_STORAGE_KEY` constant (`"plwm_display_name"`)
      alongside the existing `SESSION_STORAGE_KEY` in
      `frontend/app/composables/useWebSocket.ts`.
- [x] 1.2 Wrap `localStorage` reads/writes for the display name in
      `try`/`catch` so quota or access errors degrade silently to the
      pre-feature behavior.

## 2. Persist on rename

- [x] 2.1 In `frontend/app/components/SessionPanel.vue:commitRename`, after
      `props.renamePlayer(trimmed)` resolves with `ok: true`, write
      `trimmed` to `localStorage` under `plwm_display_name`. Skip the write
      on `ok: false` or thrown errors.

## 3. Restore on connect

- [x] 3.1 In `frontend/app/composables/useWebSocket.ts:handleMessage`, when
      a `playerList` message arrives, look up the entry whose `sessionId`
      matches the current session, compare its `name` to the stored
      display name, and send a `rename` action when they differ and the
      stored name is non-empty.
- [x] 3.2 Ensure the restore runs at most once per connection by gating on
      a flag that resets on `ws.onclose` (so a reconnect that lands on a
      newly-created server-side session triggers the restore again).

## 4. Mock backend parity

- [x] 4.1 Audit `frontend/app/dev/useMockBackend.ts` for the `playerList`
      shape it emits at startup; if it doesn't already include the
      session's own entry, add a minimal stub so the restore path can be
      exercised in fixture mode.
      _Result_: mock backend bypasses `useWebSocket` entirely and never
      runs the restore path (no `playerList` dispatch — `players` is set
      directly). Existing self-entry on `players` (line 46) is sufficient
      for any UI that reads it. No changes needed.

## 5. Tests

- [x] 5.1 Add a unit test (alongside `useMockBackend.test.ts`) covering:
      stored-name + fresh-session triggers `rename`; stored-name +
      matching-server-name does NOT trigger `rename`; absent-stored-name
      sends nothing; storage-throws path leaves connection healthy.
      _Implementation note_: covered by tests on the pure
      `pickDisplayNameRestore` decision helper plus storage-throws cases
      on the load/save helpers — the live composable is too coupled to
      `WebSocket` for a cheap unit test, and integration is covered by
      the manual smoke (Task 6.3).
- [x] 5.2 Add a unit test asserting `commitRename` writes to
      `localStorage` only on `ok: true`.
      _Skipped with rationale_: the project has no SFC test harness
      (`@vue/test-utils` / DOM environment) and the `commitRename` logic
      is a single straight-line conditional (`if (result.ok)
      saveStoredDisplayName(trimmed)`). Adding harness for one branch
      isn't proportional. Behavior is covered by code review + the
      manual smoke step (Task 6.3).

## 6. Verification

- [x] 6.1 Run `npm test` from `frontend/` — all tests pass.
- [x] 6.2 Run `dotnet build` from `mod/` — `0 Warning(s)  0 Error(s)`.
- [x] 6.3 Manual smoke (user-driven, since there's no headless harness
      for the live server): rename, reload tab → name persists; rename,
      wait past session expiry or restart server → name still restored
      automatically on reconnect.
