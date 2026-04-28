## 1. Wire contract

- [x] 1.1 Add optional `rarity?: string` to `KeyPageSchema` and
  `AvailableKeyPageSchema` in `frontend/app/types/game.ts`. Document via JSDoc
  that the field is omitted on combat-context payloads.
- [x] 1.2 Add `.Add("rarity", book.ClassInfo.Rarity.ToString())` to the
  librarian-owned `keyPage` writer in `mod/GameStateSerializer.cs`
  (around line 484, just before the closing `});` of the `keyPage` object
  inside the librarian floor loop).
- [x] 1.3 Add `.Add("rarity", book.ClassInfo.Rarity.ToString())` to the
  `availableKeyPages` entry writer in `mod/GameStateSerializer.cs`
  (around line 993, alongside `equipRangeType`).
- [x] 1.4 Verify that the BattleSetting `keyPage` writer
  (`GameStateSerializer.cs` ~line 1366) and the in-battle `WriteKeyPage`
  helper (~line 1656) do NOT emit `rarity`. Add a one-line comment at each
  combat site stating that rarity is intentionally omitted.
- [x] 1.5 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`
  and a regenerated `schema/gamestate.schema.json` containing the two new
  optional fields.
- [x] 1.6 Run `cd frontend && npm run check` and `npm test` — expect 0 type
  errors, all existing tests pass.

## 2. Frontend rendering

- [x] 2.1 In `KeyPageTab.vue`, bind `:style="{ borderColor: rarityBorder(kp.rarity) }"`
  on `.kp-tile`. Define a small `rarityBorder` helper inline (or reuse
  `rarityColor` from `useBattleDisplay.ts` with a fallback to `var(--border)`
  when the input is undefined).
- [x] 2.2 In `KeyPageDetail.vue`, bind the same border-color style on
  `.kp-detail`. Confirm visually that the panel keeps padding/spacing.
- [x] 2.3 In `PassivesTab.vue`, bind the same style on `.source-tile`.
- [x] 2.4 Update `frontend/app/dev/fixtures/main-librarian.json` so the
  existing `availableKeyPages` and embedded `keyPage` entries each include
  a `rarity` value covering the five tiers, so dev mode renders the new
  outlines without needing the live game.
- [x] 2.5 Run `cd frontend && npm run check` and `npm test` — expect 0 type
  errors, all existing tests pass.
- [x] 2.6 Manual smoke test in dev mode (`?dev`): open a librarian's
  EditPanel, confirm picker tiles and detail pane show the five distinct
  outline colors. Confirm an equipped tile shows gold left + rarity
  three-sides. Confirm `SettingDetailPanel.vue` is unchanged.

## 3. Validation and archive

- [x] 3.1 Run `cd mod && dotnet build` end-to-end — `0 Warning(s) 0 Error(s)`.
- [x] 3.2 `openspec validate key-page-rarity-indicator --strict` passes.
- [x] 3.3 Manual sign-off in the live game (or against a fresh fixture
  payload), then archive the change with
  `openspec archive key-page-rarity-indicator`.
