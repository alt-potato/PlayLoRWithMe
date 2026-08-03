# Design — cutscene story log

## Game-side findings

Decompiled from `Assembly-CSharp.dll` (see `CLAUDE.md` for the `ilspycmd` invocation).

`StoryScene.StoryManager` is the cutscene driver. It is instantiated twice: once under
`StoryRoot` (the standalone `story` scene, reached via `GameSceneManager.ActivateStoryScene`)
and once under `BattleStoryUI` (cutscenes that interrupt a battle). Both share the same
implementation, which is what lets a single set of patches cover both surfaces.

`StoryManager` holds a private `[SerializeField] DialogLogManager dialogLogManager`. The
relevant public surface of `DialogLogManager`:

```csharp
public List<CharacterDialogLogData> dialogDataList;   // public field
public void Init();                                   // clears dialogDataList
public void AddDialog(Dialog _dialog);
public void AddExtraLog(string text, bool isRed);
```

`CharacterDialogLogData` is a discriminated pair: `isDialog == true` carries a
`WorkParser.Dialog`, otherwise it carries `extratext` + `isExtraTextRed`.

```csharp
public class Dialog {
    public int ID;
    public string Model;    // portrait key
    public string Teller;   // speaker name
    public string Title;    // honorific / subtitle
    public string Voice;
    public string Content;  // the line itself
}
```

Lifecycle, traced through `StoryManager`:

- `InitDialogs(int startIdx = 0)` calls `dialogLogManager.Init()` — the per-episode reset.
- `ChangeDialog` calls `dialogLogManager.AddDialog(dialog)` **before** running the
  typewriter reveal, so the log always contains the line currently being revealed. Vanilla
  behaves the same way, so mirroring it satisfies "only dialogue that has already happened"
  without any extra sequencing work.
- `AddExtraLog` is called only from the story-choice paths (four call sites, the
  forgive/kill branches), producing the red/blue outcome rows.

Rendering reference, from `CharacterDialogLog.Init`:

- Portrait: `Resources.Load<Sprite>("StoryResource/CharacterPortraits/" + dialog.Model)`,
  and the image is simply disabled when the load returns null.
- Name line: `"<size=25>" + dialog.Title + "</size>  <size=36>" + dialog.Teller + "</size>"`
  — title first and smaller, speaker second and larger.
- `if (dialog.Teller == "Monologue") NameText.text = string.Empty;` — monologue rows show
  no name at all.
- `DialogText.text = dialog.Content;` assigned straight onto a legacy uGUI `Text`, which has
  rich text enabled.

Layout constants, from `DialogLogManager`'s serialized fields: `slotWidth = 1500f`,
`slotHeight = 220f`, `row = 4`, `column = 1`. On LoR's 1920-wide design canvas that makes a
log-history row 78.125% of canvas width, with four rows visible at a time.

`StoryManager` separately declares the width of the *normal* dialogue box —
`origintextdialogsize = new Vector3(1277f, 230f)`, with the monologue variant
`monotextdialogsize` matching at `1277f` wide. This is the measure a player actually reads
against during a cutscene; the 1500 figure describes the log-history overlay's rows.

## Decisions

### D1 — Capture by patching `DialogLogManager`, not by reflecting into `StoryManager`

`dialogLogManager` is private on `StoryManager`, but `DialogLogManager`'s own append methods
are public on a public type. Patching them avoids reflection entirely and, more importantly,
gives a push trigger: each append is a natural `Broadcast()` point. Reflecting into
`dialogDataList` at serialize time would work but is pull-based, so we would have to poll
for new lines.

Patching `StoryManager.ChangeDialog` instead was considered and rejected: it fires marginally
earlier but would force us to re-derive what `AddDialog` already assembles, and it would miss
the `AddExtraLog` choice-outcome rows entirely.

### D2 — `StoryLog` takes primitives, so it stays testable

