## ADDED Requirements

### Requirement: The mod SHALL mirror the game's dialogue log for the duration of a cutscene

The mod MUST maintain a store of the current cutscene episode's dialogue entries, populated
from the game's own `DialogLogManager` so that the mirrored contents match what the host sees
when opening the in-game log button.

The store MUST be populated by Harmony postfixes on `DialogLogManager.AddDialog` and
`DialogLogManager.AddExtraLog`, and cleared by a postfix on `DialogLogManager.Init`. Because
`StoryScene.StoryRoot` and `BattleStoryUI` both drive a `StoryScene.StoryManager` that routes
through the same `DialogLogManager` API, one set of patches MUST cover standalone cutscenes and
mid-battle cutscenes alike, with no surface-specific branching in the capture layer.

Vanilla appends a line to its log when the line begins its typewriter reveal, not when the
reveal completes. The mirror MUST NOT attempt to defer past that point: matching vanilla's
timing satisfies the requirement that only already-shown dialogue is exposed, because the
partially-revealed line is by definition already on the host's screen.

The store MUST NOT be populated from any source other than these patches. In particular the
mod MUST NOT read ahead into the episode's remaining dialogue.

#### Scenario: A cutscene line is spoken

- **WHEN** `DialogLogManager.AddDialog` runs for a dialogue line
- **THEN** a corresponding entry is appended to the mod's store
- **AND** a state broadcast is pushed to all connected sessions

#### Scenario: A story choice resolves

- **WHEN** `DialogLogManager.AddExtraLog(text, isRed)` runs
- **THEN** a choice entry carrying `text` and `isRed` is appended to the store
- **AND** a state broadcast is pushed to all connected sessions

#### Scenario: Unseen dialogue is never exposed

- **WHEN** a cutscene episode has lines remaining that the host has not yet advanced to
- **THEN** those lines are absent from the store
- **AND** they are absent from every state payload sent to clients

### Requirement: The log's lifetime SHALL match vanilla's

The log MUST exist only while a cutscene is on screen. It MUST be cleared on
`DialogLogManager.Init` — the per-episode reset that `StoryManager.InitDialogs` performs — and
again when the cutscene closes, via postfixes on `StoryRoot.EndStory` and
`BattleStoryUI.EndStory`.

The mod MUST NOT retain a finished episode's log into the following battle, library screen, or
subsequent cutscene. Clearing on cutscene close MUST trigger a state broadcast so clients drop
the panel promptly rather than holding stale text until the next unrelated push.

#### Scenario: A new episode begins

- **WHEN** `DialogLogManager.Init` runs at the start of an episode
- **THEN** the store is emptied
- **AND** the first line of the new episode is the only entry after its first `AddDialog`

#### Scenario: A cutscene closes

- **WHEN** `StoryRoot.EndStory` or `BattleStoryUI.EndStory` runs
- **THEN** the store is emptied
- **AND** a state broadcast is pushed
- **AND** subsequent state payloads omit the `storyLog` field entirely

### Requirement: Entry text SHALL be normalized before transport

The mod MUST strip Unity rich-text markup from each captured line before appending it to the
store, MUST leave angle brackets that do not form a tag intact so that prose is not corrupted,
MUST tolerate an unclosed tag without discarding the remainder of the line, MUST return an
empty string for null input, and MUST preserve embedded newlines rather than stripping or
collapsing them.

The normalization is needed because `Dialog.Content` is assigned to a rich-text-enabled legacy
uGUI `Text`, so it may carry colour, size, or bold markup, while the frontend renders entry
text as plain interpolated content — unstripped markup would appear literally as visible
angle-bracket tags.

Newlines are meaningful line breaks in the source script and the frontend renders them, so
they survive both capture and JSON encoding.

#### Scenario: A line carries colour markup

- **WHEN** a `Content` value of `"<color=#ff0000>Stop.</color>"` is captured
- **THEN** the stored entry's content is `"Stop."`

#### Scenario: Prose contains a non-tag angle bracket

- **WHEN** a `Content` value containing a comparison such as `"5 < 7"` is captured
- **THEN** the angle bracket survives into the stored entry

#### Scenario: A line carries embedded newlines

