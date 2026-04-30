## Context

`useWebSocket` opens a single WebSocket to `mod/Server.cs`. Each user action carries a `reqId`; the server replies with `actionResult` and (when the action mutates state) immediately broadcasts a `delta` to all clients with a strictly-incrementing `seq`. The client checks `msg.seq === lastSeq + 1` and triggers a `resync` if there's any gap.

`DeckTab.vue`'s tap handlers call `actions.addCardToDeck` / `actions.removeCardFromDeck`, which return `Promise<ActionResult>` with a 5-second client-side timeout. The optimistic UI rolls back only on `ok: false`; a timed-out request is `ok: false` with `error: "Action timed out"` — indistinguishable to the caller from a real server rejection.

On the mod side, each deck edit handler calls `SaveAndBroadcast()`, which serializes the full game state and writes it to every connected WebSocket. The serializer is non-trivial in size; the broadcast must run on the Unity main thread, which is shared with the action-injector queue.

## Goals / Non-Goals

**Goals:**

- Reproduce the lockup deterministically.
- Identify the actual root cause(s) — empirical, not theoretical.
- Fix the cause(s); leave the system stable under sustained taps (target: 200 taps over 10 s with no lockup, no orphaned pending state).
- Add a guard or watchdog so a future regression of the same shape surfaces visibly rather than silently locking up.

**Non-Goals:**

- Wholesale redesign of the WebSocket layer.
- Replacing the full-state-broadcast model with a more granular protocol — possible long-term, but out of scope here.
- Optimizing the C# serializer for throughput (separate concern).
- Production telemetry. Instrumentation here is dev-time only and removed (or guarded) once the fix lands.

## Decisions

### Decision: Instrument first, fix second

Don't change behavior until we can prove what's breaking. The first task group adds dev-only logging (browser console + a small status panel showing in-flight count, lastSeq, and resync count) and a scripted spam harness. Only then do we hypothesize and fix.

This is mostly to avoid the trap of "fix-first based on a guess and miss the actual issue." The lockup has multiple plausible causes; only repro + measurement disambiguates.

### Decision: Spam harness lives in `frontend/app/dev/`

Adds a dev-only function (or page) that drives the deck-tap handlers programmatically: e.g. tap-add 50 random cards from inventory, then tap-remove them, in a tight `for` loop or `setTimeout(0)` chain. Lives next to the existing mock-backend infrastructure, gated by `import.meta.dev` so it never ships.

**Alternative considered**: a real autoclicker against the rendered DOM. Rejected because it depends on viewport state (cards must be visible in the grid), which makes the harness less reliable and harder to script around.

### Decision: Per-suspect fix tasks, separately committable

Each suspect from the proposal gets its own task group. Some may turn out to be non-issues — those tasks become "verified, no fix needed" notes rather than code changes. This keeps commits atomic and reviewable.

### Decision: Add a watchdog as a defense-in-depth measure regardless of root cause

Even after the specific bug is fixed, a watchdog timer in `useWebSocket` catches the broader class of "stuck-in-flight" failures: if the in-flight action map size exceeds a threshold (e.g. 20 entries unresolved for >5 s) the watchdog logs a structured warning and force-resolves all pending requests with `ok: false`, then issues a `resync`. The user's actions are interrupted but the UI doesn't stop accepting input.

The threshold is generous enough that normal bursts pass through; it only fires when something has gone systemically wrong.

## Risks / Trade-offs

- **The lockup may be hard to reproduce reliably**, which would make the diagnosis phase open-ended.
  → Mitigation: time-box the diagnosis phase (target: <2 working sessions). If repro is slippery, lean harder on the watchdog (defense-in-depth) and ship without root-cause confirmation, accepting that we may revisit later.

- **A heavy-handed watchdog could mask future bugs** by silently clearing state instead of surfacing them.
  → Mitigation: the watchdog logs a structured warning with diagnostic context (in-flight count, lastSeq, current floor/unit) every time it fires, so a regression remains visible to anyone looking at the console.

- **A coalesced broadcast on the mod side could mask intermediate states** that the frontend depends on (e.g. delta-driven reconciliation).
  → Mitigation: only coalesce broadcasts that haven't been sent yet within a small time window (e.g. 16 ms — one Unity frame); preserve `seq` increments per logical change so the diff watcher still sees one delta per change.
