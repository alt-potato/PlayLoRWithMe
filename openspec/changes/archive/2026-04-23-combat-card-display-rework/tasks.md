## 1. CardRangeIcon component

- [x] 1.1 Create `frontend/app/components/CardRangeIcon.vue` (plus pure helper `frontend/app/utils/cardRangeGlyph.ts`) accepting a `range: string` prop, with the `Σ` / `∀` Unicode glyphs and SVG glyphs for `Near`, `Far`, `Instance`, and `Special`. Fall back to the raw `range` string for unknown values. Set `title` and `aria-label` to the original `range` string.
- [x] 1.2 Add a unit test at `frontend/app/utils/cardRangeGlyph.test.ts` covering the six supported ranges plus the unknown-range fallback, asserting both the resolved glyph descriptor and the preserved label.
- [x] 1.3 Run `cd mod && dotnet build` and `cd frontend && npm test` — expect `0 Warning(s) 0 Error(s)` and all tests passing.

## 2. HandCard rework

- [x] 2.1 In `frontend/app/components/HandCard.vue`, swap the `width` / `min-height` declarations for `width: 4rem; aspect-ratio: 5 / 7;` and add `overflow: hidden` to the `.hcard` rule.
- [x] 2.2 Replace the `.hcard-range` text span with `<CardRangeIcon :range="card.range" />`. Keep a minimal `.hcard-range` rule to scale the icon via em-relative sizing.
- [x] 2.3 Replace the `+` glyph in both the card-name annotation and per-die annotation with `†` (U+2020). Rename the `.hcard-desc-plus` CSS class to `.hcard-desc-mark` and update template references.
- [x] 2.4 Convert the `.hcard-desc-mark` span elements to `<button type="button">` with `aria-label="View card description"` (card-level) or `aria-label="View die effect"` (die-level), and an `@click.stop` handler that emits the existing `detail` event so the parent opens `CardDetail`.
- [x] 2.5 Verify visually in the browser that the hand layout is uniform (mix of cards with and without dice / desc / tokens), the dagger opens the detail sheet on tap and on click, and that range glyphs render in gold.
- [x] 2.6 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 3. SlottedCard and CardDetail

- [x] 3.1 In `frontend/app/components/SlottedCard.vue`, render `<CardRangeIcon :range="card.range" />` in the existing `.sc-top` row, between the name and the optional target label.
- [x] 3.2 In `frontend/app/components/CardDetail.vue`, replace the `.card-range` text span in the header with `<CardRangeIcon :range="card.range" />`. Use `margin-right: auto` to preserve the original push-right layout.
- [x] 3.3 Verify visually that the slotted-card row and the detail-sheet header both show the new glyph for `Near`, `Far`, `FarArea`, `FarAreaEach`, `Instance`, and `Special`.
- [x] 3.4 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 4. Two-column layout (preview + selection-driven detail)

- [x] 4.1 In `HandCard.vue`, restructure the template into a `.hcard` flex row with a `.hcard-preview` column (always rendered) and a `v-if="selected"` `.hcard-detail` column. Move the existing cost/range header, name, dice icons, token list, and count badge into `.hcard-preview`.
- [x] 4.2 Remove the min-max die range text from the preview's `.hcard-dice` — the preview shows icons only via a new `.hcard-dice-icons` strip that wraps. Die-level `†` markers move to the detail pane.
- [x] 4.3 Build the detail pane: for each die, render a row with the type icon, min-max range in `dieTypeColor(d.type)`, and a `†` button when `d.desc` is set. Use the existing `onMarkClick` handler.
- [x] 4.4 Update CSS: `.hcard-preview` keeps the `4rem` × 5:7 footprint; `.hcard` is now a flex row container. The detail pane's appear/disappear is animated via `<Transition name="hcard-detail">` with width / opacity / padding interpolation over ~180ms.
- [x] 4.5 Verify visually that an unselected card looks identical in density to pre-4.x, and that selecting a card grows it horizontally with the detail pane — and that the dagger tap in either pane opens `CardDetail`.
- [x] 4.6 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 5. Replace selection-driven detail with `displayMode` + drop the dagger marker