- **WHEN** a `Content` value containing `\n` is captured
- **THEN** the newline survives into the stored entry
- **AND** it survives JSON encoding as an escaped `\n`

### Requirement: Speaker portraits SHALL be extracted lazily under ASCII-safe names

The mod MUST extract each speaker's portrait sprite to `wwwroot/assets/portraits/`, reusing
`IconCache`'s existing sprite-extraction primitive, and MUST do so lazily — the first time a
given model key is encountered — rather than in an upfront pass over the portrait directory.

The mod MUST derive both the on-disk filename and the wire value from an ASCII-safe slug,
because model keys are not guaranteed to be ASCII. The slug maps every character outside
`[A-Za-z0-9_-]` to `_` and appends a short hex hash of the original UTF-8 bytes. It MUST be
deterministic across sessions, and two distinct model keys that sanitize to the same form MUST
produce distinct slugs. The raw model key MUST NOT appear in any state payload.

When the sprite cannot be loaded, the entry MUST be emitted with no portrait value rather than
being dropped or failing the broadcast. Failed lookups MUST be remembered so that a speaker with
no portrait does not trigger a load attempt on every one of their lines.

Each `Dialog` carries the model key that resolves the portrait, via a `Resources.Load` call
against `StoryResource/CharacterPortraits/` suffixed with that key.

#### Scenario: A speaker's first line is captured

- **WHEN** a `Model` key not seen before this session is captured and its sprite loads
- **THEN** the sprite is written to `wwwroot/assets/portraits/<slug>.png`
- **AND** the entry carries `<slug>` as its portrait value

#### Scenario: The same speaker speaks again

- **WHEN** a subsequent line carries a `Model` key already extracted this session
- **THEN** no further extraction is performed
- **AND** the entry carries the same slug

#### Scenario: A speaker has no portrait asset

- **WHEN** `Resources.Load` returns null for a `Model` key
- **THEN** the entry is still appended, with no portrait value
- **AND** repeat lines from that speaker do not retry the load

#### Scenario: A non-ASCII model key is captured

- **WHEN** a `Model` key containing non-ASCII characters is captured
- **THEN** the written filename and the wire value contain only `[A-Za-z0-9_-]`
- **AND** the same key produces the same slug on a later session

### Requirement: The log SHALL be presented on both the story scene and the battle stage

The frontend MUST render the log through a single component used on two surfaces: as the view
for `scene === "story"`, replacing the generic scene placeholder that renders the literal word
"story" today; and as an overlay above the battle stage whenever `storyLog` is present during
`scene === "battle"`, which is how a `BattleStoryUI` cutscene reports itself.

The battle overlay MUST be collapsible. `BattleStoryUI.OpenStory` accepts a `blockBattle`
parameter that is not always true, so an uncollapsible overlay could hide a stage the host can
still act on. It MUST default to open, since the blocking case is the common one.

The battle overlay MUST be fully opaque. A cutscene blocks combat input, so there is nothing
actionable to read through the panel, and a translucent one only makes the dialogue harder to
read against the stage behind it. Collapsing is the means of seeing the stage.

The panel MUST be read-only. It MUST NOT offer skip, advance, or any other control over the
host's cutscene playback.

#### Scenario: The host enters a standalone cutscene

- **WHEN** the state payload reports `scene: "story"` with a populated `storyLog`
- **THEN** the log panel is the main view
- **AND** the generic scene placeholder is not shown

#### Scenario: A cutscene interrupts a battle

- **WHEN** the state payload reports `scene: "battle"` with a populated `storyLog`
- **THEN** the log panel is shown as an overlay above the battle stage
- **AND** the overlay starts expanded
- **AND** the overlay is fully opaque
- **AND** the viewer can collapse it to see the stage beneath

#### Scenario: A cutscene begins before any line is spoken

- **WHEN** the state payload reports `scene: "story"` with no `storyLog` field
- **THEN** an empty state is shown rather than an empty panel or a blank screen

### Requirement: Entry presentation SHALL follow the in-game log's conventions

Rows MUST mirror `CharacterDialogLog`'s layout: portrait on the left, then a name line
rendering `title` smaller and before `teller` — vanilla composes it as
`"<size=25>" + Title + "</size>  <size=36>" + Teller + "</size>"` — then the content beneath.

