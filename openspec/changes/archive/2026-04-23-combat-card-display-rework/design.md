## Context

The combat-page card surfaces (`HandCard.vue`, `SlottedCard.vue`, `CardDetail.vue`) were authored when the only goal was "show what the card does, somewhere". Three concrete defects are flagged in the proposal:

- `HandCard` height is variable (only `min-height: 5.6rem`), driven by die count.
- The range field is rendered as raw `CardRange` enum text (`"FarArea"`, `"Instance"`).
- A gold `+` is overloaded as both "click for description" and as the only desktop-tooltip-driven affordance for die effects, which is invisible to touch users.

Existing primitives that this change builds on, rather than replaces:

- `costStyle(card)` in `useBattleDisplay.ts` already produces the correct red/green delta tint for the cost badge — no change to the cost surface is required.
- `cardBorderColor(card)` already encodes rarity / EGO border colour.
- The `Card.range` field is a raw `CardRange` enum string (`"Near" | "Far" | "FarArea" | "FarAreaEach" | "Special" | "Instance"`) that ships unmodified from the backend.
- `CardDetail` is already opened on long-press of a hand card and exposes `abilityDesc` and per-die `desc` text in full; we do not need a new modal — just additional ways to reach the existing one.

## Goals / Non-Goals

**Goals:**

- Uniform hand layout: all `HandCard` tiles render at the same fixed aspect ratio, regardless of die count, rarity, or token list length.
- Glanceable range identification via in-game-flavored glyphs that match what players already recognise from the LoR UI / community card databases.
- A touch-accessible affordance for opening die-effect descriptions, replacing the desktop-only `title` tooltip.
- Drop the `+` symbol where it currently means "see more text", because it semantically collides with arithmetic / buff modifiers (`+1 Power`).

**Non-Goals:**

- **Card portrait extraction** — adding card artwork to `CardDetail` requires a parallel `IconCache.cs` extension and ~10–20MB of generated PNGs. Out of scope here; deferred to a follow-up change.
- **Aesthetic overhaul** — the user explicitly requested staying within the current minimalist style. No new ornamental frames, no rarity gradients, no portrait shells.
- **Backend changes** — `Card.range` continues to ship as the raw enum string. All re-styling is client-side.
- **Slotted-card aspect ratio** — `SlottedCard` is rendered inside `DieRow` and intentionally fills available width; only `HandCard` gets the fixed aspect.

## Decisions

### 1. Fixed preview pane + `displayMode`-driven detail pane

`HandCard` renders as a flex row with two panes:

- **Preview pane** (always rendered): `width: 4rem; aspect-ratio: 5 / 7;`, contains cost, range glyph, name, dice-icons (no min-max numbers), token list, count badge. Scan density unchanged from pre-rework.
- **Detail pane** (always in the DOM, visibility controlled by CSS): `width: 6.5rem`, stretches to preview's height. Lists each die vertically with `[icon] min-max` head and the die's full effect description text below (wrapping). Card-level `abilityDesc` shown at the top. The pane is `overflow-y: auto` with a slim scrollbar — long descriptions wrap and scroll, never truncate.

A new `displayMode: "compact" | "full"` prop controls visibility:

- **`"compact"`** (default; deck-building, key-page browsing, librarian manager): detail pane hidden via `display: none`. On hover-capable devices (`@media (hover: hover)`), `:hover` on the card un-hides the detail pane — matches the in-game "mouseover for full view" pattern. Touch-only devices fail the `(hover: hover)` gate and never accidentally trigger the expansion; their full-info path remains long-press → `CardDetail` modal.
- **`"full"`** (in-battle hand): detail pane always visible. Mirrors the in-game battle card layout where every card on the field shows its full effect text.

**Why this shape vs. selection-driven (the previous iteration):**

- The selection-driven model coupled "I'm thinking about playing this" with "I want to read this," which are different intents. Decoupling them removes a confused interaction.
- The pure CSS `:hover` mechanic means there is **no JS state** for the expansion — no flicker on re-renders, no race with the click/long-press timers, no extra reactivity overhead.
- Touch users in compact mode get a clean preview-only experience; the full info is one long-press away. They never see a half-functional hover affordance.
- The pattern matches the in-game LoR convention exactly, so users with familiarity get free transfer.

**Why preserve the 5:7 preview footprint:**

- Hand density at rest is identical to the pre-rework hand — no regression for the common "scan my cards" case.
- Hit target on the card body is unchanged.

**Hover-reveal mechanics — final settled approach:**

