## Context

Card-description strings ship to the frontend as plain text from `GameStateSerializer.cs` via `BattleCardAbilityDescXmlList.GetAbilityDescString()`, and land in `Card.abilityDesc` / `Card.dice[].desc` (both typed as `z.string().optional()` in `types/game.ts`). Brackets like `[On Use]` are present verbatim — the base game applies coloring at the UI layer, not in the data. The investigation in the preceding conversation confirmed this.

The four render sites today use plain Vue text interpolation:

- `CardDetail.vue:64` — `<p class="ability-desc">{{ card.abilityDesc }}</p>`
- `CardDetail.vue:86` — `<span class="die-desc">{{ d.desc }}</span>`
- `HandCard.vue:197` — `<p class="hcard-detail-ability">{{ card.abilityDesc }}</p>`
- `HandCard.vue:227` — `<p class="hcard-die-desc">{{ d.desc }}</p>`

The preceding `combat-card-display-rework` change (2026-04-23) explicitly deferred keyword highlighting, noting: *"Conditional-keyword highlighting (e.g. 'On Hit', 'On Clash Win' tinted yellow) is not in this change. It requires a parser/regex over the desc text; a follow-up change can introduce it without touching the layout."* This is that follow-up.

## Goals / Non-Goals

**Goals:**

- Visually distinguish bracketed keywords from surrounding effect text across the hand card detail pane and the `CardDetail` modal.
- Match the base game's visible convention: a single bright yellow/gold for all keywords (rather than per-keyword colors).
- Keep the change atomic: pure frontend, no C# touched, no wire-format change, no existing CSS classes renamed.

**Non-Goals:**

- **Per-keyword colors.** The base game renders all bracketed keywords in the same bright-gold tint (confirmed by spot-checking card text). Assigning per-keyword hues would require a keyword catalog, an investigation of the game's internal color mapping, and palette decisions — disproportionate effort for a visual refinement whose primary value is "scan the brackets quickly."
- **Numeric-value highlighting.** "5 damage" is also visually highlighted in some in-game contexts; different parser, different scope.
- **Rich-text passthrough.** The backend sends plain text. We do not accept or emit HTML — parsing is regex-based on bracket literals, never `v-html`.
- **Tooltips on keywords.** Explanatory hover on `[Counter]` etc. is a separate interaction, not a display concern.
- **`SlottedCard.vue` changes.** That component doesn't render descriptions, so there's nothing to highlight there.

## Decisions

### 1. Pure string-splitting utility, not a Vue-coupled helper

`frontend/app/utils/keywordHighlight.ts` exports a single pure function:

```ts
export interface KeywordSegment { text: string; isKeyword: boolean }
export function splitKeywordSegments(input: string): KeywordSegment[]
```

It walks the string with `/\[[^\]]+\]/g`, emitting alternating non-keyword / keyword segments. Empty-string input returns `[]`; a string with no brackets returns a single `{text, isKeyword: false}` segment. **Brackets are stripped from the emitted keyword text** (e.g. `"[On Use]"` → `{text: "On Use", isKeyword: true}`) — this matches the base game, which renders `On Use` in gold without the brackets themselves. Surrounding whitespace is preserved on the neighbouring plain segments.

**Why a pure function instead of a Vue composable**: it's stateless string processing with no reactive inputs. Putting it in `utils/` matches `cardRangeGlyph.ts`, `color.ts`, `setReactive.ts` — and makes it trivially unit-testable in `vitest` without any DOM.