- [x] 5.1 Add `displayMode: "compact" | "full"` prop to `HandCard.vue` (default `"compact"`). Drop `<Transition name="hcard-detail">` and the `selected`-conditional rendering of the detail pane.
- [x] 5.2 Render the detail pane unconditionally in the DOM. Control visibility via CSS: `.hcard--mode-compact .hcard-detail { display: none }` plus `@media (hover: hover) { .hcard--mode-compact:hover .hcard-detail { display: flex } }`. Full mode uses the base `display: flex` — always visible.
- [x] 5.3 Remove all `‡` annotation markers (card-level and per-die `<button>`s) from `HandCard.vue`. Remove the `onMarkClick` handler.
- [x] 5.4 Restructure detail pane content: per-die row becomes a vertical block with a `[icon] min-max` head row plus a `<p>` for `d.desc` that wraps. Add a `<p>` at the top for `card.abilityDesc`. Set `overflow-y: auto` on the detail pane with a slim scrollbar.
- [x] 5.5 Widen detail pane to `width: 6.5rem` for readable wrapped text.
- [x] 5.6 In `unit/DisplayCard.vue`, pass `display-mode="full"` to both `HandCard` instances (ally hand and ally EGO list). Other call sites (DeckTab, LibrarianManager, SettingDetailPanel) keep the default `"compact"`.
- [x] 5.7 Verify visually: in deck-building (compact), cards show preview at rest, expand to detail on desktop hover, never auto-expand on touch. In battle (full), every hand card shows both panes always. Long-press still opens `CardDetail`. No `‡` markers visible anywhere.
- [x] 5.8 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 6. Detail-pane layout polish + glyph fixes

- [x] 6.1 Revert the rifle SVG path to its original (un-mirrored) coordinates so the muzzle lands in the upper-right quadrant after the `-30°` rotation, instead of the lower-left.
- [x] 6.2 Restructure each die row in the detail pane to a CSS grid (`grid-template-columns: auto 1fr`): left column is icon-stacked-above-range; right column is the desc text, vertically centered with the icon/range. Remove the per-die separator border.
- [x] 6.3 Distribute dice rows evenly across the detail pane via `flex: 1` per row inside a flex column container, so a long desc on one die doesn't compress its neighbours.
- [x] 6.4 Switch per-die desc text to `line-height: 1` ("single spacing", matches base game), and add per-die-type colour tints: Atk → light red `#f0c2c2`, Def → light blue `#c2d8f0`, Standby → light gold `#f0d8a0`.
- [x] 6.5 Brighten the page-level `abilityDesc` to near-white `#f5efde` to match the base game's text hierarchy.
- [x] 6.6 In compact mode, position the detail pane absolutely at `left: 100%` with `z-index: 10` so revealing it on hover never reflows neighbouring cards in the hand row.
- [x] 6.7 Verify visually that the rifle now points upper-right (not lower-left), the detail pane's two columns line up cleanly with text wrapping in the right column, dice are evenly spaced, text colour tints match the in-game styling, and the compact-mode hover never reflows the row.
- [x] 6.8 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 7. Detail-pane alignment + hover stacking fixes

- [x] 7.1 Change each die row's left column from a vertical (icon-above-range) stack to a horizontal (`flex-direction: row`) icon-and-range pair. Result: all dice icons share the same left edge across rows.
- [x] 7.2 Drop `flex: 1` from `.hcard-detail-die` and `.hcard-detail-dice` so dice rows take their natural height and stack from the top of the pane. Within each row, contents stay `align-items: center` (icon/range and desc vertically centered with each other).
- [x] 7.3 Add a base `z-index: 1` on `.hcard` so each card establishes its own stacking context, and raise `.hcard--mode-compact:hover` to `z-index: 20` so the absolutely-positioned detail overlay paints in front of subsequent sibling cards in the hand row.
- [x] 7.4 Verify visually that hovering a deck card brings its detail overlay above following cards (no clipping behind), all dice icons line up on the left edge of the dice column, the dice rows top-align inside the pane, and a long desc wraps without throwing the icon out of vertical center.
- [x] 7.5 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 8. Teleported hover overlay + sword/rifle glyph polish

- [x] 8.1 In `HandCard.vue`, replace the `position: absolute` compact-hover detail pane with a `<Teleport to="body" :disabled="displayMode === 'full'">` wrapper. The teleported variant uses `position: fixed`, `z-index: 9999`, a full border, and a drop shadow to read as a floating overlay above any sticky chrome and outside scroll-clip parents.
- [x] 8.2 Replace the pure-CSS `:hover` trigger with a JS `isHovering` ref toggled by `@mouseenter` / `@mouseleave`. Gate the mouseenter handler on `window.matchMedia("(hover: hover)").matches` so touch devices that synthesise hover never trigger the overlay. Compute viewport coordinates from `cardEl.value.getBoundingClientRect()` on mouseenter.
- [x] 8.3 Redesign the `Near` (sword) SVG: longer 4-unit blade taper for a sharper tip, slim 2.5-unit blade body, distinct 12-unit horizontal crossguard, slim 1.5-unit hilt, slightly bulbous pommel — to read as a sword rather than a missile.
- [x] 8.4 Change the rifle's rotation transform from `rotate(-30 8 8)` to `rotate(-150 8 8)` so the muzzle points upper-left (matching the sword's tip orientation) instead of upper-right.
- [x] 8.5 Verify visually that the floating hover overlay paints above filter chrome and isn't clipped by the deck-list scrollbar; the sword reads as a sword (not a missile); the rifle muzzle points upper-left like the sword tip.
- [x] 8.6 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 9. Revert teleport, fix lift-clip, redesign sword + rifle

