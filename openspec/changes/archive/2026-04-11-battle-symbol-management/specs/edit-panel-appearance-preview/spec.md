## MODIFIED Requirements

### Requirement: Preview reflects active fashion projection

The `AppearancePreview` rendered in the Edit Panel SHALL display the currently active fashion book composite when `customBookId` is set and a matching entry exists in `state.fashionBooks`. The preview SHALL also render equipped and visible gift sprites overlaid on the character composite.

#### Scenario: Fashion projection shown in sidebar
- **WHEN** the librarian has a `customBookId` that matches a `FashionBook` in `state.fashionBooks`
- **THEN** the preview shows the fashion body composite instead of the default body

#### Scenario: Equipped visible gifts shown on preview
- **WHEN** the librarian has gifts equipped with `visible: true`
- **THEN** the preview renders each gift as a `.layer-sprite.gift-layer` div using the same CSS stacking as face/hair layers (`position: absolute; inset: 0; background-size: 100% auto`)
- **AND** each gift PNG is pre-rendered onto the shared face canvas by `GiftCache` using `AppearanceCache.SpriteToPng` with the gift's prefab transform offset, so no client-side coordinate conversion is needed
- **AND** gift layers apply the same `faceRotStyle` as face/hair layers for head-tilt alignment

#### Scenario: Hidden gifts not shown on preview
- **WHEN** an equipped gift has `visible: false`
- **THEN** the gift sprite is not rendered on the preview

#### Scenario: Gifts hidden when fashion body replaces head
- **WHEN** the active fashion book has `replacesHead: true`
- **THEN** gift overlays are hidden along with face/hair layers (`v-show="showFaceHairLayers"`)
