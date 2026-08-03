## Why

When the host enters a cutscene, remote players get nothing. The frontend has no `story`
scene view at all — `scene === "story"` falls through to the generic `.scene-idle`
placeholder that renders the literal word "story" — and mid-battle cutscenes (the ones
routed through `BattleStoryUI`) simply freeze the battle stage with no indication of what
is being said.

This is worse than it sounds for co-op. The host controls the click-through pace, so even
a host who is being considerate cannot let four other people read at their own speed. The
players who are not driving experience story beats as a pause of unexplained length.

The base game already solves the read-at-your-own-pace problem with a dialogue log
(`DialogLogManager`, reachable from the story screen's log button). Mirroring that log to
the web UI is a small, well-bounded piece of work because the game hands us the data
already assembled: `DialogLogManager.dialogDataList` is a public `List<CharacterDialogLogData>`
appended through public `AddDialog` / `AddExtraLog` methods and cleared per episode by
`Init`.

## What Changes

- New `mod/StoryLog.cs` — a static, Unity-free store holding the current episode's log
  entries plus the two pure text helpers the feature needs (`StripRichText`,
  `SlugifyPortraitKey`). Its append API takes primitives only, so the file links into
  `mod/mod.tests/` as a `Compile` item and gets real xUnit coverage without any game
  assemblies on the load path.
- New Harmony patches in `mod/StateBroadcaster.cs`, mirroring vanilla's own lifecycle:
  - `DialogLogManager.Init` postfix → `StoryLog.Clear()`. This is what `StoryManager.InitDialogs`
    calls, so it is exactly vanilla's per-episode boundary.
  - `DialogLogManager.AddDialog` / `AddExtraLog` postfix → append + `Broadcast()`.
  - `StoryRoot.EndStory` and `BattleStoryUI.EndStory` postfix → `StoryLog.Clear()` + `Broadcast()`.
  - Because `StoryRoot` and `BattleStoryUI` both drive a `StoryManager`, and both route
    through the same `DialogLogManager` API, one set of patches covers standalone cutscenes
    and mid-battle cutscenes alike.
- New optional top-level `storyLog` array on the state payload, present only while a
  cutscene is live. Each entry carries `teller`, `title`, `content`, `portrait`, and for
  choice-outcome rows `isChoice` / `choiceIsRed`.
- `IconCache` gains a fourth extraction target (`EnsurePortrait` → `wwwroot/assets/portraits/`)
  reusing the existing `EnsureSprite` primitive. Extraction is lazy: a portrait is written
  the first time its `Model` key is seen, not in an upfront pass.
- New `frontend/app/components/story/LogPanel.vue`, used in two places: as the whole view
  for `scene === "story"`, and as a collapsible overlay above the battle stage whenever
  `storyLog` is present during `scene === "battle"`.

## Capabilities

### New Capabilities

- `cutscene-story-log`: the contract for capturing the game's dialogue log and mirroring it
  to connected clients — lifecycle (when the log exists and when it is cleared), entry
  shape, text normalization, portrait extraction, and the two frontend surfaces.

### Modified Capabilities

- `wire-contract-schema`: admits the new optional top-level `storyLog: StoryLogEntry[]` on
  `GameStateSchema`, and the new `StoryLogEntrySchema`.

## Impact

- **C#**: new `StoryLog.cs` (~90 lines, no Unity types). Five Harmony patch classes in
  `StateBroadcaster.cs`. One new `EnsurePortrait` one-liner plus a directory property in
  `IconCache.cs`. One `WriteStoryLog` writer called from `BuildJson`, emitted for every
  scene rather than inside a single scene writer, since a cutscene can overlay a battle.
  No new project references.
- **Frontend**: `StoryLogEntrySchema` + `storyLog` in `types/game.ts`; regenerate
  `schema/gamestate.schema.json`. New `story/LogPanel.vue`. `app.vue` routes
  `scene === "story"` to it; `battle/Stage.vue` mounts it as an overlay.
- **Tests**: new `mod/mod.tests/StoryLogTests.cs` covering the tag stripper and the portrait
  slugger. New `LogPanel` Vitest suite. New `story-cutscene.json` dev fixture, picked up
  automatically by the existing `fixtures.test.ts` schema validation.
- **Assets**: portraits accumulate in `wwwroot/assets/portraits/`, bounded by the number of
  distinct speakers the session actually encounters.

## Non-Goals

- **Rich-text rendering.** v1 strips Unity markup (`<color=…>`, `<size=…>`) from `Content`
  rather than translating it. Recorded as a stretch goal below rather than dropped, because
  `KeywordText.vue` already splits a string into typed segments and Unity markup would
  follow the same shape — stripping is a stepping stone, not something to tear out.
- **Log persistence past the cutscene.** Deliberately matches vanilla: the log exists while
  the cutscene is on screen and disappears when it closes.
- **Voice playback**, and the per-entry voice-replay affordance vanilla's log offers.
- **Skip / advance control from the web UI.** This is a read-only mirror; the host still
  drives the cutscene.
