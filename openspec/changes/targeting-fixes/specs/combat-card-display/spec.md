## ADDED Requirements

### Requirement: The entire `HandCard` root SHALL be the click target for card selection

Across both `compact` and `full` display modes, a single `@click` handler bound to the `.hcard` root element MUST be the only path that emits the `click` event. No descendant element (preview pane, detail pane, ability paragraph, per-die rows, range icon) may set `@click.stop` or otherwise interrupt click propagation toward the root — clicks anywhere on the rendered card surface MUST reach the root handler and result in a single `click` emit (subject to the existing `unusable` / `readonly` / long-press guards).

The long-press → `detail` emit gesture (driven by `@mousedown` / `@touchstart` plus a setTimeout) MUST remain unaffected by this requirement: it continues to fire when the press exceeds `LONG_PRESS_MS`, regardless of which sub-element the press started on.

#### Scenario: Battle hand — click on detail pane selects the card

- **WHEN** an ally's hand is rendered in `displayMode="full"` and the player clicks anywhere inside the always-visible detail pane (ability text, a die description, the per-die range numbers)
- **THEN** the `click` event is emitted exactly once
- **AND** the parent `Stage.vue` treats the click as a card select (slot-first interaction flow advances)

#### Scenario: Battle hand — click on preview pane selects the card

- **WHEN** an ally's hand is rendered in `displayMode="full"` and the player clicks anywhere inside the preview pane (cost badge, range icon, card name, dice icons, token list)
- **THEN** the `click` event is emitted exactly once
- **AND** behavior is identical to clicking the detail pane

#### Scenario: Compact-mode hover overlay — click selects the card

- **WHEN** a deck-builder hand card is rendered in `displayMode="compact"`, a hover-capable user hovers the card to reveal the detail-pane overlay, and clicks on the overlay
- **THEN** the `click` event is emitted exactly once on the parent
- **AND** the parent treats the click as a card select (matching a click on the preview pane in the same mode)

#### Scenario: Long-press on detail pane still opens CardDetail

- **WHEN** a user presses and holds anywhere on the detail pane for at least `LONG_PRESS_MS`
- **THEN** the `detail` event is emitted (opens `CardDetail` sheet)
- **AND** the `click` event is NOT emitted on the press release (long-press flag suppresses it)

#### Scenario: Unusable / readonly hand card swallows the click

- **WHEN** a hand card has `unusable === true` or `readonly === true` and the user clicks anywhere on it (preview or detail pane)
- **THEN** the `click` event is NOT emitted
- **AND** the existing visual treatment (greyscale + reduced opacity for unusable, default cursor for readonly) is the user's only feedback