`mod/mod.tests/` compiles linked source files directly rather than referencing the mod
project, specifically so it needs no game assemblies and stays CI-runnable. To keep
`StoryLog.cs` eligible for that treatment it must not name `WorkParser.Dialog`,
`UnityEngine.Sprite`, or any other game type.

So the boundary is:

- `StoryLog.cs` — the entry list, `Append(teller, title, content, portrait)`,
  `AppendChoice(text, isRed)`, `Clear()`, `WriteTo(JsonWriter)`, and the pure helpers
  `StripRichText` and `SlugifyPortraitKey`. Unity-free.
- `StateBroadcaster.cs` patches — map `Dialog` → primitives, call `IconCache.EnsurePortrait`,
  then call into `StoryLog`. Unity-bound, untested (consistent with every other patch class).

The two helpers are where bugs will actually hide, and this split is what lets them be
tested at all.

### D3 — Strip Unity rich text mod-side (v1)

`Content` is assigned to a rich-text-enabled `Text`, so it may contain `<color=…>`,
`<size=…>`, `<b>`, and similar. The frontend interpolates entry text as plain content, so
unstripped markup would render literally as visible angle-bracket tags.

v1 strips every `<…>` span. The stretch goal is to parse markup into typed segments instead
— `KeywordText.vue` already does exactly this shape of work for card keywords, so the
upgrade path replaces `StripRichText` with a segmenter and swaps the renderer, without
touching the capture or transport layers.

`Content` also contains real newlines, which are preserved on the wire (`JsonWriter`
escapes `\n` correctly) and rendered with `white-space: pre-line`, matching the convention
already used for passive and card descriptions.

### D4 — Slug portrait keys rather than trusting `Model` as a filename

`Model` values are not guaranteed ASCII — several `dialog.Model == "…"` comparisons in
`StoryManager` decompile to mojibake, indicating CJK string literals. Using them raw as
filenames invites encoding problems on disk and percent-encoding problems in the asset URL.

`SlugifyPortraitKey` maps any character outside `[A-Za-z0-9_-]` to `_` and appends a short
hex hash of the original UTF-8 bytes, guaranteeing an ASCII-safe, collision-free,
deterministic filename. Only the slug goes on the wire; the raw `Model` never leaves the mod.

### D5 — Emit the whole array, not an append protocol

`DeltaEngine` special-cases `allies` / `enemies` and treats every other top-level field as a
whole-value comparison, so `storyLog` resends in full on each new line. A 100-line episode at
roughly 150 characters per line is about 15 KB per push over localhost or LAN — not enough to
justify an append protocol, whose real cost is that late joiners and `resync` would need a
separate full-replay path. Resending whole gives correct history to late joiners for free.

Growth is bounded by the per-episode `Init` clear, so the array cannot grow without limit.

### D6 — Battle overlay is collapsible, not unconditional

`BattleStoryUI.OpenStory(endFunc, nonskip, blockBattle = true)` usually blocks battle input,
which would justify an unconditional full-screen overlay. But `blockBattle` is a parameter and
can be `false`, so the overlay must be dismissible to avoid covering a stage the host can still
act on. It defaults to open, because the common case is a blocking cutscene.

## Component structure

```
mod/StoryLog.cs               entry list + pure text helpers      (linked into mod.tests)
mod/StateBroadcaster.cs       +5 Harmony patch classes
mod/IconCache.cs              +EnsurePortrait / PortraitDir
mod/GameStateSerializer.cs    +WriteStoryLog, called from BuildJson

frontend/app/types/game.ts                        +StoryLogEntrySchema, +storyLog
frontend/app/components/story/LogPanel.vue        the panel (both surfaces)
frontend/app/app.vue                              route scene === "story"
frontend/app/components/battle/Stage.vue          mount as overlay
```

`WriteStoryLog` is called from `BuildJson` at top level rather than from inside
`WriteStoryScene`, because a cutscene can overlay a battle — during a `BattleStoryUI`
cutscene the reported scene is still `battle`.

