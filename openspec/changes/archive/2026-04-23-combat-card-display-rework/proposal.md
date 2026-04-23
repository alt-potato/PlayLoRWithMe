## Why

Combat-page card tiles (`HandCard.vue`, `SlottedCard.vue`) and the `CardDetail` modal have three usability defects:

1. `HandCard` has only a min-height floor, so cards with more dice grow taller than cards with fewer — the hand is visually ragged and re-flows during play.
2. The card range is rendered as raw enum text (`"FarArea"`, `"Instance"`) in the upper-right, which is verbose, jargony, and gives no glanceable distinction between melee, ranged, mass, and self-targeting.
3. A gold `+` is overloaded as the "open detail" affordance for both card-level `abilityDesc` and per-die `desc`. It collides semantically with arithmetic / buff modifiers (where `+1 Power` is the established meaning), and it relies on a `title` tooltip that does not work on touch — leaving mobile players without any way to read effect text from the hand.

Card cost is already in the upper-left and already uses `costStyle()` to colour-shift on cost delta, so that part is already correct and stays as-is.

## What Changes

- `HandCard.vue`: pin to a fixed **5 : 7** aspect ratio (`width: 4rem; aspect-ratio: 5 / 7;`), so all hand tiles are uniform regardless of die count. Inner content scrolls / truncates rather than expanding the tile.
- Replace the upper-right range text in `HandCard.vue`, `SlottedCard.vue`, and `CardDetail.vue` with a new shared `CardRangeIcon.vue` component. It renders a single in-game-style glyph per `CardRange` value:
  - `Near` — sword (custom inline SVG)
  - `Far` — gun (custom inline SVG)
  - `Instance` — downward triangle with a horizontal lightning bolt over it (custom inline SVG)
  - `Special` — sword with a small superscript `+` (custom inline SVG)
  - `FarArea` — Unicode `Σ` (mass summation)
  - `FarAreaEach` — Unicode `∀` (mass individual)
  - Fallback — original range string as text, so unknown values are visible rather than hidden.

  Glyphs render in `var(--gold)`. The original range string is exposed via `title` for hover and via the component's accessible name for screen readers.
- Surface card `abilityDesc` and per-die `desc` text **inline in the detail pane** rather than behind a tappable marker. The previous gold `+` marker (and the `‡` double-dagger we briefly trialled) is dropped — at hand-card scale the glyph was too small to be a reliable touch target, and the in-game LoR card layout shows effect text directly on the card face anyway. Long-press of the card body remains the path to the full `CardDetail` modal.
- `SlottedCard.vue` and `CardDetail.vue` get the same range-glyph and dagger treatment so the symbology is consistent across surfaces.

Card-portrait extraction (mimicking the in-game card art layout) is **explicitly out of scope** for this change and is deferred to a separate proposal.

## Capabilities

### New Capabilities

- `combat-card-display`: visual rules and interaction affordances for the combat-page card tiles (hand, slotted, and detail view). Codifies the fixed aspect ratio, the range-glyph mapping, and the dagger-as-detail-marker affordance.

### Modified Capabilities

_(none — this is a frontend-only display rework with no spec-level behavioural overlap with existing capabilities)_

## Impact

- **Frontend code touched** (all under `frontend/app/components/`):
  - `HandCard.vue` — fixed aspect ratio, `CardRangeIcon` integration, `‡` markers, marker click → emit `detail`.
  - `SlottedCard.vue` — `CardRangeIcon` integration, `‡` markers.
  - `CardDetail.vue` — `CardRangeIcon` in header.
  - `icons/CardRangeIcon.vue` — new shared component.
- **Frontend tests**: a small `CardRangeIcon` unit test verifying glyph mapping (including fallback) and accessible-label output.
- **No backend changes**: `Card.range` continues to carry the raw `CardRange` enum string; only its display is reworked.
- **No new runtime dependencies**: SVGs are inlined in the component.
- **Mobile-first impact**: touch users gain access to die-effect descriptions for the first time, via the now-tappable `‡` marker.
