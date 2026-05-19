# combat-card-display Specification

## Purpose
TBD - created by archiving change combat-card-display. Update Purpose after archive.
## Requirements
### Requirement: Hand card tiles SHALL render a fixed preview pane and a `displayMode`-driven detail pane

Every `HandCard` tile MUST render a **preview pane** with fixed dimensions (`width: 5.5rem; aspect-ratio: 5 / 7;`) that contains the cost badge, range glyph, card name, dice icons (without min-max numbers), optional token list, and optional ×N count badge. The preview pane MUST be the same size regardless of how many dice, tokens, or descriptions the card has.

Every `HandCard` MUST also render a **detail pane** in the DOM, alongside the preview, containing each die's type icon, min-max range coloured by die type, and the die's full effect description (text wraps; the pane scrolls vertically on overflow). When the card has a non-empty `abilityDesc`, the detail pane MUST also surface that text at the top.

The detail pane's visibility is determined by the `displayMode` prop:

- **`"compact"` (default)** — used in deck-building, key-page browsing, and other non-battle surfaces. The detail pane is hidden at rest. On a hover-capable device (`@media (hover: hover)`), hovering the card reveals the detail pane as an absolutely-positioned overlay to the right of the preview (or to the left when the card sits near the right edge of its nearest scroll-clipping ancestor, to avoid overflow). On touch-only devices the detail pane never appears via this path; the user reaches the same information by long-pressing the card to open the existing `CardDetail` sheet.
- **`"full"`** — used in the in-battle hand. The detail pane is always visible inline to the right of the preview, regardless of selection or hover.

In compact mode the card's total footprint MUST NOT change when the detail pane is shown (the overlay is absolutely positioned so neighbouring cards do not reflow); in full mode the preview and detail panes sit side-by-side and together define the card's footprint.

#### Scenario: Two compact-mode cards with different die counts in the same hand

- **WHEN** the player views their deck (compact mode) with one card having no dice and one card having three dice, and neither is hovered
- **THEN** both `HandCard` tiles render at identical preview-pane dimensions
- **AND** neither tile shows a detail pane

#### Scenario: Compact-mode card has overflow-prone content

- **WHEN** a compact-mode hand card has a long name, many tokens, and four dice, and is not hovered
- **THEN** the name is truncated (existing line-clamp behaviour)
- **AND** the dice appear as icons only within the preview pane
- **AND** no per-die range numbers are visible

#### Scenario: Hovering a compact-mode card on a desktop reveals the detail pane

- **WHEN** a user with a hover-capable input device moves the cursor over a compact-mode `HandCard`
- **THEN** the detail pane becomes visible alongside the preview pane
- **AND** every die's range and full effect description are listed in the detail pane
- **AND** neighbouring cards in the row do NOT reflow for the duration of the hover

#### Scenario: Hovering a card near the right edge of a scrollable container

- **WHEN** a compact-mode card whose right edge is close to the right edge of the nearest scroll-clipping ancestor is hovered
- **THEN** the detail-pane overlay flips to the left side of the preview instead of the right
- **AND** the overlay does NOT extend past the container's right edge or introduce a horizontal scrollbar

#### Scenario: Long-pressing a compact-mode card on touch opens CardDetail

- **WHEN** a touch-only user long-presses a compact-mode `HandCard`
- **THEN** the existing `CardDetail` sheet opens with the full card information
- **AND** the in-card detail pane does NOT become visible (touch devices fail the `@media (hover: hover)` gate)

#### Scenario: Battle hand uses full mode

- **WHEN** the in-battle hand is rendered for an ally unit
- **THEN** every `HandCard` is passed `displayMode="full"`
- **AND** every `HandCard` shows both the preview and detail panes simultaneously, regardless of selection

#### Scenario: Detail pane content overflows the available height

- **WHEN** a card's combined ability and die descriptions exceed the detail pane's vertical space
- **THEN** the detail pane's content wraps (no truncation by ellipsis)
- **AND** the detail pane provides a vertical scrollbar so the user can read overflow content

### Requirement: Card range SHALL be displayed as a glanceable glyph in the upper-right of every card surface

The `HandCard`, `SlottedCard`, and `CardDetail` header surfaces MUST replace the raw `CardRange` enum text with a glyph rendered by a shared `CardRangeIcon` component. The mapping is:

| `range` value  | Glyph                                                |
| -------------- | ---------------------------------------------------- |
| `Near`         | sword (outline-only SVG)                             |
| `Far`          | rifle (outline-only SVG)                             |
| `Instance`     | downward triangle with horizontal lightning bolt SVG |
| `Special`      | sword with small superscript `+` (outline-only SVG)  |
| `FarArea`      | `Σ` (Unicode U+03A3)                                 |
| `FarAreaEach`  | `∀` (Unicode U+2200)                                 |

