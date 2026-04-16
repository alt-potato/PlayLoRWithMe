## 1. Rename Light resource fields in JSON and TypeScript

- [x] 1.1 In `mod/GameStateSerializer.cs` (lines 1299–1301), rename JSON string literals: `"playPoint"` → `"light"`, `"maxPlayPoint"` → `"maxLight"`, `"reservedPlayPoint"` → `"reservedLight"`
- [x] 1.2 In `frontend/app/types/game.ts` (`AllyUnit` interface, lines 319–321), rename fields: `playPoint` → `light`, `maxPlayPoint` → `maxLight`, `reservedPlayPoint` → `reservedLight`
- [x] 1.3 In `frontend/app/components/unit/DisplayCard.vue` (lines 154–156), update template bindings: `ally.playPoint` → `ally.light`, `ally.maxPlayPoint` → `ally.maxLight`, `ally.reservedPlayPoint` → `ally.reservedLight`
- [x] 1.4 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`

## 2. Rename AbnormalityPicker to EmotionUpgradePicker

- [x] 2.1 Rename file `frontend/app/components/AbnormalityPicker.vue` → `EmotionUpgradePicker.vue` (update the component name comment inside the file header too)
- [x] 2.2 In `frontend/app/components/battle/Stage.vue` (lines 565–566), replace `<AbnormalityPicker` with `<EmotionUpgradePicker` and update the closing tag
- [x] 2.3 In `frontend/app/components/LibrarianManager.vue` (line 360), replace `<AbnormalityPageCard` import/usage if it references `AbnormalityPicker` directly (verify; may be unused here)
- [x] 2.4 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`

## 3. Remove C# class names from frontend comments

- [x] 3.1 In `frontend/app/types/game.ts` line 167: replace `BookModel.instanceId —` with a plain description of why the field is optional
- [x] 3.2 In `frontend/app/types/game.ts` line 223: replace `DiceCardXmlInfo` with "combat card definition"
- [x] 3.3 In `frontend/app/types/game.ts` lines 555 and 557: replace `Raw C# StageController.Phase class name` / `Raw C# StageController.State enum value` with plain descriptions
- [x] 3.4 In `frontend/app/components/librarian/DeckTab.vue` line 42: replace `BookModel.AddCardFromInventoryToCurrentDeck range checks` with a plain description of the constraint
- [x] 3.5 In `frontend/app/components/librarian/DeckTab.vue` line 57: replace `DiceCardXmlInfo.GetCardLimit` with a plain description of the per-rarity limit rule
- [x] 3.6 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`
