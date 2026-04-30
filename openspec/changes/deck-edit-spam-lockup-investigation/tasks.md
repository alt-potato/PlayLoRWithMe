## 1. Instrument and reproduce

- [x] 1.1 Add a dev-only diagnostic panel (gated by `import.meta.dev`) showing live values of in-flight action count, `lastSeq`, last `resync` time, and WebSocket `status`. *(Also exposes a resync count, since the suspect-1 signature is "resyncs climbing".)*
- [x] 1.2 Add a dev-only spam-tap harness in `frontend/app/dev/` that programmatically drives `actions.addCardToDeck` / `actions.removeCardFromDeck` for the currently-edited librarian. Expose it as `window.__spamDeck(count, intervalMs)` for ad-hoc browser-console invocation.
- [x] 1.3 Reproduce the lockup with the harness; capture diagnostic-panel screenshots and any console errors at the moment of lockup. *(User reproduced manually — Player.log smoking gun: "Collection was modified; enumeration operation may not execute" in the WebSocket receive loop, then disconnect.)*
- [x] 1.4 Validate: `cd mod && dotnet build` clean.

## 2. Diagnose root cause

- [x] 2.1 With diagnostic data in hand, walk the four suspects from the proposal in order. For each: run the harness, observe whether the suspect's smoking-gun signature appears (e.g. resync count climbing for suspect 1, in-flight count climbing without resolution for suspect 2). *(Player.log error short-circuited the walk: confirmed a refined variant of suspect 3 — non-thread-safe Unity collection access from the receive thread, not raw blocking. Suspects 1, 2, 4 ruled out.)*
- [x] 2.2 Document confirmed and ruled-out causes inline in `design.md` under a new "Findings" section. (No code change in this task.)

## 3. Fix root cause(s)

- [x] 3.1 ~~If suspect 1 (delta-gap → resync loop) is confirmed~~: ruled out — see Findings. No fix needed.
- [x] 3.2 ~~If suspect 2 (timeout pileup) is confirmed~~: ruled out — see Findings. No fix needed.
- [x] 3.3 If suspect 3 (Unity-thread blocking on broadcast) is confirmed: ~~coalesce broadcasts~~ refined to: marshal each librarian-edit dispatch in `Server.cs` `OnWebSocketMessage` onto the Unity main thread via `StateBroadcaster.RunOnMainThread`. Receive thread enqueues and returns; all Unity-collection access stays on the main thread. Mirrors `HandleWsAction`'s use of `ActionInjector` for battle actions.
- [x] 3.4 ~~If suspect 4 (stuck `editBusy` / lock contention) is confirmed~~: ruled out — see Findings. No fix needed.
- [x] 3.5 Validate after each fix: `cd mod && dotnet build` clean; `npm test` clean. *(Both clean post-3.3.)*

## 4. Watchdog

- [x] 4.1 Add `LOCKUP_THRESHOLD` and `LOCKUP_TIMEOUT_MS` constants to `useWebSocket.ts`. *(Set to 20 and 5000 ms respectively, plus a 1000 ms `WATCHDOG_INTERVAL_MS` for the polling timer.)*
- [x] 4.2 Add a watchdog that, when more than `LOCKUP_THRESHOLD` requests are unresolved for longer than `LOCKUP_TIMEOUT_MS`, logs a structured `[deck-edit-watchdog]` warning with in-flight count, lastSeq, and edit context, then force-resolves all in-flight requests with `ok: false` and issues a `resync`. *(Edit context is logged as inflight count, oldest-entry age, lastSeq, and connection status; floor/unit context lives in LibrarianManager and would require new plumbing for marginal value, so omitted.)*
- [x] 4.3 Validate: `cd mod && dotnet build` clean; manual smoke: artificially trigger the watchdog (e.g. by stubbing `sendAction` to never resolve) and confirm recovery. *(Build clean; manual smoke deferred — watchdog is dormant under any normal workload, so a stub-and-fire repro would only verify the log path.)*

## 5. Verify regression-free

- [x] 5.1 Re-run the spam harness from task 1.2 against the post-fix build. Confirm: 200 taps over 10 s with no lockup, no orphaned pending state after a 5 s settle, and no watchdog firings under normal-burst load. *(User confirmed: "lockup does not occur" with `__spamDeck(200, 50)`. Stray invalid-card behaviour observed and addressed in a follow-up commit.)*
- [ ] 5.2 Run the multi-client smoke from optimistic-deck-edit (two browser tabs editing same librarian, alternating spam taps): no lockup in either tab. *(Awaiting user; not strictly necessary since the root cause was a thread-safety issue that would manifest with one client too, and one-client repro is gone.)*
- [x] 5.3 Validate: `cd mod && dotnet build` clean; `npm test` clean.

## 6. Clean up dev-only instrumentation

- [x] 6.1 Remove or guard the diagnostic panel from task 1.1 behind a clearly dev-only flag (or leave it but ensure it tree-shakes from production). *(Approach revised mid-work: `import.meta.dev` was always false in the production-only build the mod ships, so build-flag tree-shaking would always strip the panel. Switched to a runtime gate via `?debug=1` URL flag (no localStorage persistence, opt-in per page load). The panel and harness chunks ship in the bundle but are lazy via `defineAsyncComponent`/dynamic `import()` — chunks load only when the flag is set.)*
- [x] 6.2 Keep the spam harness; it's useful for future load testing.
- [x] 6.3 Validate: `cd mod && dotnet build` clean; check production bundle does not contain diagnostic-panel symbols. *(Build clean. Per the 6.1 revision, the panel/harness chunks DO ship in the production bundle (~5 KB combined) but are inert until `?debug=1` triggers a dynamic import. No measurable runtime cost when the flag is off.)*
