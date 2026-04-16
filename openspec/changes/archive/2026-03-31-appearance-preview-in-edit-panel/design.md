## Context

`EditPanel.vue` is a full-screen overlay that lets a player rename, re-equip, and re-deck a librarian across three tabs (Key Page, Deck, Info). The `AppearancePreview` component already exists and accepts `appearance`, `fashionBook`, and `appearanceType` props. `LibrarianEntry` already carries `appearance`, `appearanceType`, `customBookId`, and the relevant fashion book data is available by looking up `customBookId` in `state.fashionBooks`. The only missing piece is surfacing the preview inside `EditPanel`.

On desktop (≥700 px) the panel renders as a centered modal (`max-width: 900px`, `80dvh`). On mobile it's a bottom-sheet (`92dvh`). The current layout is a single-column flex column in both cases.

## Goals / Non-Goals

**Goals:**
- Show `AppearancePreview` in the Edit Panel for any librarian that has `appearance` data.
- On mobile: render it on the Info tab only, above the name/customize controls.
- On desktop: render it as a fixed-width left column so it persists across all three tabs.
- Reuse `AppearancePreview` as-is; no changes to that component.

**Non-Goals:**
- Showing the preview in the battle or stage view.
- Adding any new interactive controls to the preview within `EditPanel` (those remain in `CustomizePanel`).
- Changing the preview for librarians that have no `appearance` data (no-op, component is omitted).

## Decisions

### Decision: Info-tab-only on mobile, sidebar on desktop

**Alternatives considered:**
- *Always show a top strip across all tabs on mobile* — wastes 160 px of vertical space on the Key Page and Deck tabs, which are already content-dense.
- *Only show on Info tab everywhere* — simpler, but misses the "always visible on all tabs" desire expressed for wider viewports.

**Chosen:** Responsive split. CSS media query (`min-width: 700px`) switches the panel body from a single-column flex column to a two-column grid (`160px 1fr`). The preview column is hidden via `display: none` below that breakpoint; on mobile the same component renders inline inside the info tab markup via a separate `v-if` branch.

To avoid mounting the component twice, the desktop sidebar slot uses `v-show` keyed on `activeTab` availability, while the mobile info-tab inset uses a plain conditional: `v-if="!isDesktop && activeTab === 'info'"`. A `isDesktop` computed ref reads a `matchMedia` watcher so Vue reactivity drives visibility without duplicating the component.

Actually, a simpler approach: render the sidebar `<AppearancePreview>` unconditionally inside a `.preview-sidebar` div that is `display: none` on mobile via CSS, and render a second instance inside the Info tab that is `display: none` on desktop. Both instances are lightweight (just CSS backgrounds) and the sprite URLs are already cached by the browser after the first render. This avoids matchMedia JS wiring entirely.

### Decision: Fashion book lookup stays local to EditPanel

`EditPanel` already receives `state` as a prop. Derive the active `fashionBook` as a computed:
```ts
const activeFashionBook = computed(() =>
  props.lib.customBookId != null && props.lib.customBookId >= 0
    ? (props.state.fashionBooks ?? []).find(fb => fb.id === props.lib.customBookId) ?? null
    : null
);
```
No new props or emits needed.

## Risks / Trade-offs

**Duplicate DOM elements for preview** → Two `<AppearancePreview>` instances (one for sidebar, one for mobile info tab). Both are CSS-only renders and share cached sprite images. The cost is two extra DOM nodes with identical background-image declarations — negligible. Consolidating into one instance would require JS-driven visibility which adds more complexity than it removes.

**Preview absent for librarians without `appearance`** → `lib.appearance` is optional; when absent the sidebar column and info-tab inset are simply not rendered. The layout shifts (no wasted empty column) via `v-if` on the outer wrapper.
