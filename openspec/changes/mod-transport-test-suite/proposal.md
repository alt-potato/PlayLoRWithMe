## Why

The mod's transport/protocol layer — WebSocket framing, the RFC 6455 handshake,
JSON serialization, per-session delta diffing, and session/ownership rules — is
the highest-risk code for silent regressions: a wire-format bug corrupts every
client and is invisible in-game until something breaks. Today there are zero
automated tests for any C# code, so this layer can only be verified by manual
in-game smoke testing. This is also the layer least coupled to Unity, so it is
both the most valuable and the cheapest to bring under test.

## What Changes

- Add an SDK-style **net48 xUnit test project** (`mod.tests/`) that compiles the
  pure source files directly (linked `Compile` items), so the suite builds and
  runs via `dotnet test` **without** `Assembly-CSharp.dll`, Harmony, or any
  Unity assembly on the load path — making it CI-runnable on a stock runner.
- Introduce a **`ModLog` logging facade** (Unity-free, swappable `Action<string>`
  delegates) and route the `UnityEngine.Debug` calls in `SessionManager` (1) and
  `WebSocketClient` (7) through it. `Initializer` wires the real `Debug.Log`/
  `Debug.LogWarning` at startup; tests leave the no-op defaults.
- Replace `SessionManager`'s two direct `StateBroadcaster.Broadcast()` calls with
  a static **broadcast-notify hook** (`Action`), wired to `StateBroadcaster` on
  the Unity side. This severs `SessionManager`'s last hard dependency on the
  Harmony/Unity layer.
- Add test coverage for:
  - `WebSocketCodec`: `ReadFrame`/`WriteFrame` round-trip across opcodes,
    payload-length boundaries (7-bit / 16-bit / 64-bit), masked client frames,
    `SendText`/`SendClose`, and the RFC 6455 accept-key computation.
  - `JsonWriter`/`JsonReader`: escaping, nested objects/arrays, `Build()`
    idempotency, and writer→reader round-trip for strings/ints/bools.
  - `DeltaEngine`: first-message-is-full-state, changed-only field/ally/enemy
    diffs, removals, sequence-number monotonicity, and per-session isolation.
  - `SessionManager`: claim/release, authorization, librarian lock ownership,
    `TranslateUnitIds`, rename, and `BuildPlayerListJson` output.

## Capabilities

### New Capabilities

- `mod-test-suite`: The mod's transport/protocol layer (WebSocket codec, JSON
  reader/writer, delta engine, session/ownership manager) SHALL be unit-testable
  in isolation from the Unity runtime, and SHALL have automated test coverage of
  its core behaviors that runs via `dotnet test` without game assemblies.

### Modified Capabilities

<!-- None. The ModLog and broadcast-hook refactors are implementation details
     that preserve existing runtime behavior; no wire-contract or session spec
     requirements change. -->

## Impact

- **New:** `mod/mod.tests/` (test project + test files), `mod/ModLog.cs`.
- **Modified (no behavior change):** `mod/SessionManager.cs` (Debug + broadcast
  hook), `mod/WebSocketClient.cs` (Debug), `mod/Initializer.cs` (wire ModLog +
  broadcast hook defaults).
- **Build:** new `dotnet test` entry point. The main `mod/` build is unaffected
  (still `dotnet build`, expecting 0 Warning / 0 Error). The test project does
  not reference `Assembly-CSharp.dll`.
- **Out of scope:** `GameStateSerializer`, `Server`, `ActionInjector`,
  `StateBroadcaster`, caches, and probes — all bound to live Unity objects and
  covered by in-game smoke testing instead.
- **Prerequisite:** the .NET Framework 4.8 targeting pack (already required by
  the existing `dotnet build`).
