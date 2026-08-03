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
- [x] 3.5 Verify in-game that a standalone cutscene and a mid-battle cutscene both populate and
      both clear. This needs a running game — if it cannot be driven from the CLI, stop and ask
      the maintainer to exercise both paths.

## 4. Serializer and wire contract

- [x] 4.1 Emit the `storyLog` array only when the log is non-empty, called from `BuildJson` at
      top level rather than from `WriteStoryScene` — a `BattleStoryUI` cutscene reports
      `scene: "battle"`. The writer lives on `StoryLog.WriteTo` rather than as a
      `WriteStoryLog` helper in the serializer, so it stays inside the tested Unity-free file.
- [x] 4.2 Replace the `WriteStoryScene` no-op placeholder comment, which currently claims the
      story scene emits nothing beyond its scene tag.
- [x] 4.3 Add `StoryLogEntrySchema` to `frontend/app/types/game.ts` and
      `storyLog: z.optional(z.array(StoryLogEntrySchema))` to `GameStateSchema`.
- [x] 4.4 Regenerate `schema/gamestate.schema.json` and extend `schema/reference-state.json`
      with a `storyLog` sample.
- [x] 4.5 `cd frontend && npm test` passes, including the schema drift test.

## 5. Log panel component

- [x] 5.1 Create `frontend/app/components/story/LogPanel.vue` taking `entries: StoryLogEntry[]`.
      Row layout mirrors `CharacterDialogLog`: portrait left, name line (title smaller and
      first, speaker larger and second), content below.
- [x] 5.2 Implement the two vanilla presentation rules: `Teller === "Monologue"` renders no name
      line; choice rows render centered with a red or blue accent per `choiceIsRed`.
- [x] 5.3 Render `content` with `white-space: pre-line`, consistent with the passive and
      card-description convention.
- [x] 5.4 Add `--story-log-max-width: 1277px` as a token and apply it as a ceiling only. Below
      that width the panel fills the space available to it; do not scale it to the game's
      canvas proportion. Corrected after review from `DialogLogManager.slotWidth` (1500, the
      log-history rows) to `StoryManager.origintextdialogsize.x` (1277, the normal dialogue
      box) — this panel stands in for the normal view, so it takes the normal view's measure.
- [x] 5.8 Match the in-game log's name line and portrait framing: title and speaker share the
      display typeface (vanilla varies only the size tag), and portraits are clipped to a
      pointy-top hexagon via a new `--hex-pointy` token, built from two clipped layers since
      `clip-path` cuts a real CSS border away.
- [x] 5.5 Render portraits from `/assets/portraits/<portrait>.png`. Superseded during review:
      rather than falling back to a name-only row, a spoken row keeps its empty hex frame when
      `portrait` is absent or the image fails to load, matching vanilla (which disables the
      image but leaves its frame) and keeping the text column aligned. See 6.6.
- [x] 5.6 Implement newest-at-bottom auto-scroll, suppressed while the reader has scrolled away
      from the bottom.
- [x] 5.7 Add the `LogPanel` Vitest suite per the design's testing table. Split from what the
      design assumed: `@vue/test-utils` is not a project dependency, so the decision logic
      moved into `utils/storyLog.ts` and is unit-tested directly (13 tests), while the
      template/CSS rules it cannot reach — choice accents, `pre-line`, portrait fallback, the
      width ceiling — are asserted against the SFC source, following the precedent
      `HandCard.test.ts` already set (8 tests).

## 6. Surfacing

- [x] 6.1 Route `scene === "story"` in `app.vue` to `LazyStoryLogPanel`, keeping the existing
      `.scene-idle` placeholder as the empty state for a cutscene with no lines logged yet.
- [x] 6.2 Mount the panel as a collapsible overlay in `battle/Stage.vue`, shown whenever
      `storyLog` is present. Defaults to open; collapsing reveals the stage underneath.
- [x] 6.3 Add the `story-cutscene.json` dev fixture covering a dialogue row with a portrait, one
      without, a monologue row, and both choice colours. Confirm `fixtures.test.ts` validates it.
- [x] 6.4 Check the panel at phone width: rows stay legible, the portrait does not crowd out the
      content, and the battle overlay's collapse control stays reachable. Confirmed by the
      maintainer, along with 3.5 (both cutscene types populate and clear in-game).
- [x] 6.6 Post-review fidelity pass against the in-game appearance: make the battle overlay
      fully opaque (a cutscene blocks combat input, so nothing actionable sits behind it),
      render dialogue text in the serif display face, bias the portrait crop onto the face
      rather than fitting the whole bust, and reserve the empty hex frame for speakers with no
      portrait as vanilla does.
- [x] 6.5 `cd mod && dotnet build` runs `0 Warning(s) 0 Error(s)` and `cd frontend && npm test`
      passes.

