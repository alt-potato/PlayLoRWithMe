# Tasks

## 1. Decouple logging via ModLog facade

- [x] 1.1 Add `mod/ModLog.cs`: Unity-free `internal static` class with
  `Action<string> Info` and `Action<string> Warn` defaulting to no-op.
- [x] 1.2 In `Initializer.OnInitializeMod`, wire `ModLog.Info = Debug.Log` and
  `ModLog.Warn = Debug.LogWarning` before `_server.Start()`.
- [x] 1.3 Add `ModLog.cs` to the `mod.csproj` `<Compile>` ItemGroup.
- [x] 1.4 Replace the 7 `Debug.Log/LogWarning` calls in `WebSocketClient.cs`
  with `ModLog.Info/Warn`; replace the 1 `Debug.Log` in `SessionManager.cs`.
  (6 calls in `WebSocketClient`, not 7 — doc overcount.)
- [x] 1.5 `cd mod && dotnet build` → 0 Warning / 0 Error. Commit.

## 2. Decouple session-change broadcast via hook

- [x] 2.1 Add `public static Action OnSessionsChanged = () => { };` to
  `SessionManager` with a doc comment explaining the wiring contract.
- [x] 2.2 Replace the 2 `StateBroadcaster.Broadcast()` calls in
  `SessionManager` (`Attach`/`Detach`) with `OnSessionsChanged()`.
- [x] 2.3 In `Initializer`, set
  `SessionManager.OnSessionsChanged = StateBroadcaster.Broadcast` at startup.
- [x] 2.4 Confirm `SessionManager.cs` no longer references `StateBroadcaster`,
  `UnityEngine`, or any game type (grep). `dotnet build` green. Commit.

## 3. Test project scaffold

- [ ] 3.1 Create `mod/mod.tests/mod.tests.csproj` (SDK-style, `net48`) with
  xUnit + xunit.runner.visualstudio + Microsoft.NET.Test.Sdk PackageReferences.
- [ ] 3.2 Add linked `<Compile Include="..\X.cs" Link="src\X.cs" />` items for
  `JsonReader`, `JsonWriter`, `WebSocketCodec`, `WebSocketClient`,
  `SessionManager`, `DeltaEngine`, `ModLog`.
- [ ] 3.3 Add one trivial smoke test; run `dotnet test` from `mod/mod.tests/` and
  confirm it builds and runs with no `Assembly-CSharp`/Unity/Harmony reference.
  Commit.

## 4. WebSocketCodec tests

- [ ] 4.1 `WriteFrame`→`ReadFrame` round-trip over a shared `MemoryStream` for
  payload lengths {0, 125, 126, 65535, 65536}, asserting opcode + bytes.
- [ ] 4.2 Decode a hand-built masked client frame; assert unmasked payload.
- [ ] 4.3 `SendText` and `SendClose` produce the expected opcode and payload
  (UTF-8 text; close status code).
- [ ] 4.4 Accept-key derivation matches the RFC 6455 canonical example
  (`dGhlIHNhbXBsZSBub25jZQ==` → `s3pPLMBiTxaQ9kYGzzhZRbK+xOo=`). Expose the
  computation as `internal` if needed for direct assertion. `dotnet test` green.
  Commit.

## 5. JsonWriter / JsonReader tests

- [ ] 5.1 Escaping: quotes, backslashes, and control characters produce valid
  JSON escape sequences.
- [ ] 5.2 Nested objects/arrays are balanced and parse back to the same shape.
- [ ] 5.3 `Build()` called twice returns the identical string with no duplicated
  closing braces.
- [ ] 5.4 `JsonWriter` → `JsonReader` round-trip recovers string and int values
  (`GetString`, `TryGetInt`). `dotnet test` green. Commit.

## 6. DeltaEngine tests

- [ ] 6.1 First `BuildMessage` for a fresh session emits full state.
- [ ] 6.2 Second message contains only changed fields/allies/enemies.
- [ ] 6.3 An ally/enemy absent from the new state is reported as removed.
- [ ] 6.4 Sequence numbers strictly increase per session.
- [ ] 6.5 Two sessions at different stream positions each diff against their own
  last-seen state. `dotnet test` green. Commit.

## 7. SessionManager tests

- [ ] 7.1 Claim then `IsAuthorized` true; `ReleaseUnit` then `IsAuthorized`
  false.
- [ ] 7.2 Librarian lock is exclusive: second locker fails; holder-name queries
  return the first session.
- [ ] 7.3 `TranslateUnitIds` remaps existing claims via the old→new map.
- [ ] 7.4 `RenameSession` updates the display name; `BuildPlayerListJson`
  reflects names and claimed units. `dotnet test` green. Commit.

## 8. Finalize

- [ ] 8.1 Document `dotnet test` (from `mod/mod.tests/`) in CLAUDE.md alongside
  the existing build instructions; note the suite needs no game assemblies.
- [ ] 8.2 Full `cd mod && dotnet build` (0 Warning / 0 Error) + `dotnet test`
  (all green). In-game smoke test: logging and player-list/connection broadcasts
  behave as before. Commit.
