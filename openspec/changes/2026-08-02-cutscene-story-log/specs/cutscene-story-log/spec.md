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

Portraits MUST be framed in a pointy-top hexagon, matching how the in-game log crops them. The
frame MUST be built from two clipped layers rather than a CSS border, because a clip path cuts
a real border away; the outer layer's fill stands in as the outline. The frame MUST be sized on
both axes, since a pointy-top hexagon is taller than it is wide.

When an entry has no portrait value, or its image fails to load, the row MUST fall back to a
name-only layout rather than reserving empty space or showing a broken image.

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
- **THEN** both render in the display typeface
- **AND** the title renders smaller and ahead of the name

#### Scenario: A portrait is rendered

- **WHEN** an entry's portrait asset loads
- **THEN** it is clipped to a pointy-top hexagon
- **AND** the hexagonal outline is drawn by a second clipped layer behind it, not a CSS border

#### Scenario: A portrait image fails to load

- **WHEN** an entry's portrait asset returns 404
- **THEN** the row renders name and content with no portrait slot
- **AND** no broken-image placeholder is visible

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