Glyphs MUST inherit their foreground colour through `var(--rarity-range-icon-color, var(--gold))`, allowing per-card colour overrides via the inline-var pattern while preserving the default gold-on-dark rendering. The component MUST expose the original `range` string via the HTML `title` attribute (for desktop hover) and via `aria-label` (for assistive tech). Unknown values MUST fall back to rendering the raw `range` string as text without producing a runtime error.

#### Scenario: Mass-summation card in hand

- **WHEN** a hand card with `range === "FarArea"` is rendered with no rarity override
- **THEN** the upper-right of the tile shows the `Σ` glyph
- **AND** the glyph's foreground colour resolves to `var(--gold)`
- **AND** hovering the glyph reveals the tooltip text `"FarArea"`

#### Scenario: Self-targeting card in hand

- **WHEN** a hand card with `range === "Instance"` is rendered
- **THEN** the upper-right of the tile shows the downward triangle + lightning bolt SVG
- **AND** the glyph's `aria-label` is `"Instance"`

#### Scenario: Custom-rarity card recolours the glyph

- **WHEN** a hand card payload supplies `rarityRangeIconColor: "#9b00ff"` along with the card's range
- **THEN** the rendered glyph's foreground colour is `#9b00ff`
- **AND** the glyph shape itself is unchanged from the table above

#### Scenario: Unknown range value

- **WHEN** a card has a `range` value that is not in the supported mapping
- **THEN** `CardRangeIcon` falls back to rendering the raw `range` string as text
- **AND** no console error or icon-not-found warning is produced

### Requirement: Card surfaces SHALL honour rarity colour overrides when present

When a card payload includes `rarityColor`, `rarityRangeIconColor`, `rarityAbilityColor`, or `rarityKeywordColor`, the corresponding card surfaces (`HandCard`, `SlottedCard`, `CardDetail`) MUST set the matching CSS custom property inline on the card's root element so descendant CSS can consume it:

| Payload field | CSS var set inline | Consumed by |
|---|---|---|
| `rarityColor` | `--rarity-color` | rarity-bordered chrome (panel/border) |
| `rarityRangeIconColor` | `--rarity-range-icon-color` | `CardRangeIcon` glyph fill |
| `rarityAbilityColor` | `--rarity-ability-color` | `abilityDesc` body text |
| `rarityKeywordColor` | `--rarity-keyword-color` | bracketed-keyword highlights inside descriptions |

Each surface MUST consume each var with a default fallback so absent values render exactly as they did before this change.

#### Scenario: Hand card with full override set

- **WHEN** a hand card payload includes all four `rarity*Color` fields
- **THEN** the rendered `HandCard` root element carries inline styles setting all four CSS vars
- **AND** the rarity-bordered chrome uses `rarityColor`
- **AND** the `CardRangeIcon` glyph uses `rarityRangeIconColor`
- **AND** the `abilityDesc` body text uses `rarityAbilityColor`
- **AND** bracketed keywords inside the descriptions use `rarityKeywordColor`

#### Scenario: Hand card with only `rarityColor` set

- **WHEN** a hand card payload includes only `rarityColor`, with the other three override fields absent
- **THEN** the rarity-bordered chrome uses `rarityColor`
- **AND** the range glyph, ability text, and keyword highlights render with their pre-change default colours

#### Scenario: Hand card with no overrides falls back to defaults

- **WHEN** a hand card payload omits all four `rarity*Color` fields
- **THEN** the card renders exactly as it did before this change, with no inline `--rarity-*-color` overrides emitted

### Requirement: Card cost dynamic-change accent SHALL be preserved

The cost badge in the upper-left of every card surface MUST continue to display the dynamic-cost accent produced by the existing `costStyle()` helper: red fill when the live cost is greater than `baseCost`, green fill when less, and the default gold fill when equal or `baseCost` is absent.

#### Scenario: Cost reduced by an in-game effect

- **WHEN** a hand card's `cost` is less than its `baseCost`
- **THEN** the cost badge renders with the green-tinted background and foreground from `costStyle()`

#### Scenario: Cost unchanged

- **WHEN** a hand card has no `baseCost` set, or its `cost` equals its `baseCost`
- **THEN** the cost badge renders in the default gold palette

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

### Requirement: Mod-side serializer SHALL mirror `StageController.IsHideEnemyDiceAbilityInfo()` when emitting per-die descriptions