In compact mode the detail pane is `position: absolute; top: 0; left: 100%; height: 100%;` inside the card. CSS-only `:hover` triggers `display: flex`, gated by `@media (hover: hover)` so touch devices don't accidentally trigger.

The pane carries its own `border: 1px solid var(--border-mid)` so it reads as a distinct surface even when not adjacent to the preview pane's frame. Full mode has both panes inline as flex siblings; the detail pane omits the explicit border in that case (the card's outer border already wraps both).

We previously trialled `<Teleport to="body">` with computed `position: fixed` to escape parent clipping and stacking. That **was reverted** — the user is fine with the hover preview being painted under sticky filter chrome (the simpler implementation wins). The tradeoff: the overlay can be partially obscured by higher-z-index siblings; this is accepted.

**Removed: hover/selection lift.** Earlier iterations had `transform: translateY(-3px)` on `:hover` and `translateY(-5px)` on `--selected`. These caused the card to intrude into sticky chrome above the deck list (the filter panel) and get visually clipped. Both translates removed; the selection state now uses a `box-shadow: 0 0 0 2px` glow ring instead of a vertical translate, and hover has no transform at all.

**Risk: in-battle hand becomes ~2.6× wider per card.** With every card always at `~10.5rem` (preview + detail), a 9-card hand reaches ~95rem ≈ 1500px. **Mitigation:** the hand row already supports horizontal scroll; this matches how players scroll the in-game hand on narrow displays.

### 2. Range glyphs: a new `CardRangeIcon.vue` component

A single shared component, `frontend/app/components/icons/CardRangeIcon.vue`, accepts a `range: string` prop and renders one of:

| `range`        | Glyph                                                  | Source              |
| -------------- | ------------------------------------------------------ | ------------------- |
| `Near`         | sword                                                  | inline SVG          |
| `Far`          | gun (pistol silhouette)                                | inline SVG          |
| `Instance`     | downward triangle (▽) with horizontal lightning bolt  | inline SVG          |
| `Special`      | sword with small superscript `+`                       | inline SVG          |
| `FarArea`      | `Σ` (U+03A3, uppercase sigma)                          | Unicode in `<span>` |
| `FarAreaEach`  | `∀` (U+2200, for-all / inverted A)                     | Unicode in `<span>` |
| _other_        | the `range` string verbatim                            | text fallback       |

Glyphs render in `var(--gold)` at the same approximate visual size that the previous text occupied (~`0.9em` square, `currentColor` fill on the SVGs). The component sets `title` to the original range string and exposes the same string as `aria-label`, so hover and screen-reader behaviour both surface the canonical name.

**Why a component, not a helper that returns a string:** the four sword/gun/triangle/sword-plus glyphs are SVGs, not characters. They need to participate in CSS sizing, `currentColor` inheritance, and hover/title behaviour, so they need a real DOM presence.

**Why not extract from the game:** the in-game range icons are stored as Unity sprites and would require an `IconCache`-style extraction pipeline. Inline SVGs are zero new assets, render crisply at any DPI, and we have full creative control over their stroke weight to match the existing minimalist palette. This also keeps the affordance available offline and during initial sprite extraction.

**Why mix Unicode and SVG:** `Σ` and `∀` are semantically exact (the tiphereth community database uses them, and they convey "summation" / "for-each" universally). They render in our existing display font and need no asset. The other four ranges have no good Unicode equivalent (`⚔` is "crossed swords" — wrong; `🔫` is emoji — inconsistent rendering), so they must be SVG.

**Alternative considered:** a single sprite-sheet PNG with all six glyphs. Rejected — six glyphs do not justify a sprite sheet, and inline SVGs scale and re-color via `currentColor` for free.

### 3. Detail-pane layout: two-column per-die grid, evenly distributed

Within the detail pane, each die is a CSS grid row (`grid-template-columns: auto 1fr`):

- **Left column (`auto`):** die-type icon and `min-max` range numbers laid out side-by-side (`flex-direction: row`). All icons land on the same left edge across rows.
- **Right column (`1fr`):** the die's effect description text, vertically centered with the icon/range row and wrapping freely.

Dice rows take their **natural height** (no `flex: 1`) and stack from the top of the pane. Empty space accumulates at the bottom if content does not fill the pane. Per-row contents stay vertically centered relative to each other — useful when a desc wraps to multiple lines, so the icon+range still sits at the row's vertical midpoint. If the combined dice content exceeds the pane's available height, the pane scrolls vertically (`overflow-y: auto`).

There are no separator borders between die rows — the two-column structure already gives the page-level `abilityDesc` (full-width above the dice) a natural visual distinction from per-die effects (paired with their icon).