- [x] 9.1 Revert `<Teleport>` overlay back to a simple `position: absolute; left: 100%` pane revealed by CSS `:hover` + `@media (hover: hover)`. Drop the JS hover state (cardEl ref, isHovering, overlayPos) and `mouseenter` handler. Keep the per-pane border so the absolute reveal still reads as a distinct surface.
- [x] 9.2 Remove `transform: translateY(-3px)` on `.hcard:hover` (was causing the card to intrude into sticky filter chrome and get visually clipped). Remove `transform: translateY(-5px)` on `.hcard--selected` and replace with a `box-shadow: 0 0 0 2px` glow ring so the selection still reads but doesn't move the card.
- [x] 9.3 Redesign the `Near` (sword) SVG with a pentagonal/leaf-shaped blade: tip at (8,0), shoulders at (12,5)/(4,5) (8 units wide — the bulge), narrowing back to (10,12)/(6,12) at the crossguard. Longer 12-unit blade overall, wider 2.5-unit hilt for a sturdier handle.
- [x] 9.4 Redesign the `Far` (rifle) SVG as a vertical Kar98k-style bolt-action rifle in source (muzzle at top, smooth wood stock at bottom — no separate pistol grip, no magazine bulge). Apply `transform="rotate(-45 8 8)"` so the muzzle points upper-left in the correct right-side-up orientation.
- [x] 9.5 Verify visually: hovering a deck-list card no longer lifts/intrudes into the filter panel; selection glow ring is visible without translate; the hover preview shows under the filter chrome (acceptable); the sword now reads as a leaf-bladed sword (not a missile); the rifle reads as a Kar98k pointing upper-left, with the stock at the lower-right.
- [x] 9.6 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 10. Outline-only range icons + hover overlay border/z-index/edge-flip

- [x] 10.1 Convert every `CardRangeIcon` SVG to outline-only (`fill="none"` + `stroke="currentColor"` + `stroke-width`), so all four custom glyphs share a consistent wireframe aesthetic with the Σ/∀ Unicode glyphs. The bolt inside the `Instance` triangle and the plus inside `Special` lose their filled style and become outlined.
- [x] 10.2 Lengthen the `Near` sword's blade to ~12.5 units (tip at y=0.5, crossguard at y=13) and widen the horizontal crossguard to 13 units (x=1.5-14.5) so the sword reads as longer and more dominant within the viewBox.
- [x] 10.3 Redesign the `Far` rifle with three distinguishing features now visible in outline form: a front-sight bead at the muzzle (x=7.4-8.6, y=0-0.5), a narrower barrel (x=7.7-8.3), and an asymmetric bolt-handle lug extending to the right of the receiver (x=9-10.5, y=8-9) — so the rifle reads clearly as a bolt-action silhouette.
- [x] 10.4 In `HandCard.vue`, change the absolutely-positioned compact-mode detail pane to `border: 1px solid; border-color: inherit` so the floating overlay adopts whichever dynamic colour the outer `.hcard` is using (ally colour on selected, per-rarity colour at rest, etc.), keeping the floating pane visually continuous with the card.
- [x] 10.5 Raise `z-index: 20` on `.hcard--mode-compact:hover` inside the `@media (hover: hover)` block so the hovered card's overlay paints above subsequent sibling cards in the row, which also have `position: relative`.
- [x] 10.6 Add a minimal `@mouseenter` handler that measures `getBoundingClientRect()` and flips the overlay to the left side (`left: auto; right: 100%`) via a `hcard--flip-left` class when the card is close to the viewport's right edge, preventing the overlay from extending past the page. Gated on `matchMedia("(hover: hover)")` so touch devices don't invoke it.
- [x] 10.7 Verify visually that every range icon reads as an outlined wireframe in both preview and detail contexts; the sword reads as longer; the rifle's bolt-handle and front-sight make it unmistakably a rifle; the hover overlay's border matches the card's border colour; the hover overlay paints in front of neighbouring cards in the row; the overlay flips to the left side for cards near the viewport's right edge rather than overflowing off-screen.
- [x] 10.8 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 11. Icon legibility + grid overflow polish

