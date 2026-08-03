## ADDED Requirements

### Requirement: `GameStateSchema` SHALL carry an optional `storyLog` array

`frontend/app/types/game.ts` MUST declare a `StoryLogEntrySchema` and add
`storyLog: z.optional(z.array(StoryLogEntrySchema))` to `GameStateSchema`.

`StoryLogEntrySchema` MUST declare:

- `content: z.string()` — the line text, rich-text markup already stripped by the mod,
  embedded newlines preserved
- `teller: z.optional(z.string())` — the speaker's name; absent on choice entries
- `title: z.optional(z.string())` — the speaker's honorific or subtitle, rendered smaller and
  before `teller`
- `portrait: z.optional(z.string())` — the ASCII slug identifying the extracted portrait under
  `/assets/portraits/<portrait>.png`; absent when the speaker has no portrait asset
- `isChoice: z.optional(z.boolean())` — marks a story-choice outcome row rather than a
  dialogue row
- `choiceIsRed: z.optional(z.boolean())` — the accent colour for a choice row; meaningful only
  when `isChoice` is true
- `isPlace: z.optional(z.boolean())` — marks a place caption recording a change of location.
  Mutually exclusive with `isChoice`, and carries no `teller`, `title`, or `portrait`

The field MUST be optional at the `GameState` level so that every payload outside a cutscene
continues to parse unchanged. The C# serializer MUST emit `storyLog` only when the log is
non-empty, and MUST emit it from the top-level state writer rather than from a scene-specific
writer, because a `BattleStoryUI` cutscene reports `scene: "battle"` while carrying a log.

`DeltaEngine` special-cases only the `allies` and `enemies` arrays; `storyLog` is compared as a
whole value and resent in full whenever it changes. This is intentional — it keeps late joiners
and `resync` correct with no separate replay path, and per-episode clearing bounds the array's
growth.

#### Scenario: Schema parses a payload with a dialogue entry

- **WHEN** `GameStateSchema` parses a payload whose `storyLog` contains
  `{teller: "Roland", title: "Ex-Grade 1 Fixer", content: "...", portrait: "roland_a1b2c3"}`
- **THEN** the parse succeeds and the entry's fields are present as declared

#### Scenario: Schema parses a payload with a choice entry

- **WHEN** `GameStateSchema` parses a payload whose `storyLog` contains
  `{content: "Forgive", isChoice: true, choiceIsRed: false}`
- **THEN** the parse succeeds
- **AND** `teller`, `title`, and `portrait` are `undefined`

#### Scenario: Schema parses a payload with a place caption

- **WHEN** `GameStateSchema` parses a payload whose `storyLog` contains
  `{content: "The Library", isPlace: true}`
- **THEN** the parse succeeds
- **AND** `teller`, `title`, `portrait`, and `isChoice` are `undefined`

#### Scenario: Schema parses a payload outside a cutscene

- **WHEN** `GameStateSchema` parses a payload that omits `storyLog`
- **THEN** the parse succeeds and `state.storyLog` is `undefined`

#### Scenario: A cutscene overlays a battle

- **WHEN** a payload carries both `scene: "battle"` and a populated `storyLog`
- **THEN** the parse succeeds with both fields present

#### Scenario: Regenerated schema artifact includes the new field

- **WHEN** `schema/gamestate.schema.json` is regenerated after the schema change
- **THEN** the artifact contains the `storyLog` definition
- **AND** the drift test passes against the committed artifact