## Frontend layout

Rows mirror `CharacterDialogLog`: portrait left, name line, content. Vanilla's two special
cases carry over — `Teller === "Monologue"` renders no name, and choice rows render centered
with a red or blue accent per `choiceIsRed`.

Title and speaker share the display face. Vanilla composes both from a single string
(`"<size=25>" + Title + "</size>  <size=36>" + Teller + "</size>"`), so they differ in size,
not typeface.

Dialogue text uses the serif display face, not the body sans. The game renders story text in a
serif and swaps only the localized font.

Portraits are framed in a pointy-top hexagon to match the in-game log's crop, via a
`--hex-pointy` token alongside the existing flat-top `--hex`. The frame is built from two
clipped layers rather than a CSS border, since `clip-path` cuts a real border away — the same
technique `DieRow` already uses for speed dice. The box is sized on both axes because a
pointy-top hexagon is taller than wide.

The crop is zoomed and biased upward rather than a plain `cover` fit. Inspecting the extracted
sprites shows head-and-shoulders busts at roughly 0.75-0.97 aspect whose face centres near a
third of the image height, so `cover` in a taller-than-wide hexagon frames the entire bust and
puts the hexagon's lower point through the chest. `--story-log-portrait-zoom` and
`--story-log-portrait-offset-y` carry the correction.

Those two values are tuned by eye, not derived. `CharacterDialogLog.Init` only assigns
`PortraitImage.sprite`; the RectTransform and Image settings that determine the game's real
framing live in prefab data inside `resources.assets`, which is not decompilable. Keeping them
as custom properties makes the approximation adjustable without hunting through the stylesheet.

The frame is reserved for every spoken row, including speakers with no portrait. Vanilla sets
`PortraitImage.enabled = false` and leaves the surrounding hexagon in place, and matching that
also keeps the text column aligned. Choice rows get no frame — vanilla renders them through
`extraLogRoot`, a separate slot without one.

The battle overlay is fully opaque. A cutscene blocks combat input, so there is nothing
actionable behind the panel and translucency would only cost dialogue legibility against a busy
stage; collapsing is the way to look underneath.

Width uses the game's *normal dialogue box* as a ceiling only: one token,
`--story-log-max-width: 1277px` (`StoryManager.origintextdialogsize.x`). Below that the panel
simply fills the width available to it.

The reference is deliberately not `DialogLogManager.slotWidth` (1500). That figure sizes the
rows of the log-history overlay, which is a different surface from the one a player reads
during a cutscene. Our panel plays the role of the normal view, so it takes the normal view's
measure.

Scaling the panel proportionally to the game's canvas ratio was considered and rejected — on a
phone it would surrender a fifth of an already narrow viewport to margins for no reason. The
ceiling is what carries the "matches the game" intent; the proportion does not.

Scrolling is newest-at-bottom with auto-scroll to latest on each new entry, suppressed while
the reader has scrolled up. This is the one behavior the vanilla log does not need and this
feature does: the entire point is a player reading behind a host who is clicking ahead.

## Testing

| Layer | Coverage |
| --- | --- |
| `mod.tests` (xUnit) | `StripRichText`: nested tags, unclosed tags, angle brackets in prose, empty and null input. `SlugifyPortraitKey`: ASCII passthrough, non-ASCII input, determinism, collision resistance between two keys with the same sanitized form. |
| Vitest | `LogPanel.vue`: monologue name suppression, choice accent for both `choiceIsRed` values, portrait-missing fallback, entry ordering, auto-scroll suppression when scrolled up. |
| Fixtures | `story-cutscene.json` covering a dialogue row with portrait, one without, a monologue row, and both choice colours. Validated against the schema by the existing `fixtures.test.ts`. |
| Schema | Drift test regenerates `schema/gamestate.schema.json`; `reference-state.json` extended with a `storyLog` sample. |
