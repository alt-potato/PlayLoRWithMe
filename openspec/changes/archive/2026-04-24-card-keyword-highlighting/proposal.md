## Why

Combat-card ability text and per-die effect text contain bracketed keywords like `[On Use]`, `[On Clash Win]`, `[On Hit]`, `[Counter]`, `[Combat Start]`, `[Reroll]`. In the base game, these brackets are highlighted in a bright yellow/gold so players can scan cards quickly — the bracketed keyword tells you *when* the effect fires, and the text after says *what* happens.

In our mod today, `CardDetail.vue` and `HandCard.vue` render these descriptions via plain `{{ }}` interpolation, so the brackets are present but rendered in the same tint as the surrounding text. At the font sizes we use on the hand card's detail pane (0.62-0.68rem), keywords blend into the body text and the "when vs. what" read is lost.

## What Changes

- Add a pure utility `frontend/app/utils/keywordHighlight.ts` that splits a description string into an array of `{text, isKeyword}` segments via `/\[[^\]]+\]/g`. Brackets are stripped from the emitted keyword segments (matching the in-game view, which drops the brackets and renders only the keyword word in gold). Includes unit tests.
- Add a small presentational component `frontend/app/components/KeywordText.vue` that renders those segments, wrapping `isKeyword: true` segments in a `<span class="keyword">` styled with `color: var(--gold-bright)` (`#e8c247`) and a slightly heavier font weight.
- Replace the four plain-text interpolations of ability/die descriptions in `CardDetail.vue` and `HandCard.vue` with `<KeywordText>`.

Descriptions with no brackets render as a single plain-text segment (identical output to today). No data-model changes; no backend changes. `SlottedCard.vue` does not render descriptions and is untouched.

**Explicitly out of scope:**
- Per-keyword colors (different hue per keyword). The base game appears to use a single highlight color for all bracketed keywords; per-keyword coloring is a larger investigation + palette decision that would expand scope.
- Numeric-value highlighting (e.g. coloring damage numbers). Different problem; not bracket-delimited.
- Keyword-hover tooltips (explaining what `[Counter]` does). Separate affordance.

## Capabilities

### New Capabilities

- `card-keyword-highlighting`: the rule that bracketed tokens in card ability / die description text are rendered in a bright gold highlight, consistently across the hand card's detail pane and the `CardDetail` sheet.

### Modified Capabilities

_(none — this is a purely additive display affordance that layers on top of the existing `combat-card-display` render pipeline without changing its structure.)_

## Impact

- **Frontend code added**:
  - `frontend/app/utils/keywordHighlight.ts` (new — pure function)
  - `frontend/app/utils/keywordHighlight.test.ts` (new — unit tests)
  - `frontend/app/components/KeywordText.vue` (new — ~30 lines)
- **Frontend code touched**:
  - `frontend/app/components/CardDetail.vue` — lines 64 and 86: swap `{{ ... }}` for `<KeywordText :text="..." />`
  - `frontend/app/components/HandCard.vue` — lines 197 and 227: same swap
- **No backend changes**: the raw description strings already ship with bracketed keywords intact from `GameStateSerializer.cs`.
- **No new runtime dependencies**: pure string processing, no libraries.
- **Styling reuses an existing token**: `--gold-bright` is already defined in `app.vue`'s design-token block.
