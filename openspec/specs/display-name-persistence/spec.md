# display-name-persistence Specification

## Purpose

Defines client-side persistence and automatic restoration of the player's chosen display name across browser reloads, server restarts, and 5-minute session expiry events. The server treats the display name as session-scoped (auto-assigned `Player N` on session creation, mutated only via `rename`); when a session can't be resumed the user would otherwise have to retype their name on every fresh connect. The frontend writes the name to `localStorage` after a successful rename and replays it via a single `rename` after the post-connect `playerList` reveals the server is using its auto-assigned default. Resumed sessions whose server-side name already matches the stored value send no redundant rename.

## Requirements

### Requirement: Persist chosen display name to local storage

The frontend SHALL write the player's display name to browser local storage
under the key `plwm_display_name` after a successful rename operation, so
that the name is available to restore on subsequent connections from the
same browser profile.

#### Scenario: Successful rename writes to storage

- **WHEN** the user submits a non-empty display name through the rename UI
  and the server responds with `ok: true`
- **THEN** the trimmed name MUST be written to `localStorage` under
  `plwm_display_name`

#### Scenario: Failed rename does not write to storage

- **WHEN** the rename request fails (server returns `ok: false`, the request
  times out, or the connection drops before a response)
- **THEN** the value in `localStorage` under `plwm_display_name` MUST NOT be
  modified

#### Scenario: Empty rename input is ignored

- **WHEN** the user submits an empty or whitespace-only name
- **THEN** no rename request is sent and `localStorage` is not modified

### Requirement: Restore stored display name on fresh sessions

When a player connects with no resumable session, or when the resumed
session's name on the server does not match the stored name, the frontend
SHALL automatically send a `rename` to restore the stored name. Resumed
sessions whose server-side name already matches the stored name SHALL NOT
trigger a redundant rename.

#### Scenario: Fresh session restores stored name

- **GIVEN** `localStorage.plwm_display_name` is `"Ada"`
- **WHEN** the WebSocket connects and the server reports this session's
  current name as the auto-assigned default (e.g. `"Player 3"`)
- **THEN** the frontend SHALL send a `rename` action with `name: "Ada"`

#### Scenario: Resumed session skips redundant rename

- **GIVEN** `localStorage.plwm_display_name` is `"Ada"`
- **WHEN** the WebSocket connects and the server reports this session's
  current name as `"Ada"` (resumed session)
- **THEN** the frontend MUST NOT send a `rename` action

#### Scenario: No stored name leaves server default in place

- **GIVEN** `localStorage.plwm_display_name` is absent or empty
- **WHEN** the WebSocket connects
- **THEN** the frontend MUST NOT send a `rename` action and the server's
  current name (auto-assigned or resumed) SHALL be used as-is

### Requirement: Storage failures degrade gracefully

If `localStorage` is unavailable (private browsing, quota exceeded, disabled
by user policy), display-name persistence SHALL be skipped silently without
breaking the WebSocket connection or the rename UI.

#### Scenario: Read failure falls through to server default

- **WHEN** reading `localStorage.plwm_display_name` throws or returns no
  value at connect time
- **THEN** no `rename` is sent and the connection proceeds normally with
  the server's default name

#### Scenario: Write failure does not interrupt rename

- **WHEN** writing `localStorage.plwm_display_name` throws after a
  successful server rename
- **THEN** the rename's UI effect (the new name showing in the player list)
  is unchanged and no error surfaces to the user
