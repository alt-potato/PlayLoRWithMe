## 1. Mod-side log store and pure helpers

- [x] 1.1 Create `mod/StoryLog.cs` with a `StoryLogEntry` type (`Teller`, `Title`, `Content`,
      `Portrait`, `IsChoice`, `ChoiceIsRed` — all `string`/`bool`, no Unity or game types) and a
      static `StoryLog` class exposing `Append(teller, title, content, portrait)`,
      `AppendChoice(text, isRed)`, `Clear()`, `IsEmpty`, and `WriteTo(JsonWriter)`.
- [x] 1.2 Implement `StoryLog.StripRichText(string)`: removes every `<…>` span, leaves prose
      angle brackets that do not form a tag intact, tolerates unclosed tags, and returns
      `string.Empty` for null input.
- [x] 1.3 Implement `StoryLog.SlugifyPortraitKey(string)`: maps characters outside
      `[A-Za-z0-9_-]` to `_` and appends a short hex hash of the original UTF-8 bytes.
      Deterministic; two distinct keys with the same sanitized form MUST produce distinct slugs.
- [x] 1.4 Register `StoryLog.cs` as a linked `Compile` item in `mod/mod.tests/` alongside the
      other pure sources. Confirm the test project still builds with no game assemblies on the
      load path.
- [x] 1.5 Add `mod/mod.tests/StoryLogTests.cs` covering both helpers per the design's testing
      table, plus append/clear behaviour on the store itself.
- [x] 1.6 `cd mod/mod.tests && dotnet test` passes (53 tests, 24 new).

## 2. Portrait extraction

- [x] 2.1 Add `PortraitDir` (`wwwroot/assets/portraits/`) and
      `EnsurePortrait(Sprite)` to `mod/IconCache.cs`, delegating to the existing `EnsureSprite`
      primitive with a `"portrait"` label.
- [x] 2.2 Add a resolver in the patch layer that loads
      `Resources.Load<Sprite>("StoryResource/CharacterPortraits/" + model)` and returns the
      slug from `EnsurePortrait`, or `null` when the sprite is missing. Cache negative lookups
      so a missing portrait is not re-loaded on every line by the same speaker.
- [x] 2.3 `cd mod && dotnet build` runs `0 Warning(s) 0 Error(s)`. Also registered
      `StoryLog.cs` in `PlayLoRWithMe.csproj`, which lists compile items explicitly rather
      than globbing.

## 3. Harmony capture patches

- [x] 3.1 Add `Patch_DialogLogInit`: postfix on `DialogLogManager.Init` → `StoryLog.Clear()`.
      No broadcast — `InitDialogs` is followed by dialogue appends that will push anyway.
- [x] 3.2 Add `Patch_DialogLogAddDialog`: postfix on `DialogLogManager.AddDialog` → resolve the
      portrait slug, strip rich text from `Content`, append, `Broadcast()`. A `Teller` of
      `"Monologue"` is passed through as-is; the frontend owns that presentation rule.
- [x] 3.3 Add `Patch_DialogLogAddExtraLog`: postfix on `DialogLogManager.AddExtraLog` →
      `StoryLog.AppendChoice(text, isRed)`, `Broadcast()`.
- [x] 3.4 Add `Patch_StoryRootEndStory` and `Patch_BattleStoryEndStory`: postfix on
      `StoryRoot.EndStory` / `BattleStoryUI.EndStory` → `StoryLog.Clear()` + `Broadcast()`, so
      the log disappears when the cutscene closes. `StoryRoot.EndStory` guards its body with
      `(forcely || inGame) && storyUI.activeSelf` and is a no-op when that fails, so the
      postfix re-checks `storyUI.activeSelf` and skips the clear — otherwise a rejected call
      would wipe the log of a cutscene still on screen.
- [ ] 3.5 Verify in-game that a standalone cutscene and a mid-battle cutscene both populate and
      both clear. This needs a running game — if it cannot be driven from the CLI, stop and ask
      the maintainer to exercise both paths.

## 4. Serializer and wire contract

- [x] 4.1 Emit the `storyLog` array only when the log is non-empty, called from `BuildJson` at
      top level rather than from `WriteStoryScene` — a `BattleStoryUI` cutscene reports
      `scene: "battle"`. The writer lives on `StoryLog.WriteTo` rather than as a
      `WriteStoryLog` helper in the serializer, so it stays inside the tested Unity-free file.
- [x] 4.2 Replace the `WriteStoryScene` no-op placeholder comment, which currently claims the
      story scene emits nothing beyond its scene tag.
- [ ] 4.3 Add `StoryLogEntrySchema` to `frontend/app/types/game.ts` and
      `storyLog: z.optional(z.array(StoryLogEntrySchema))` to `GameStateSchema`.
- [ ] 4.4 Regenerate `schema/gamestate.schema.json` and extend `schema/reference-state.json`
      with a `storyLog` sample.
- [ ] 4.5 `cd frontend && npm test` passes, including the schema drift test.

## 5. Log panel component

- [ ] 5.1 Create `frontend/app/components/story/LogPanel.vue` taking `entries: StoryLogEntry[]`.
      Row layout mirrors `CharacterDialogLog`: portrait left, name line (title smaller and
      first, speaker larger and second), content below.
- [ ] 5.2 Implement the two vanilla presentation rules: `Teller === "Monologue"` renders no name
      line; choice rows render centered with a red or blue accent per `choiceIsRed`.
- [ ] 5.3 Render `content` with `white-space: pre-line`, consistent with the passive and
      card-description convention.
- [ ] 5.4 Add `--story-log-max-width: 1500px` as a token and apply it as a ceiling only. Below
      that width the panel fills the space available to it; do not scale it to the game's
      canvas proportion.
- [ ] 5.5 Render portraits from `/assets/portraits/<portrait>.png`, falling back to a name-only
      row when `portrait` is absent or the image fails to load.
- [ ] 5.6 Implement newest-at-bottom auto-scroll, suppressed while the reader has scrolled away
      from the bottom.
- [ ] 5.7 Add the `LogPanel` Vitest suite per the design's testing table.

## 6. Surfacing

- [ ] 6.1 Route `scene === "story"` in `app.vue` to `LazyStoryLogPanel`, keeping the existing
      `.scene-idle` placeholder as the empty state for a cutscene with no lines logged yet.
- [ ] 6.2 Mount the panel as a collapsible overlay in `battle/Stage.vue`, shown whenever
      `storyLog` is present. Defaults to open; collapsing reveals the stage underneath.
- [ ] 6.3 Add the `story-cutscene.json` dev fixture covering a dialogue row with a portrait, one
      without, a monologue row, and both choice colours. Confirm `fixtures.test.ts` validates it.
- [ ] 6.4 Check the panel at phone width: rows stay legible, the portrait does not crowd out the
      content, and the battle overlay's collapse control stays reachable.
- [ ] 6.5 `cd mod && dotnet build` runs `0 Warning(s) 0 Error(s)` and `cd frontend && npm test`
      passes.

## 7. Documentation

- [ ] 7.1 Add `StoryLog.cs` to the mod file table in `CLAUDE.md`, and `story/LogPanel.vue` to the
      frontend table. Note the new `storyLog` field in the wire-format section.
