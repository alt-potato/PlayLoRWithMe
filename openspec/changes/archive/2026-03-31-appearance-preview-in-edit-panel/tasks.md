## 1. EditPanel layout and preview integration

- [x] 1.1 Add `activeFashionBook` computed to `EditPanel.vue` that looks up `lib.customBookId` in `state.fashionBooks`
- [x] 1.2 Add desktop sidebar column: wrap existing `.tab-content` in a two-column grid container gated by `v-if="lib.appearance"`, placing `<LibrarianAppearancePreview>` in the left column and the tab content in the right; apply `display: none` on the sidebar below 700 px via CSS
- [x] 1.3 Add mobile Info-tab inset: render a second `<LibrarianAppearancePreview>` at the top of the Info tab markup, hidden above 700 px via CSS, gated by `v-if="lib.appearance"`
- [x] 1.4 Pass `appearance`, `fashionBook`, and `appearanceType` props to both preview instances

## 2. Validation

- [x] 2.1 Run `cd mod && dotnet build` — expect `0 Warning(s)  0 Error(s)`
