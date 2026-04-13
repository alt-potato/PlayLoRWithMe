# Spec: Workshop Fashion Projection

The Projection tab's fashion section SHALL support workshop cloth-overlay skins alongside core fashion books, with the two selection types being mutually exclusive. Workshop skins are sourced from `CustomizingResourceLoader` and serialized into game state; the active workshop skin is persisted per librarian.

---

## Requirement: Workshop skins are serialized from CustomizingResourceLoader

Workshop cloth-overlay skins from `CustomizingResourceLoader.GetWorkshopSkinDataAll()` SHALL be serialized as a `workshopSkins` array inside `customizeOptions`. Each entry SHALL include `id` (int), `name` (string), and `contentFolderIdx` (string) fields. This list is not filtered by range type.

### Scenario: Workshop skins are present in state
- **WHEN** the game state is serialized and workshop skins are registered with `CustomizingResourceLoader`
- **THEN** `customizeOptions.workshopSkins` contains one entry per skin with `id`, `name`, and `contentFolderIdx`

### Scenario: No workshop skins installed
- **WHEN** no workshop skins are registered
- **THEN** `customizeOptions.workshopSkins` is an empty array

---

## Requirement: Active workshop skin is serialized per librarian

A librarian's active workshop skin SHALL be serialized as `workshopSkin` (the `contentFolderIdx` string) on the librarian object. This field is omitted when no workshop skin is active.

### Scenario: Librarian with active workshop skin is serialized
- **WHEN** `unit.workshopSkin` is a non-empty string
- **THEN** the librarian object contains `workshopSkin` with that string value

### Scenario: Librarian with no workshop skin omits the field
- **WHEN** `unit.workshopSkin` is empty or absent
- **THEN** `workshopSkin` is omitted from the librarian object

---

## Requirement: setCustomization handler applies workshopSkin

When `setCustomization` is received with a `workshopSkin` key, the handler SHALL set `unit.workshopSkin` to the provided value. An empty string unequips the active workshop skin.

### Scenario: Workshop skin is equipped
- **WHEN** `setCustomization` includes `workshopSkin` with a non-empty `contentFolderIdx`
- **THEN** `unit.workshopSkin` is set to that value

### Scenario: Workshop skin is unequipped
- **WHEN** `setCustomization` includes `workshopSkin` as an empty string
- **THEN** `unit.workshopSkin` is cleared

---

## Requirement: Projection tab shows workshop skins in a separate toggle section

The Projection tab's "X Page Fashion" section SHALL display a Fashion/Workshop toggle. The Fashion tab shows core fashion books filtered by range-type compatibility. The Workshop tab shows `workshopSkins` entries without range-type filtering.

### Scenario: Workshop tab lists available skins
- **WHEN** `customizeOptions.workshopSkins` is non-empty and the Workshop tab is active
- **THEN** each skin appears as a selectable row identified by `contentFolderIdx`

### Scenario: Workshop tab is empty
- **WHEN** `customizeOptions.workshopSkins` is empty and the Workshop tab is active
- **THEN** a "No workshop skins installed" hint is shown

---

## Requirement: Fashion book and workshop skin selections are mutually exclusive

Selecting a fashion book SHALL clear the active workshop skin, and selecting a workshop skin SHALL set `customBookId` to -1 (unequip). A shared "Unequip" control above the toggle clears both.

### Scenario: Workshop skin selected clears fashion book
- **WHEN** the user selects a workshop skin entry
- **THEN** `customBookId` is set to -1 and `workshopSkin` is set to the selected `contentFolderIdx`

### Scenario: Fashion book selected clears workshop skin
- **WHEN** the user selects a fashion book entry
- **THEN** `workshopSkin` is set to empty string and `customBookId` is set to the selected book's id
