# mod-test-suite Specification

## Purpose
The mod's transport/protocol layer (WebSocket codec, JSON reader/writer, delta
engine, and session/ownership manager) is the highest-risk code for silent
regressions and the least coupled to the Unity runtime. This capability requires
that layer to be unit-testable in isolation from Unity, with automated coverage
of its core behaviors that runs via `dotnet test` without any game assemblies on
the load path.

## Requirements
### Requirement: Transport layer is decoupled from the Unity runtime

The transport/protocol source files SHALL compile and execute without any
reference to `Assembly-CSharp.dll`, Harmony, or any `UnityEngine` assembly. The
affected files are `JsonReader`, `JsonWriter`, `WebSocketCodec`,
`WebSocketClient`, `SessionManager`, and `DeltaEngine`. Logging and broadcast
side effects MUST be expressed through swappable indirections so the Unity
bindings are supplied only by the mod entry point at runtime.

#### Scenario: Pure files build without game assemblies
- **WHEN** the listed source files are compiled into an assembly that references
  only the .NET 4.8 base class library
- **THEN** compilation succeeds with no unresolved `UnityEngine`, Harmony, or
  `Assembly-CSharp` types

#### Scenario: Logging routes through a Unity-free facade
- **WHEN** `SessionManager` or `WebSocketClient` emits a log message
- **THEN** the message is dispatched through the `ModLog` facade's swappable
  delegate
- **AND** with no delegate configured (the test default) the call is a no-op
  that performs no Unity work

#### Scenario: Session-change broadcasts route through a hook
- **WHEN** `SessionManager` performs an operation that previously called
  `StateBroadcaster.Broadcast()` directly
- **THEN** it invokes a swappable broadcast-notify hook instead
- **AND** the Unity entry point wires that hook to `StateBroadcaster.Broadcast`,
  preserving existing in-game behavior

#### Scenario: Runtime behavior is unchanged
- **WHEN** the mod runs inside the game with the entry point wiring the `ModLog`
  delegates and broadcast hook
- **THEN** log output and broadcast timing match the pre-refactor behavior

### Requirement: WebSocket codec round-trip coverage

The test suite SHALL verify that `WebSocketCodec` correctly encodes and decodes
RFC 6455 frames across the payload-length and masking variations the protocol
defines.

#### Scenario: Frame round-trips across length boundaries
- **WHEN** a payload of length in {0, 125, 126, 65535, 65536} is written with
  `WriteFrame` and read back with `ReadFrame` over a shared in-memory stream
- **THEN** the decoded opcode and payload bytes equal the originals

#### Scenario: Masked client frame is unmasked correctly
- **WHEN** a client-style masked frame is supplied to `ReadFrame`
- **THEN** the returned payload is the unmasked plaintext

#### Scenario: Text and close helpers produce valid frames
- **WHEN** `SendText` or `SendClose` writes to a stream
- **THEN** reading the stream yields the corresponding opcode and payload
  (UTF-8 text, or a close status code)

#### Scenario: RFC 6455 accept key is computed per spec
- **WHEN** the handshake accept-key derivation is given the canonical example
  client key from RFC 6455
- **THEN** it returns the spec's expected `Sec-WebSocket-Accept` value

### Requirement: JSON writer and reader coverage

The test suite SHALL verify that `JsonWriter` produces correctly escaped,
well-formed JSON and that `JsonReader` parses values written by `JsonWriter`.

#### Scenario: Special characters are escaped
- **WHEN** a string containing quotes, backslashes, and control characters is
  added via `JsonWriter`
- **THEN** the output contains valid JSON escape sequences

#### Scenario: Nested objects and arrays are well-formed
- **WHEN** a writer builds nested objects and arrays
- **THEN** the result is balanced and parses back to the same structure

#### Scenario: Build is idempotent
- **WHEN** `Build()` is called more than once on the same writer
- **THEN** each call returns the identical string and emits no duplicate
  closing braces

#### Scenario: Reader recovers written values
- **WHEN** a value written with `JsonWriter` is read back with `JsonReader`
  (`GetString`, `TryGetInt`)
- **THEN** the recovered value equals the original

### Requirement: Delta engine coverage

The test suite SHALL verify that `DeltaEngine` emits full state on first message,
emits only changed fields and collection entries thereafter, reports removals,
and maintains monotonic per-session sequence numbers isolated between sessions.

#### Scenario: First message is full state
- **WHEN** `BuildMessage` is called for a session that has received nothing
- **THEN** the message carries the complete state, not a diff

#### Scenario: Subsequent message contains only changes
- **WHEN** a second state differs from the first in a subset of fields, allies,
  or enemies
- **THEN** the emitted delta contains only the changed entries

#### Scenario: Removed collection entries are reported
- **WHEN** an ally or enemy present in the prior state is absent from the new
  state
- **THEN** the delta lists that entry's id as removed

#### Scenario: Sequence numbers increase monotonically per session
- **WHEN** successive messages are built for one session
- **THEN** each carries a strictly greater sequence number than the last

#### Scenario: Sessions are isolated
- **WHEN** two sessions are at different points in the state stream
- **THEN** each receives a delta computed against its own last-seen state

### Requirement: Session manager coverage

The test suite SHALL verify `SessionManager` claim/release, authorization,
librarian lock ownership, unit-id translation, rename, and player-list
serialization behaviors.

#### Scenario: Claim grants authorization; release revokes it
- **WHEN** a session claims a unit and is then queried via `IsAuthorized`
- **THEN** authorization is granted, and revoked again after `ReleaseUnit`

#### Scenario: Librarian lock is exclusive to its holder
- **WHEN** one session holds a librarian lock and another attempts to lock the
  same key
- **THEN** the second attempt fails and lock-holder queries name the first
  session

#### Scenario: Unit-id translation remaps claims
- **WHEN** `TranslateUnitIds` is given an old→new id mapping
- **THEN** existing claims are remapped to the new ids

#### Scenario: Player-list JSON reflects sessions
- **WHEN** `BuildPlayerListJson` is called with known sessions
- **THEN** the output lists each session's display name and claimed units