- [x] 11.1 Bump `.hcard-range` font-size from `0.7rem` to `0.9rem`. At the prior 0.7rem, the glyph rendered at ~10px which collapsed wireframe detail (sword taper, rifle bolt-handle lug, bolt-zigzag inside the `Instance` triangle) into an indistinct blob. 0.9rem gives a ~13-14px glyph while still fitting the header row alongside the cost pill.
- [x] 11.2 Redesign the `Near` sword to push the blade to ~85% of the glyph's vertical extent: tip at (8,0), widest shoulders at (11.5,4)/(4.5,4), narrow to (10,13.5)/(6,13.5) at the crossguard. Crossguard spans x=2-14 at y=13.5-14.5, a slim 2-unit hilt at y=14.5-15.8. The blade dominates the silhouette instead of reading as a short dagger.
- [x] 11.3 In `HandCard.vue`, widen the `flipLeft` detection beyond just the viewport's right edge: walk up the DOM from `cardEl.value` and pick the first ancestor whose `overflow-x` is not `visible` (or fall back to the viewport). The overlay now flips to the left side before it would extend past the nearest scroll-clipping container — in DeckTab, that means flipping before crossing the card-grid boundary into the equipped-deck panel.
- [x] 11.4 In `DeckTab.vue`, change `.card-grid` from `overflow-y: auto` to the shorthand `overflow: hidden auto`. Without explicit overflow-x, the browser promotes it to `auto` alongside overflow-y, which surfaces a horizontal scrollbar whenever the compact-mode hover overlay pokes past the right edge. `hidden auto` keeps vertical scrolling while silently clipping horizontal overflow, and the flip logic from 11.3 ensures the overlay is actually visible (not clipped) whenever it's shown.
- [x] 11.5 Verify visually that the sword now reads as notably longer than before; range icons are crisply legible at the preview size; no horizontal scrollbar appears on the card-grid in the Add Cards screen; the hover overlay never renders behind the equipped-deck panel (it flips to the left side instead).
- [x] 11.6 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 12. Sword hilt proportions

- [x] 12.1 Rebalance the `Near` sword: shorten the blade from 13.5 to 12 units (narrow points at y=12 instead of y=13.5) and extend the hilt to 2.5 units (y=13 → y=15.5) so the handle carries visible weight instead of reading as a stub below the crossguard. Blade is now ~77% of total height — still dominant but no longer overwhelming.
- [x] 12.2 Verify visually that the hilt below the crossguard is now clearly a handle (not a tiny nub) and the blade still reads as the dominant feature.
- [x] 12.3 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 13. Scale up card preview so text is not dwarfed by surrounding UI

- [x] 13.1 Bump the preview pane from `width: 4rem` to `width: 5.5rem` (5:7 aspect preserved → 7.7rem tall). Every text/glyph size in the card was previously sub-UI-baseline; at this width internal elements can scale to a comfortable reading size without layout churn.
- [x] 13.2 Scale preview elements proportionally (~1.3-1.4x): cost pill 1.1rem → 1.5rem (font 0.68rem → 0.95rem); name 0.58rem → 0.8rem; range glyph 0.9rem → 1rem; die icon 0.85rem → 1.15rem; token icon 0.75rem → 1rem; token stack 0.4rem → 0.55rem; count badge 0.46rem → 0.62rem.
- [x] 13.3 Bump the detail pane from `width: 6.5rem` to `width: 9rem` so wrapped desc lines remain readable at the larger font sizes. Ability desc 0.5rem → 0.68rem; die range 0.55rem → 0.75rem; die desc 0.46rem → 0.62rem. Ease line-height from `1` to `1.05-1.1` on wrapped-paragraph text so the bigger glyphs don't touch the lines above/below.
- [x] 13.4 Update `OVERLAY_WIDTH_PX` in `HandCard.vue` from 110 to 150 to match the detail pane's new 9rem width (144px + margin).
- [x] 13.5 Verify visually: card text is no longer visibly smaller than the rest of the UI chrome; range glyphs are legible at a glance (sword reads as a sword, rifle reads as a bolt-action); the Add Cards grid still fits a reasonable number of cards per row on typical desktop viewports; battle-mode (full) cards still fit the hand row without horizontal scrolling.
- [x] 13.6 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.

## 14. Preview name fit + dice spacing tune

- [x] 14.1 Drop `.hcard-name` font-size from `0.8rem` to `0.7rem` so 13-character names ("Unforgettable") fit the 5.5rem preview on a single line. Add `overflow-wrap: break-word` as a safety net for any outlier longer name.
- [x] 14.2 Tighten `.hcard-dice-icons` gap from `0.08rem 0.06rem` to `0.04rem 0.02rem` so the dice icons read as a grouped strip rather than individually-spaced icons.
- [x] 14.3 Verify visually that "Unforgettable" and other long names no longer wrap mid-word or spill out of the preview, and the preview dice strip reads as a single compact row.
- [x] 14.4 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.
