## Why

Players currently lose their chosen display name whenever the session can't be
resumed — first visits from another browser, sessions older than the 5-minute
expiry, server restarts, or a manual `localStorage` clear. Each time, the server
assigns a fresh `Player N` and the user has to retype their name. Persisting
the chosen name on the client removes that friction without changing the
existing protocol.

## What Changes

- Persist the player's chosen display name to `localStorage` whenever it is
  set or changed via the rename UI.
- On WebSocket connect, after the server's `hello` arrives, if the locally
  stored name differs from the server's view of this session's name (i.e. a
  fresh session with the auto-assigned `Player N`), send a `rename` to
  restore the saved name.
- Resumed sessions where the server already has the correct name skip the
  restore step (no redundant rename traffic).

## Capabilities

### New Capabilities

- `display-name-persistence`: client-side persistence and automatic restoration
  of the player's chosen display name across browser sessions, tab reloads, and
  session expiry events.

### Modified Capabilities

<!-- None — no existing specs to modify. The wire protocol and server-side
     SessionManager behavior are unchanged. -->

## Impact

- Affected code: `frontend/app/composables/useWebSocket.ts` (read stored name on
  hello, conditionally send rename), `frontend/app/components/SessionPanel.vue`
  (write stored name on commit).
- No mod-side (C#) changes; the existing `rename` message handles restoration.
- No new wire-protocol messages or fields.
- No new dependencies.
