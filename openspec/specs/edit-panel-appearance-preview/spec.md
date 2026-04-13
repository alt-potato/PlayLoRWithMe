# Spec: Edit Panel Appearance Preview

The Edit Panel SHALL display an `AppearancePreview` for any librarian that has `appearance` data, adapting its layout based on viewport width. The preview SHALL reflect the librarian's current `appearance`, active fashion book (derived from `customBookId`), and `appearanceType`.

---

## Requirement: Appearance preview shown on Info tab (mobile)

On viewports narrower than 700 px the Edit Panel SHALL render an `AppearancePreview` at the top of the Info tab for any librarian that has `appearance` data. The preview SHALL reflect the librarian's current `appearance`, active fashion book (derived from `customBookId`), and `appearanceType`. The preview SHALL NOT be shown on the Key Page or Deck tabs on mobile.

### Scenario: Preview visible on Info tab (mobile)
- **WHEN** the viewport is below 700 px wide AND the user opens the Edit Panel for a librarian with `appearance` data AND the Info tab is active
- **THEN** `AppearancePreview` is rendered at the top of the Info tab

### Scenario: Preview absent on Key Page tab (mobile)
- **WHEN** the viewport is below 700 px wide AND the Key Page tab is active
- **THEN** no `AppearancePreview` element is present in the visible tab content

### Scenario: Preview absent when no appearance data
- **WHEN** the librarian has no `appearance` field
- **THEN** no `AppearancePreview` element is rendered anywhere in the Edit Panel

---

## Requirement: Appearance preview shown as persistent sidebar (desktop)

On viewports 700 px wide or wider the Edit Panel SHALL render an `AppearancePreview` in a fixed-width left column that remains visible regardless of the active tab, for any librarian that has `appearance` data.

### Scenario: Preview visible across all tabs (desktop)
- **WHEN** the viewport is 700 px wide or wider AND the librarian has `appearance` data
- **THEN** `AppearancePreview` is visible while the Key Page tab is active
- **THEN** `AppearancePreview` is visible while the Deck tab is active
- **THEN** `AppearancePreview` is visible while the Info tab is active

### Scenario: Sidebar column absent when no appearance data (desktop)
- **WHEN** the viewport is 700 px wide or wider AND the librarian has no `appearance` field
- **THEN** the tab content fills the full panel width with no empty sidebar column

---

## Requirement: Preview reflects active fashion projection

The `AppearancePreview` rendered in the Edit Panel SHALL display the currently active fashion book composite when `customBookId` is set and a matching entry exists in `state.fashionBooks`.

### Scenario: Fashion projection shown in sidebar
- **WHEN** the librarian has a `customBookId` that matches a `FashionBook` in `state.fashionBooks`
- **THEN** the preview shows the fashion body composite instead of the default body
