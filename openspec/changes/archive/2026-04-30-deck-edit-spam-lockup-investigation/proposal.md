## Why

During smoke testing of the optimistic-deck-edit feature, the frontend was observed to "lock up" — no operations get accepted, the UI stops responding to deck edits — seemingly at random but tending to occur after a burst of rapid taps. Recovery requires (at minimum) closing the EditPanel; possibly a full reload. The lockup is reproducible under sustained spam-tap workloads but the root cause is not yet identified. We need to diagnose and fix it before this feature is considered done.

## What Changes

This change is investigative-then-corrective. The first half identifies the cause; the second half implements the fix.

- Add lightweight instrumentation (browser-side and mod-side) to surface what's happening during the lockup window: in-flight action queue depth, last sequence number, WebSocket state, lock owner, edit-busy state.
- Reproduce the lockup deterministically with a scripted spam-tap harness (or a dev-tools-driven loop) and capture the instrumentation output.
- Diagnose the root cause. Top suspects (in rough order):
  1. **Delta-gap → resync feedback loop.** `useWebSocket.handleMessage` requests a `resync` whenever `msg.seq !== lastSeq + 1`. If actions are flushed faster than the broadcast cycle, multiple deltas can arrive with seq values that all fail the strict-successor check, triggering repeated resyncs and starving subsequent action results.
  2. **Action-result timeout pileup.** `sendAction` resolves with `{ ok: false, error: "Action timed out" }` after 5 s. If responses are queued behind broadcasts, a burst of taps can produce a wall of timeouts; nothing in `DeckTab` distinguishes "timed out, no server effect" from "rejected, server effect rolled back" — and in the former case, the user's intent may have been silently dropped.
  3. **Server-side `SaveAndBroadcast` blocking the Unity main thread.** Each `addCardToDeck`/`removeCardFromDeck` calls `SaveAndBroadcast()` which serializes the entire game state and pushes it to all connected clients. Under spam, this can stall the action-injector queue and the WebSocket reader.
  4. **Stuck `editBusy` / lock contention.** The `editBusy` flag is computed from `lockBusy || renameBusy`; neither toggles on deck edits, so this is unlikely — but worth ruling out.
- Implement the fix that the diagnosis points to. If multiple causes are confirmed, fix them as separate sub-tasks (one commit per cause).
- Add a regression-style verification: the same spam harness used to reproduce SHALL run cleanly after the fix, with no observed lockup over a defined sustained workload (e.g. 200 taps over 10 s).

## Capabilities

### New Capabilities

- `deck-edit-resilience`: defines the invariants the deck editor SHALL hold under sustained input — request queue stays bounded, the client recovers from any delta gap without entering an irrecoverable state, and the `DeckTab` continues to accept new taps as long as the WebSocket is connected.

### Modified Capabilities

None planned at proposal time. If the diagnosis surfaces a wire-contract change (e.g. a new "ack" message), the implementation phase will add a delta to `wire-contract-schema`.

## Impact

- `frontend/app/composables/useWebSocket.ts`: likely receives the bulk of the fix (delta-gap handling, send-pacing, action-result reconciliation).
- `frontend/app/components/librarian/DeckTab.vue`: may need rate-limiting or back-pressure on the tap handlers if frontend-side flow control turns out to be necessary.
- `mod/Server.cs` and `mod/StateBroadcaster.cs`: may need broadcast coalescing if the Unity-thread-blocking suspect is confirmed.
- New dev-only spam-tap harness (an autoclicker bookmarklet or a Vitest-driven mock). No production runtime cost.
- No backend wire-protocol changes anticipated, but not ruled out until diagnosis lands.