**Text colour conventions** (mirroring in-game card styling):

- Page-level `abilityDesc`: near-white (`#f5efde`), the brightest text on the card.
- `Atk` die desc: light red (`#f0c2c2`).
- `Def` die desc: light blue (`#c2d8f0`).
- `Standby` die desc: light gold (`#f0d8a0`).
- All per-die desc uses `line-height: 1` ("single spacing" — matches base game).

Conditional-keyword highlighting (e.g. "On Hit", "On Clash Win" tinted yellow) is **not** in this change. It requires a parser/regex over the desc text; a follow-up change can introduce it without touching the layout.

### 4. Description text rendered inline; no annotation marker

Earlier iterations of this change trialled a tappable annotation marker (gold `+`, then `†`, then `‡`) next to the card name and each die row, opening `CardDetail` on tap to surface the full description text. Both daggers proved too small at hand-card scale to be a reliable touch target, and `†` additionally read too similarly to the sword glyph used by the `Near` range.

**Final decision:** drop the annotation marker entirely. Description text lives directly inside the detail pane:

- `card.abilityDesc` renders as a small `<p>` at the top of the detail pane.
- Each die's `d.desc` renders as a small `<p>` immediately below its `[icon] min-max` head row.
- Both are unbounded — they wrap inside the pane's `width: 6.5rem`. The pane scrolls on overflow; nothing is truncated.

Long-press of the card body remains the path to `CardDetail` for users who want a larger, isolated reading surface — but most readers will get what they need from the inline detail pane (especially in `displayMode="full"` where it is always visible).

**Why this is better than the marker approach:**

- No micro-target hit-test problems. The card body's existing long-press is the only touch interaction; nothing competes with it.
- The detail pane's text is the same content the user would have seen via `CardDetail` — but reachable without an extra modal layer in compact-hover and full-mode flows.
- Matches the in-game card layout 1:1.

**Trade-off accepted:** in compact mode without hover (i.e. touch-only deck-browsing), users can no longer see at a glance which cards have description text. They learn this by long-pressing. This is the same affordance that already exists for "tap a card to see what it does," so the user model is consistent.

### 4. Component scope and re-use

`CardRangeIcon` lives at `frontend/app/components/CardRangeIcon.vue` (top-level, not nested in `icons/`) so Nuxt's auto-import surfaces it as `<CardRangeIcon>` rather than `<IconsCardRangeIcon>`. It is reusable across `HandCard`, `SlottedCard`, and `CardDetail` without explicit imports.

The display-mode logic is **not** extracted into a composable — it is one prop and a small block of CSS. A composable would be premature abstraction for this scope.

## Risks / Trade-offs

- **Glyph recognition risk** — players who do not already know LoR's glyph vocabulary may need a moment to map sword → melee. **Mitigation:** the `title` attribute and `aria-label` always carry the canonical range string, so hover and screen-readers expose the full word. The fallback path also leaves unknown ranges as readable text.
- **`aspect-ratio: 5 / 7` may clip content on very narrow viewports** if a future feature adds another row to the preview pane. **Mitigation:** the preview's dice are now icons-only and centered (no min-max numbers), so the previous overflow case (many dice eating the name) is resolved. Additional content would need an explicit overflow decision at the time it is added.
- **Compact-mode hover overlay can be obscured by sticky chrome.** The pane is `position: absolute` within the card; sticky filter panels with higher z-index render in front. **Mitigation:** accepted trade-off — `<Teleport>`/`position: fixed` was tried and reverted because the simpler approach is preferred and the obscuring is rare in practice (filter panels sit above the deck list, hover happens at card position).
- **Battle-mode hand row gets very wide.** With every card showing both panes, hands of 7-9 cards exceed common viewport widths. **Mitigation:** matches in-game behaviour — the hand already supports horizontal scroll. Mobile users will see 2-3 cards at a time and scroll, identical to the in-game mobile experience.
- **Touch-only users in compact mode cannot tell which cards have descriptions** without long-pressing each. **Mitigation:** accepted trade-off — the same affordance pattern as "what does this card do," and the inline-text approach removes the touch hit-target problem that would otherwise be worse with a `†`-style marker.
- **Σ and ∀ glyph weight depends on the user's installed display font** — these will not look hand-tuned to match the SVG glyphs. **Mitigation:** acceptable for v1; the alternative is shipping all six as SVG, which is more work for marginal gain. If the visual mismatch is jarring in practice, a follow-up can convert them to SVG with no API change to `CardRangeIcon`.