Two vanilla rules MUST carry over. A `teller` of `"Monologue"` MUST render no name line at all.
Choice entries MUST render centered with a red or blue accent selected by `choiceIsRed`.

Content MUST render with `white-space: pre-line` so the newlines preserved in transport are
displayed, consistent with the convention already applied to passive and card descriptions.

Title and speaker MUST share the same display typeface, differing in size only. Vanilla
composes the whole name line from one string and varies the size tag alone, so a differing
typeface would not mirror it.

Dialogue text, speaker names, and titles MUST all render in a reading serif rather than the
UI's display face. The game renders story text in a serif and swaps only the localized font,
never to a sans face — but the display face is Trajan-derived, so its lowercase reads as small
capitals, which suits short headings and misrepresents both dialogue and character names.

The name line MUST be a reserved row rather than a conditional one. Rows that render no name
into it — monologue rows — MUST still occupy it, so their content sits at the same height as
every other row's, as the in-game layout does.

The name line MUST carry a visual separator in the interface's gold accent. The base game
tabs a trapezoid into the top of the portrait hexagon and runs it the length of the name; that
shape depends on the name plate abutting the portrait, which does not hold here because the
name occupies its own column, so an equivalent gold rule MAY be substituted.

Dialogue MUST additionally be bounded to a comfortable reading measure, independent of the
panel's own width cap. The cap alone does not deliver one: it sits near a typical laptop
viewport, so lines still run the full width of the screen. Long unbroken runs MUST wrap rather
than force horizontal scrolling.

Portraits MUST be framed in a pointy-top hexagon, matching how the in-game log crops them. The
frame MUST be built from two clipped layers rather than a CSS border, because a clip path cuts
a real border away; the outer layer's fill stands in as the outline. That outline MUST use the
interface's gold accent, matching the base game. The frame MUST be sized on both axes, since a
pointy-top hexagon is taller than it is wide.

The crop MUST be biased toward the speaker's face rather than fitting the whole bust. Portrait
art is a head-and-shoulders bust whose face centres at roughly a third of the image height, so
a plain cover fit lets the hexagon's lower point cut through the chest. The zoom and vertical
bias MUST be expressed as adjustable custom properties, because the game's true framing lives
in prefab data that cannot be decompiled and these values are therefore tuned approximations.

Every spoken row MUST reserve its portrait frame, including rows whose speaker has no portrait
asset and rows whose portrait image fails to load. The in-game log disables only the image and
leaves its hexagon standing, and reserving it keeps the text column aligned down the list. A
failed image MUST leave an empty frame rather than a broken-image placeholder. Choice rows MUST
NOT reserve a frame — vanilla renders them through a separate slot that has none.

#### Scenario: A monologue line is rendered

- **WHEN** an entry has `teller` equal to `"Monologue"`
- **THEN** no name line is rendered for that row
- **AND** the content is still rendered

#### Scenario: A choice outcome is rendered

- **WHEN** an entry has `isChoice` true and `choiceIsRed` true
- **THEN** the row renders centered with the red accent
- **AND** an entry with `choiceIsRed` false renders with the blue accent

#### Scenario: A speaker has both a title and a name

- **WHEN** an entry carries both `title` and `teller`
- **THEN** both render in the same reading serif
- **AND** the title renders smaller and ahead of the name
- **AND** the name line carries a gold separator rule

#### Scenario: A monologue row sits among speaker rows

- **WHEN** a monologue entry renders between two rows that have speakers
- **THEN** its name line is still occupied, rendering no name text
- **AND** its content sits at the same height as the surrounding rows' content

#### Scenario: A long passage is rendered on a wide display

- **WHEN** an entry's content is long enough to exceed a comfortable line length
- **THEN** the text wraps at the reading measure rather than at the panel's width cap

#### Scenario: A portrait is rendered

- **WHEN** an entry's portrait asset loads
- **THEN** it is clipped to a pointy-top hexagon
- **AND** the hexagonal outline is drawn by a second clipped layer behind it, not a CSS border

#### Scenario: A portrait image fails to load

- **WHEN** an entry's portrait asset returns 404
- **THEN** the row keeps its empty hexagonal frame
- **AND** no broken-image placeholder is visible

#### Scenario: A speaker has no portrait at all

