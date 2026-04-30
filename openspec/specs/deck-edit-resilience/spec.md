# deck-edit-resilience Specification

## Purpose

Defines the responsiveness invariants the librarian deck editor SHALL hold under sustained input. The originating motivation was a server-side `InvalidOperationException` ("Collection was modified") that crashed the WebSocket receive loop when librarian-edit handlers ran concurrently on multiple receive threads and mutated non-thread-safe Unity model collections. The handlers are now marshaled onto the Unity main thread, and a client-side watchdog catches the broader class of "stuck-in-flight" failures so any future regression of the same shape surfaces visibly rather than silently.

## Requirements

### Requirement: Frontend SHALL remain responsive under sustained deck-edit input

The deck editor SHALL continue to accept tap input as long as the WebSocket is connected, regardless of how many requests are in flight or how many state deltas are arriving. "Responsive" means the click handlers fire, optimistic state updates appear in the same render cycle as the tap, and the in-flight action queue does not grow unboundedly.

#### Scenario: Sustained spam-tap workload does not lock up the UI

- **WHEN** the user (or a dev-mode spam harness) issues 200 deck add/remove taps over 10 seconds
- **THEN** every tap fires its click handler within the same render cycle
- **AND** every tap produces an optimistic UI change (pending-add tile or pending-remove hide) within the same render cycle
- **AND** the in-flight action queue size stays bounded (does not grow proportionally to taps)
- **AND** after the workload completes, all pending UI state reconciles within 5 seconds

#### Scenario: Lockup is visibly recoverable rather than silent

- **WHEN** the in-flight action queue holds more than `LOCKUP_THRESHOLD` entries unresolved for longer than `LOCKUP_TIMEOUT_MS`
- **THEN** the client logs a structured `[deck-edit-watchdog]` console warning containing: in-flight count, lastSeq, currently-edited floor/unit
- **AND** the client force-resolves all in-flight requests with `{ ok: false, error: "watchdog: requests stalled" }`
- **AND** the client issues a `resync` to re-establish authoritative state
- **AND** the deck editor immediately resumes accepting new taps

### Requirement: Delta-gap recovery SHALL converge

After a delta-gap is detected and a `resync` is issued, the client SHALL not enter a feedback loop where the resync response itself fails the gap check and triggers another resync. A single resync SHALL fully restore strictly-incrementing sequence ordering.

#### Scenario: Gap → resync → recovery completes in one cycle

- **WHEN** the client detects `msg.seq !== lastSeq + 1` and sends a `resync`
- **AND** the server responds with a fresh `state` message
- **THEN** `lastSeq` is set to the new state message's `seq`
- **AND** the next incoming `delta` (with `seq === lastSeq + 1`) is applied without triggering another resync
- **AND** no more than one `resync` is sent per gap event

#### Scenario: Concurrent action requests survive a resync

- **WHEN** multiple action requests are in flight at the moment a `resync` is triggered
- **THEN** each in-flight request resolves with either its real server result or a single `{ ok: false }` (not a duplicate or a hang)
- **AND** no in-flight request remains pending past the watchdog threshold
