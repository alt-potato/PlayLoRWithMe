## MODIFIED Requirements

### Requirement: Preview reflects active fashion projection

The `AppearancePreview` rendered in the Edit Panel SHALL display the currently active fashion book composite when `customBookId` is set and a matching entry exists in `state.fashionBooks`. The preview SHALL also render equipped and visible gift sprites overlaid on the character composite.

#### Scenario: Fashion projection shown in sidebar
- **WHEN** the librarian has a `customBookId` that matches a `FashionBook` in `state.fashionBooks`
- **THEN** the preview shows the fashion body composite instead of the default body

#### Scenario: Equipped visible gifts shown on preview
- **WHEN** the librarian has gifts equipped with `visible: true`
- **THEN** the preview renders each gift sprite at a per-position anchor (`GIFT_LAYOUT`) using absolute CSS positioning with `transform: translate(-50%, -50%)` centering
- **AND** gift wrappers apply the same `faceRotStyle` as face/hair layers for head-tilt alignment

#### Scenario: Hidden gifts not shown on preview
- **WHEN** an equipped gift has `visible: false`
- **THEN** the gift sprite is not rendered on the preview

#### Scenario: Gifts hidden when fashion body replaces head
- **WHEN** the active fashion book has `replacesHead: true`
- **THEN** gift overlays are hidden along with face/hair layers (`v-show="showFaceHairLayers"`)
