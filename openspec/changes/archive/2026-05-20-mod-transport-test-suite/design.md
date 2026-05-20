# Design

## Context

The mod compiles as a single old-style (non-SDK) `.csproj` targeting .NET 4.8,
referencing the game's `Assembly-CSharp.dll`, Harmony, and several Unity
assemblies via `HintPath`. Those DLLs are not redistributable, so any build that
references them cannot run on a stock CI runner and cannot be exercised outside
the game process.

The transport/protocol files are nearly free of that coupling already. Their
*only* remaining ties to Unity are:

- `WebSocketClient.cs` — 7 `UnityEngine.Debug.Log/LogWarning` calls.
- `SessionManager.cs` — 1 `UnityEngine.Debug.Log` call **and** 2 direct
  `StateBroadcaster.Broadcast()` calls (the Harmony/Unity layer).

`JsonReader`, `JsonWriter`, `WebSocketCodec`, and `DeltaEngine` have zero Unity
references today. Severing the two coupling points above makes the whole set
compilable against the bare BCL.

## Goals / Non-Goals

**Goals**
- A `dotnet test` suite that runs with no game assemblies on the load path.
- Cover codec framing, JSON round-trip, delta diffing, and session/ownership
  logic — the silent-regression-prone wire layer.
- Keep runtime behavior byte-for-byte identical after the decoupling refactor.

**Non-Goals**
- Testing `GameStateSerializer`, `Server`, `ActionInjector`, `StateBroadcaster`,
  caches, or probes (bound to live Unity objects — out of scope).
- Mocking game types (`BattleUnitModel`, `StageController`) — not feasible;
  they are concrete, often sealed, interface-free classes.
- An in-engine (PlayMode) test harness — LoR ships none.

## Decisions

### 1. Test project compiles the source files directly (no project reference)

The test project is **SDK-style**, targets `net48`, uses **xUnit**, and pulls in
the production code via linked `Compile` items rather than a `ProjectReference`:

```xml
<ItemGroup>
  <Compile Include="..\JsonReader.cs"      Link="src\JsonReader.cs" />
  <Compile Include="..\JsonWriter.cs"      Link="src\JsonWriter.cs" />
  <Compile Include="..\WebSocketCodec.cs"  Link="src\WebSocketCodec.cs" />
  <Compile Include="..\WebSocketClient.cs" Link="src\WebSocketClient.cs" />
  <Compile Include="..\SessionManager.cs"  Link="src\SessionManager.cs" />
  <Compile Include="..\DeltaEngine.cs"     Link="src\DeltaEngine.cs" />
  <Compile Include="..\ModLog.cs"          Link="src\ModLog.cs" />
</ItemGroup>
```

**Why direct compile, not ProjectReference:** referencing the main `mod.csproj`
would transitively require `Assembly-CSharp.dll`, defeating the CI goal.
Compiling only the pure files into the test assembly keeps the dependency set at
the BCL. As a side benefit, the production types are `internal` — same-assembly
compilation grants the tests access without `InternalsVisibleTo`.

**Trade-off:** the file list is maintained by hand. If a tested file gains a new
Unity dependency, the test build breaks loudly — which is the desired guardrail,
not a hazard. The list is small and stable.

### 2. `ModLog` logging facade

New `mod/ModLog.cs`, Unity-free, with swappable delegates defaulting to no-op:

```csharp
internal static class ModLog
{
    // wired to UnityEngine.Debug.Log / .LogWarning by Initializer at startup;
    // left as no-ops under test so no Unity runtime is touched.
    public static Action<string> Info = _ => { };
    public static Action<string> Warn = _ => { };
}
```

`Initializer.OnInitializeMod` sets `ModLog.Info = Debug.Log;` and
`ModLog.Warn = Debug.LogWarning;` before `_server.Start()`. All `Debug.*` calls
in `SessionManager` (1) and `WebSocketClient` (7) become `ModLog.Info/Warn`.
Other files keep calling `Debug` directly — they are not compiled into tests.

