## Why

The `AppearancePreview` component exists but is only visible deep inside the full-screen `CustomizePanel`. When a player opens the Edit Panel to check a librarian's key page or deck, they have no quick way to see what that librarian currently looks like — including whether a fashion projection is active.

## What Changes

- The `AppearancePreview` component is added to the Edit Panel, visible while any tab is active.
- On the Info tab, the preview sits alongside the rename/customize controls.
- On wider viewports (desktop two-column layout), the preview is pinned in a left sidebar so it remains visible across all three tabs (Key Page, Deck, Info).
- On narrow viewports (mobile), the preview appears only on the Info tab to avoid consuming vertical space on the more action-focused Key Page and Deck tabs.

## Capabilities

### New Capabilities

- `edit-panel-appearance-preview`: Appearance preview shown in the Edit Panel — on the Info tab on mobile, and as a persistent left sidebar on desktop.

### Modified Capabilities

*(none — no existing spec requirements are changing)*

## Impact

- `frontend/app/components/librarian/EditPanel.vue` — layout changes to accommodate the preview sidebar (desktop) and Info tab inset (mobile).
- No backend changes required; all data (`lib.appearance`, `lib.appearanceType`, `lib.customBookId`, and the `fashionBooks` lookup from `state`) is already available in the Edit Panel's props.
