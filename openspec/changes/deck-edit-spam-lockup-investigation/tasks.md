## 1. Instrument and reproduce

- [ ] 1.1 Add a dev-only diagnostic panel (gated by `import.meta.dev`) showing live values of in-flight action count, `lastSeq`, last `resync` time, and WebSocket `status`.
- [ ] 1.2 Add a dev-only spam-tap harness in `frontend/app/dev/` that programmatically drives `actions.addCardToDeck` / `actions.removeCardFromDeck` for the currently-edited librarian. Expose it as `window.__spamDeck(count, intervalMs)` for ad-hoc browser-console invocation.
- [ ] 1.3 Reproduce the lockup with the harness; capture diagnostic-panel screenshots and any console errors at the moment of lockup.
- [ ] 1.4 Validate: `cd mod && dotnet build` clean.

## 2. Diagnose root cause

- [ ] 2.1 With diagnostic data in hand, walk the four suspects from the proposal in order. For each: run the harness, observe whether the suspect's smoking-gun signature appears (e.g. resync count climbing for suspect 1, in-flight count climbing without resolution for suspect 2).
- [ ] 2.2 Document confirmed and ruled-out causes inline in `design.md` under a new "Findings" section. (No code change in this task.)

## 3. Fix root cause(s)

- [ ] 3.1 If suspect 1 (delta-gap → resync loop) is confirmed: in `useWebSocket.handleMessage`, queue/buffer deltas with seq > expected and apply them in order once the gap is closed by the resync's state message, instead of demanding strict-successor-or-resync.
- [ ] 3.2 If suspect 2 (timeout pileup) is confirmed: distinguish "request acknowledged with ok:false" from "request timed out" in `ActionResult`, and adjust `DeckTab.handleAddCard` / `handleRemoveCard` to retry-or-reconcile rather than silently drop the optimistic state on timeout.
- [ ] 3.3 If suspect 3 (Unity-thread blocking on broadcast) is confirmed: in `mod/Server.cs` (or `StateBroadcaster.cs`), coalesce broadcasts that fall within a single Unity frame so a burst of N adds produces ≤ N + 1 broadcasts but ≥ ceil(N/frame) — preserving per-change `seq` while eliminating duplicate full-state serialization.
- [ ] 3.4 If suspect 4 (stuck `editBusy` / lock contention) is confirmed: pin down the stuck-state path in `EditPanel.vue` and reset the flag at the right edge.
- [ ] 3.5 Validate after each fix: `cd mod && dotnet build` clean; `npm test` clean.

## 4. Watchdog

- [ ] 4.1 Add `LOCKUP_THRESHOLD` and `LOCKUP_TIMEOUT_MS` constants to `useWebSocket.ts`.
- [ ] 4.2 Add a watchdog that, when more than `LOCKUP_THRESHOLD` requests are unresolved for longer than `LOCKUP_TIMEOUT_MS`, logs a structured `[deck-edit-watchdog]` warning with in-flight count, lastSeq, and edit context, then force-resolves all in-flight requests with `ok: false` and issues a `resync`.
- [ ] 4.3 Validate: `cd mod && dotnet build` clean; manual smoke: artificially trigger the watchdog (e.g. by stubbing `sendAction` to never resolve) and confirm recovery.

## 5. Verify regression-free

- [ ] 5.1 Re-run the spam harness from task 1.2 against the post-fix build. Confirm: 200 taps over 10 s with no lockup, no orphaned pending state after a 5 s settle, and no watchdog firings under normal-burst load.
- [ ] 5.2 Run the multi-client smoke from optimistic-deck-edit (two browser tabs editing same librarian, alternating spam taps): no lockup in either tab.
- [ ] 5.3 Validate: `cd mod && dotnet build` clean; `npm test` clean.

## 6. Clean up dev-only instrumentation

- [ ] 6.1 Remove or guard the diagnostic panel from task 1.1 behind a clearly dev-only flag (or leave it but ensure it tree-shakes from production).
- [ ] 6.2 Keep the spam harness; it's useful for future load testing.
- [ ] 6.3 Validate: `cd mod && dotnet build` clean; check production bundle does not contain diagnostic-panel symbols.