- **WHEN** a spoken entry carries no portrait value
- **THEN** the row still reserves its empty hexagonal frame
- **AND** the text column stays aligned with rows that do have portraits

#### Scenario: A choice row is rendered

- **WHEN** an entry is a story-choice outcome
- **THEN** no portrait frame is reserved for it

### Requirement: The panel SHALL match the game's log width and follow new lines

The panel MUST cap its width at the width of the game's normal dialogue box, which
`StoryManager` declares as `origintextdialogsize` (`1277f` wide). That value MUST be declared as
a CSS custom property rather than an inline literal.

The cap MUST NOT be taken from `DialogLogManager.slotWidth` (`1500f`). That figure sizes the
rows of the log-history overlay, a different surface from the one a player reads during a
cutscene; this panel stands in for the normal view and so takes the normal view's measure.

The cap is a ceiling only. Below it the panel MUST fill the width available to it, and MUST NOT
be scaled to the game's canvas proportion — reserving proportional margins would waste space on
narrow viewports, which the mobile-first frontend cannot afford.

The panel MUST scroll with the newest entry at the bottom and MUST auto-scroll to the newest
entry as lines arrive. Auto-scroll MUST be suppressed while the viewer has scrolled away from
the bottom, so that reading back through earlier dialogue is not interrupted by the host
advancing — the case this feature exists to serve.

The panel MUST scroll within itself rather than growing the page. Auto-scroll depends on it: an
element that is not itself scrolling silently ignores a scroll position being set, so an
unbounded panel would let the page scroll instead and the newest line would never be brought
into view.

#### Scenario: The panel is viewed on a wide display

- **WHEN** the viewport is wider than the game's normal dialogue box
- **THEN** the panel stops growing at `1277px`

#### Scenario: The panel is viewed below the cap

- **WHEN** the viewport is narrower than the game's normal dialogue box
- **THEN** the panel fills the width available to it
- **AND** no proportional side margin is reserved

#### Scenario: A new line arrives while the viewer is at the bottom

- **WHEN** the viewer is scrolled to the newest entry and a new entry arrives
- **THEN** the panel scrolls to show it

#### Scenario: A new line arrives while the viewer is reading back

- **WHEN** the viewer has scrolled up and a new entry arrives
- **THEN** the scroll position is left unchanged

#### Scenario: The log grows past the available height

- **WHEN** enough entries accumulate to exceed the panel's height
- **THEN** the panel scrolls internally
- **AND** the page itself does not grow to accommodate the log

### Requirement: Place captions SHALL be mirrored inline for the story scene

The mod MUST capture the story scene's current location and emit it as an entry flagged
`isPlace`, inserted inline so that a change of location lands between the lines it separates
rather than being reported out of sequence. The story scene displays this location above the
dialogue, and it can change partway through an episode.

A caption MUST be emitted only when the location differs from the last one emitted. The game
holds the current location in a label that every line reads back identically, so an unguarded
capture would emit a caption per line. The record of the last emitted location MUST be reset
whenever the log is cleared, or the first caption of a new episode would be suppressed as a
duplicate of the previous episode's last.

Capture MUST be restricted to the standalone story scene. A mid-battle cutscene drives a
different `StoryManager` whose location label is never populated, so reading it could surface a
stale location during combat. Blank locations MUST NOT produce an entry.

Place captions are interstitials: they MUST NOT reserve a portrait frame or a name row, and
MUST render distinctly from both spoken lines and choice outcomes.

#### Scenario: An episode opens at a location

- **WHEN** the first line of an episode is captured and a location is set
- **THEN** a `isPlace` entry carrying that location precedes the line

#### Scenario: The location changes mid-episode

- **WHEN** a later line is captured after the location has changed
- **THEN** a new `isPlace` entry is inserted immediately before that line

#### Scenario: The location is unchanged between lines

- **WHEN** successive lines are captured with the location unchanged
- **THEN** no further `isPlace` entry is emitted

#### Scenario: A new episode reopens at the same location

- **WHEN** the log is cleared and a line is captured at the location the previous episode ended at
- **THEN** a `isPlace` entry is emitted for it

#### Scenario: A mid-battle cutscene runs

- **WHEN** dialogue is captured from a cutscene driven by `BattleStoryUI`
- **THEN** no `isPlace` entry is emitted