**Why default to no-op rather than `Console.WriteLine`:** the default must carry
zero Unity dependency *and* be safe in a headless test process; a no-op is the
minimal correct default, and tests that want to assert on log output can set the
delegate locally.

### 3. Broadcast-notify hook on `SessionManager`

`SessionManager`'s two `StateBroadcaster.Broadcast()` calls (in `Attach` and
`Detach`, when connection state changes) become a swappable hook:

```csharp
// raised when session/connection state changes in a way clients must see.
// wired to StateBroadcaster.Broadcast by the entry point; no-op under test.
public static Action OnSessionsChanged = () => { };
```

`Initializer` sets `SessionManager.OnSessionsChanged = StateBroadcaster.Broadcast;`.
This removes `SessionManager`'s only hard reference to the Harmony/Unity layer.

**Why a static `Action` rather than an instance event or constructor injection:**
`SessionManager` is effectively a singleton owned by `Server`, the existing call
sites are static, and a static hook is the smallest change that preserves call
order. An instance event would ripple through construction with no test benefit.

### 4. xUnit, targeting net48

xUnit runs on .NET Framework 4.8 and is the most common modern choice. Targeting
`net48` (not a newer TFM) keeps the test runtime identical to the Unity Mono
target, so behaviors like integer overflow, string handling, and
`System.Net`/`Stream` semantics match production. Requires the .NET Framework
4.8 targeting pack, already present for the existing `dotnet build`.

## Test strategy

| Area | Key cases |
|------|-----------|
| `WebSocketCodec` | `WriteFrame`→`ReadFrame` round-trip over a `MemoryStream` for payload lengths {0, 125, 126, 65535, 65536}; masked client frame decode; `SendText`/`SendClose` opcode+payload; accept-key vs RFC 6455 canonical example |
| `JsonWriter`/`JsonReader` | escaping (quotes/backslash/control chars); nested object+array balance; `Build()` idempotency; writer→reader value round-trip |
| `DeltaEngine` | first message full; changed-only fields/allies/enemies; removals reported; per-session monotonic seq; two-session isolation |
| `SessionManager` | claim/release ↔ `IsAuthorized`; exclusive librarian lock + holder name; `TranslateUnitIds` remap; `RenameSession`; `BuildPlayerListJson` content |

Frames are exercised through a shared in-memory stream, so no sockets or game
context are needed. Codec accept-key and JSON tests are deterministic. Session
tests drive only the methods that take primitives (`GetOrCreate`, `ClaimUnit`,
lock APIs, `TranslateUnitIds`, `BuildPlayerListJson`); no real `WebSocketClient`
is constructed.

## Risks / Trade-offs

- **Hand-maintained `Compile` list (low):** mitigated — a new Unity dependency
  breaks the test build immediately, which is the guardrail we want.
- **net48 `dotnet test` needs the targeting pack (low):** already a prerequisite
  of the current build; documented in the proposal.
- **Refactor must not change behavior (medium):** mitigated by keeping the
  `ModLog`/hook wiring in `Initializer` *before* `Server.Start()`, preserving the
  exact pre-refactor log output and broadcast timing; verified by the existing
  `dotnet build` (0 Warning / 0 Error) plus an in-game smoke check.

## Migration / Rollout

1. Add `ModLog.cs`; wire defaults in `Initializer`. (`dotnet build` green.)
2. Swap `Debug.*` → `ModLog.*` in `WebSocketClient` and `SessionManager`.
3. Add `OnSessionsChanged` hook; wire in `Initializer`; replace the two
   `StateBroadcaster.Broadcast()` calls. (`dotnet build` green.)
4. Add the test project and tests; `dotnet test` green.
5. In-game smoke test confirms logging and broadcast behavior unchanged.

No rollback concerns: the refactor is internal and the test project is additive.