## 7. Documentation

- [x] 7.1 Add `StoryLog.cs` to the mod file table in `CLAUDE.md`, and `story/LogPanel.vue` to the
      frontend table. Note the new `storyLog` field in the wire-format section.

## 8. Second fidelity pass

- [x] 8.1 Reserve the name row for every spoken line so monologue rows keep their content
      aligned with the rest, instead of collapsing when there is no name to show.
- [x] 8.2 Recolour the portrait hex outline to the interface gold accent, matching the base game.
- [x] 8.3 Separate the name line with a gold hairline rule. The base game tabs a trapezoid into
      the top of the hexagon and runs it the length of the name; that shape depends on the name
      plate abutting the portrait, which does not hold here since the name has its own column.
- [x] 8.4 Reduce the portrait zoom — the head read too large inside the frame.
- [x] 8.5 Bound dialogue to a reading measure (`--story-log-measure`) and allow long unbroken
      runs to wrap. The 1277px panel cap sits near a typical laptop viewport, so on its own it
      left lines running the full width of the screen.
- [x] 8.6 Bound the story-scene scroller (`--story-log-scroll-max-h`) so the panel scrolls
      internally. Unbounded it grew to fit and the page scrolled instead, which silently broke
      auto-scroll: setting scrollTop on a non-scrolling element does nothing. Also set
      `overflow-anchor: auto` so the viewport sticks to the bottom natively.
- [x] 8.7 Replace the display face with a new `--font-serif` reading serif for dialogue, names,
      and titles. Cinzel is Trajan-derived and its lowercase reads as small capitals, which
      suits headings but misrepresents prose and character names. System faces only, so nothing
      new is downloaded and the app stays usable with no route to a font CDN.
- [x] 8.8 Mirror the story scene's place caption as an inline `isPlace` entry, emitted only when
      the location changes and only for the standalone story scene (a mid-battle cutscene drives
      a different `StoryManager` whose label is never populated). Reset the dedupe record on
      clear so a new episode still captions its opening location.
- [x] 8.9 `cd mod && dotnet build` and `cd mod/mod.tests && dotnet test` pass; `cd frontend &&
      npm test` and `npm run check` pass.

## 9. Layout pass

- [x] 9.1 Fill the remaining viewport height so the log reaches the bottom of the page, via a
      `main--fill` flex context scoped to the story scene alone — applying it to `main` outright
      would change the block flow the battle and library scenes rely on.
- [x] 9.2 Move the scrollbar to the page edge: the scroller now spans full width with a centred
      `.slog-inner` column inside it, instead of the whole panel being capped and centred.
- [x] 9.3 Size the column from the reading measure (`measure + portrait + gap`) so a row's text
      spans it edge to edge. Drops `--story-log-max-width` (1277px), which the maintainer
      approved dropping: it sat near a typical laptop viewport, so it never constrained a line
      and left the text far narrower than the panel around it.
- [x] 9.4 Set `font-family` on the panel root so the `ch` in the measure resolves against the
      font the dialogue is actually set in. Otherwise the column is sized from the inherited
      sans's advance width while the text is bounded by the serif's, and the two disagree.
- [x] 9.5 Run the name rule from the hexagon's edge to the end of the name: negative start
      margin reaches back across the row gap, `width: fit-content` stops it at the name. Rows
      with no name keep the reserved height with the rule made transparent.
- [x] 9.6 Shift the portrait crop right via a new `--story-log-portrait-offset-x`.
- [x] 9.7 `cd frontend && npm test` and `npm run check` pass; `cd mod && dotnet build` passes.

## 10. Portrait scale and name colour

- [x] 10.1 Give the title the same colour as the speaker name. In game a single `Text` draws
      both and varies only the size tag, so size is the whole of the distinction.
- [x] 10.2 Scale portraits to the frame's height alone rather than `object-fit: cover`. Measured
      the extracted sprites: they are tight crops with only ~2px transparent margin and native
      aspects from roughly 0.70 to 1.03, so `cover` scaled the narrow ones by width and zoomed
      those heads past the wide ones'. Head size tracks bust height, so fitting height alone
      normalises them.
- [x] 10.3 Pad portrait extraction to the sprite's logical rect
      (`IconCache.SpriteToPaddedPng`), so sprites authored on a shared canvas but trimmed to
      differing bounds keep a common frame of reference. Inert where a sprite is untrimmed,
      which the measurements above suggest is the case here — kept because it is correct by
      construction and costs nothing at runtime.
- [x] 10.4 Log each portrait's rect/crop/offset/pivot geometry once, so whether 10.3 ever
      engages can be confirmed from the player log rather than assumed.
- [x] 10.5 `cd mod && dotnet build`, `cd mod/mod.tests && dotnet test`, `cd frontend && npm test`
      and `npm run check` all pass.