When `Singleton<StageController>.Instance.IsHideEnemyDiceAbilityInfo()` returns `true` during the `battle` scene branch of `GameStateSerializer.BuildJson` (equivalent to any active `EnemyTeamStageManager.IsHideDiceAbilityInfo()` override returning `true` — including `EnemyTeamStageManager_TheCrying` while any alive enemy holds an undestroyed `PassiveAbility_240328`), the serializer MUST mask the per-die description on enemy-owned slotted cards:

- For every unit where `unit.faction == Faction.Enemy`, each die entry inside every `slottedCards[].dice[]` MUST emit `desc: "???"` rather than the real description string returned by `BattleCardAbilityDescXmlList.GetAbilityDesc` / `DiceBehaviour.Desc`.
- For every unit where `unit.faction == Faction.Player`, each die entry MUST continue to emit the real `desc` value exactly as today. Vanilla only gates enemy-owned card previews.
- All other fields on the slotted card (`cardId`, `name`, `cost`, `range`, `abilityDesc`, `rarity`, `emotionLimit`, `baseCost`, `bufs`, `options`, `dice[].type`, `dice[].detail`, `dice[].min`, `dice[].max`) MUST emit unchanged in both factions.

When `IsHideEnemyDiceAbilityInfo()` returns `false` (the steady-state default), all per-die `desc` fields MUST emit exactly as they do today — this requirement is gated on the suppression condition only.

If `Singleton<StageController>.Instance` is null or the call throws, the serializer MUST treat the gate as `false` (i.e. emit real `desc` values). This matches the vanilla `try/catch` fail-open behavior inside `StageController.IsHideEnemyDiceAbilityInfo` and prevents unrelated runtime errors from masking descriptions outside the gated encounter.

#### Scenario: Crying Children encounter is active and an enemy holds Unseeing Child

- **WHEN** a battle state is built while an alive enemy has `PassiveAbility_240328` (passive id 240328) and it is not destroyed
- **THEN** every enemy unit's `slottedCards[].dice[].desc` is the literal string `"???"`
- **AND** every ally unit's `slottedCards[].dice[].desc` is the real description string from `BattleCardAbilityDescXmlList`
- **AND** every other field on every slotted card (name, cost, range, card-level `abilityDesc`, dice type/detail/min/max, bufs, options) emits exactly as it does outside the gated encounter

#### Scenario: Unseeing Child holder becomes Staggered

- **WHEN** the holder of `PassiveAbility_240328` reaches stagger (`OnBreakGageZero` flips the passive's `destroyed` to `true`) and a subsequent state push is built
- **THEN** `IsHideEnemyDiceAbilityInfo()` returns `false`
- **AND** the masking rule no longer applies
- **AND** enemy `slottedCards[].dice[].desc` again carries the real description string

#### Scenario: Standard encounter with no `IsHideDiceAbilityInfo` override

- **WHEN** a battle state is built in any encounter where no alive enemy gates `IsHideEnemyDiceAbilityInfo()` to `true`
- **THEN** both enemy and ally slotted cards emit per-die `desc` exactly as before this change

#### Scenario: `StageController` is unavailable

- **WHEN** the serializer is invoked but `Singleton<StageController>.Instance` is null or `IsHideEnemyDiceAbilityInfo()` throws
- **THEN** the serializer behaves as if the gate is open (`false`)
- **AND** enemy and ally slotted cards emit real per-die `desc` values
- **AND** no exception is propagated out of `BuildJsonSafe`

### Requirement: Frontend rendering of per-die descriptions SHALL pass the masked string through without additional gating

Existing frontend card surfaces MUST render whatever string the wire emits for `dice[].desc` without applying any per-encounter mask of their own. When the mod emits `"???"` per the rule above, every surface that renders die descriptions (notably `unit/DisplayCard.vue` detail pane, `CardDetail.vue`, `SlottedCard.vue` tooltip if any) MUST display the literal `"???"` text in place of the usual effect description. This locks in the no-frontend-change posture of this change so future contributors do not introduce a redundant frontend gate.

#### Scenario: Enemy slotted card carries `"???"` descriptions on every die

- **WHEN** state arrives with an enemy unit's slotted card where every `dice[].desc === "???"`
- **THEN** the rendered card surface shows `???` (verbatim) in the per-die description area for every die
- **AND** the card name, cost, range, card-level `abilityDesc`, and dice icons/values render normally

#### Scenario: Schema accepts the masked payload

- **WHEN** a wire payload representing the Crying Children die-description-masked state is parsed by `GameStateSchema`
- **THEN** parsing succeeds without validation errors
- **AND** the parsed shape exposes enemy slots whose every die has `desc: "???"` and ally slots whose dice carry the real description strings
