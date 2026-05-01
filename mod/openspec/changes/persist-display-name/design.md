## Context

The mod's session model treats the display name as server-owned: a session is
born with `"Player N"` (incrementing counter), the client sends `rename` to
change it, and the server holds the canonical value in `PlayerSession.DisplayName`.
The browser already persists `plwm_session` in `localStorage` so a tab reload
resumes the existing session — and with it, the existing name.

Three cases break that resume path and lose the name:

1. **First connection from a new browser/tab** — no stored sessionId.
2. **Session expired** — server discards sessions 5 minutes after disconnect.
3. **Server restart** — all in-memory sessions vanish.

In each case the server creates a fresh session with `"Player N"` and the user
must re-enter their preferred name. Persisting the name client-side and
restoring it via the existing `rename` message closes the gap without any
protocol or server changes.

## Goals / Non-Goals

**Goals:**

- Remember the chosen display name across reloads, server restarts, and
  session expiry, scoped to a single browser profile.
- Restore the name automatically and silently on connect when the server
  doesn't already have it (fresh session).
- Avoid redundant `rename` traffic on the common case where the session
  resumed cleanly.

**Non-Goals:**

- Cross-device or cross-browser identity (would require a real account
  system; out of scope for a single-host co-op tool).
- Persisting unit claims, librarian edits, or any other session state — only
  the display name is restored.
- Server-side persistence of names across restarts.
- Cookie-based storage. `localStorage` matches the existing `plwm_session`
  pattern and avoids server-side cookie parsing for a value that's purely
  client-driven.

## Decisions

### Storage mechanism: `localStorage` under `plwm_display_name`

Mirrors the existing `plwm_session` key in `useWebSocket.ts`. Synchronous
read on connect is fine — the codebase already does the same for sessionId
without `nextTick` gymnastics. Cookies were considered but rejected: they'd
either need server-side parsing (the server has no use for the name beyond
echoing it back) or `document.cookie` with manual expiry, both more code for
no benefit over `localStorage`.

### When to write

`SessionPanel.vue:commitRename` writes after a successful `renamePlayer` call
resolves with `ok: true`. Writing before the round-trip would cache a name
the server may have rejected (e.g. if future validation is added); writing
on failure would leak invalid state into future sessions. The trimmed value
already used for the rename payload is what gets stored.

### When to read & send `rename`

Restoration happens in `useWebSocket.ts` after the `playerList` message that
follows `hello`, since `hello` itself only carries `sessionId` — not the
display name the server has for this session. The `playerList` payload
contains an entry for our own session (matched by `sessionId`); comparing
its `name` to the stored value tells us whether the server already has the
right name (resumed session) or is using its auto-assigned default (fresh
session). Only the second case triggers a `rename` send.

Alternative considered: sending `rename` unconditionally on every connect
would simplify the logic but spam the server (and other clients via
`playerList` broadcast) on every reload. The compare-and-skip path is one
extra string compare and saves a round-trip on the common case.

### What counts as "stored vs. server differ"

Exact-string equality after `trim()`. Empty stored name = no restore. The
server's auto-assigned `"Player N"` always differs from any user-chosen
name, so the check naturally classifies fresh sessions correctly without
hardcoding the `"Player N"` pattern.

### Edge case: stored name was the auto-assigned default

If a user never renamed and `localStorage` has no entry, behavior is
unchanged. If a user renamed *back* to a literal string matching the
server's current auto-name (e.g. typed `"Player 3"` deliberately), the
compare would skip the rename — harmless, since the server already has
that exact name.

### SSR / tree-shaking

`localStorage` access is gated by the same `import.meta.dev` /
`onMounted`-style patterns the codebase already uses for `plwm_session`.
Nuxt's SSR build never executes `useWebSocket`'s connect path during prerender
(it's called from `app.vue`'s mounted lifecycle), so no `window`-guard is
needed beyond what's already there.

## Risks / Trade-offs

- **[Stale name across distinct identities on shared browser]** → Mitigation:
  scope is one browser profile, same as `plwm_session`. Users who share a
  browser already see this with claims/sessions; the tradeoff is acceptable
  for a co-op tool typically run on a single trusted host.
- **[Race between `playerList` arrival and rename reply]** → Mitigation: the
  rename is fire-and-forget (its result isn't awaited for further logic). If
  the server's `playerList` broadcast for the rename arrives before the
  rename's own `actionResult`, no harm — both paths converge on the same
  name being set.
- **[Quota exceeded / storage disabled]** → Mitigation: wrap reads/writes in
  `try`/`catch`; log a dev-only warning and fall through to the existing
  behavior (name not persisted, server uses default). The feature degrades
  to status quo, never breaks the connection.

## Migration Plan

No data migration. The new `localStorage` key starts empty for existing
users; first rename populates it, and subsequent connects restore it.
Removing the feature later is a one-line deletion of the read-and-restore
block plus the write call — old entries become inert.

## Open Questions

None blocking. Future work (out of scope) could move persistence to a
shared cookie if cross-tab same-origin sync becomes desirable, but that
requires deciding whose value wins on conflict and is not motivated by
current usage.