**Regex choice — `\[[^\]]+\]`:**
- Matches any non-empty bracketed run up to the first `]`. Does not attempt to match nested brackets (LoR card text doesn't nest them).
- Does not include `[]` (empty brackets) — harmless; that would be a data bug anyway.
- Unclosed `[` with no matching `]` is treated as plain text (no match), which is the safe failure mode.
- Multiline flag is not needed — card text is single-paragraph in practice, but `[^\]]` matches newlines already if one occurs.

### 2. Presentational component `KeywordText.vue`

A thin wrapper that takes `text: string`, runs it through `splitKeywordSegments`, and renders either a plain text node or a `<span class="keyword">` for each segment. One `<template>` using `v-for` over the segments; no state.

```vue
<template>
  <template v-for="(seg, i) in segments" :key="i"
    ><span v-if="seg.isKeyword" class="keyword">{{ seg.text }}</span
    ><template v-else>{{ seg.text }}</template
  ></template>
</template>
```

The odd formatting with `><` is deliberate — it prevents Vue from emitting whitespace text nodes between the spans, which would add unwanted gaps around keyword highlights inside a paragraph.

**Why a component, not a helper that builds a string with `v-html`:**
- `v-html` + HTML string would require escaping the user-visible text (XSS safety). The data is from our own mod serializer, but defense-in-depth + boring code says we use structured output.
- A component lets us scope the `.keyword` CSS once rather than repeating styles across four call sites.

### 3. Highlight styling

The `.keyword` span uses:

```css
color: var(--gold-bright);  /* #e8c247 — already in the design-token block */
font-weight: 600;
```

`--gold-bright` is an existing token (see `app.vue` L215), brighter than the standard `--gold` `#c9a227`, and matches the user's request of "brighter yellow/gold to match the base game." `font-weight: 600` (semi-bold) gives keywords a small weight bump for scan-ability without crossing into "heavy" territory that would clash with the light-body text at 0.62-0.68rem sizes.

**Why the keyword color deliberately overrides the per-die-type tint in `HandCard.vue`:** the detail pane has `.hcard-die-desc--atk/def/standby` classes that tint the whole paragraph light-red / light-blue / light-gold. Keyword spans inside those paragraphs should read as keywords first — the gold highlight is what the player scans for, not the per-die tint. CSS specificity naturally handles this: `.keyword` sets `color` directly on the span, overriding the parent's inherited color. No `!important` needed.

### 4. Where to render

Only the four sites that currently interpolate description text:

- `CardDetail.vue:64` — card-level `abilityDesc` in the modal
- `CardDetail.vue:86` — per-die `desc` in the modal
- `HandCard.vue:197` — card-level `abilityDesc` in the hand's detail pane
- `HandCard.vue:227` — per-die `desc` in the hand's detail pane

All four swap `{{ text }}` for `<KeywordText :text="..." />`. Surrounding CSS classes stay intact; the `KeywordText` component renders inline with no block-level wrapper, so it drops into any container (`<p>`, `<span>`) without layout impact.

`SlottedCard.vue` is deliberately left alone — it shows only card name + dice icons, no description text.

## Risks / Trade-offs

- **False-positive bracket matches.** If a card description ever contains a bracket used for a non-keyword reason (e.g. literally "[1]" as a footnote marker), it will be highlighted too. **Mitigation:** accepted — no such content exists in LoR card text today, and if it appears the highlight reads as "this is a special term," which is a reasonable default.
- **Single-color limitation.** Keywords with very different semantics (`[On Use]` vs `[Combat Start]` vs `[Reroll]`) all get the same tint. **Mitigation:** accepted per the proposal's explicit non-goal; matches base-game behavior as observed.
- **`font-weight: 600` may not render distinctly on the body font at small sizes.** **Mitigation:** weight is additive to the color bump; even if the weight is imperceptible, the color change alone achieves the primary goal.
- **Regex-based parsing won't handle theoretical future nested brackets.** **Mitigation:** no such data exists; if it ever appears, the inner brackets render as plain text inside a highlighted outer span, which degrades gracefully.
- **Adjacent keywords (`[A][B]` back-to-back) visually collide** after bracket stripping — the rendered spans have no intrinsic separator. **Mitigation:** no such data exists in LoR card text (keywords are always followed by an effect phrase). If it ever surfaces, a follow-up can add a small `margin-right` on `.keyword` or force a separator in the parser.
- **Per-die tint loss inside keywords.** The gold highlight intentionally overrides `.hcard-die-desc--atk` etc. on keyword runs, so a keyword inside an atk-die desc loses the light-red tint. **Mitigation:** intended behavior — the keyword's "this is a trigger" signal is more valuable than the die-type signal, which is already carried redundantly by the die icon on the same row.
