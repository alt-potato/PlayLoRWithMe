## 1. Mod-side wire surface

- [ ] 1.1 In `mod/StateBroadcaster.cs`, add a new `EgoSelectionState` static container (mirroring `AbnormalitySelectionState`) with `IsActive`, `Choices: List<EmotionEgoXmlInfo>`, and `Floor: StageLibraryFloorModel` fields.
- [ ] 1.2 Add `[HarmonyPatch(typeof(LevelUpUI), "InitEgo")] Patch_LevelUpInitEgo.Prefix(List<EmotionEgoXmlInfo> egoList)` that populates `EgoSelectionState` and calls `Broadcast()`. Mirror the abnormality `Patch_LevelUpInit` shape.
- [ ] 1.3 Add `[HarmonyPatch(typeof(StageLibraryFloorModel), "OnPickEgoCard")] Patch_OnPickEgoCard.Postfix()` that clears `EgoSelectionState` and calls `Broadcast()`. Mirror `Patch_OnPickPassiveCard`.
- [ ] 1.4 In `mod/GameStateSerializer.cs`, add a `WriteEgoSelection` block parallel to the existing abnormality block — emit the `egoSelection` field only when `EgoSelectionState.IsActive && EgoSelectionState.Choices != null`. Resolve each `EmotionEgoXmlInfo` to its `BattleDiceCardModel` via the card-detail XML list, then serialize: `id`, `cardId` (EntryId), `name`, `cost` (`GetCost()`), `range` (`GetSpec().Ranges`), `rarity`, `sephirah` (`Sephirah.ToString()`), `dice[]`, optional `desc`. Also emit the team-emotion header (`teamEmotionLevel`, `teamCoin`, `teamCoinMax`, `teamPositiveCoins`, `teamNegativeCoins`) using the same logic the abnormality block uses.
- [ ] 1.5 Build: `cd mod && dotnet build`. Expect `0 Warning(s)  0 Error(s)`.

## 2. Action handler

- [ ] 2.1 In `mod/ActionInjector.cs`, add a `case "selectEgo": ok = DoSelectEgo(r, out error); break;` branch in the action dispatcher.
- [ ] 2.2 Implement `DoSelectEgo(JsonReader r, out string error)`: gate on `EgoSelectionState.IsActive`; require `choiceId: int`; look up the matching `EmotionEgoXmlInfo` in `EgoSelectionState.Choices`; call `floor.OnPickEgoCard(choice)` and `levelup.OnSelectHide(force: true)` for dismissal. Mirror the structure of `DoSelectAbnormality` minus the target argument.
- [ ] 2.3 Build: `cd mod && dotnet build`. Expect `0 Warning(s)  0 Error(s)`.

## 3. Frontend schema and types

- [ ] 3.1 In `frontend/app/types/game.ts`, add `EgoChoiceSchema` carrying `id`, `cardId: EntryIdSchema`, `name`, `cost`, `range: CardRangeSchema`, `rarity`, `sephirah`, `dice: z.array(DieFaceSchema)`, optional `desc`.
- [ ] 3.2 Add `EgoSelectionSchema` carrying `choices: z.array(EgoChoiceSchema)` plus the same five optional team-header fields used by `AbnormalitySelectionSchema`.
- [ ] 3.3 Add `egoSelection: z.optional(EgoSelectionSchema)` to `GameStateSchema`.
- [ ] 3.4 Add `SelectEgoActionSchema = z.object({ type: z.literal("selectEgo"), choiceId: z.number() })` and include it in the `ClientActionSchema` union.
- [ ] 3.5 Run `cd frontend && npm test`. Expect green: the wire-contract drift test triggers `pretest` to regenerate `schema/gamestate.schema.json`, and the new schema fields propagate automatically.

## 4. Frontend rendering

- [ ] 4.1 Decide single-component vs sibling: extend `EmotionUpgradePicker.vue` to accept either `abnormalitySelection?` or `egoSelection?` as alternative props, or split into a sibling `EgoUpgradePicker.vue`. The design doc recommends extending; lock in decision before implementing.
- [ ] 4.2 Implement the EGO card-tile renderer. Surface name, cost (with light-cost glyph), range, rarity (with floor-color affordance), dice faces (reuse `unit/DieRow.vue` or `CardDetail.vue` dice rendering helpers as appropriate). No `targetType` step.
- [ ] 4.3 In `frontend/app/components/battle/Stage.vue`, mount the picker when `state?.egoSelection` is populated. If both `abnormalitySelection` and `egoSelection` happen to be present simultaneously, render only the abnormality picker (matches in-game `StartPickEmotionCard` ordering).
- [ ] 4.4 In `frontend/app/composables/useBattleActions.ts` and `useBattleContext.ts`, add `onSelectEgo(choiceId: number)` to `BattleCtx` and wire it through to a `sendAction({ type: "selectEgo", choiceId })` call.
- [ ] 4.5 Wire the picker's per-tile click handler to invoke `BATTLE_CTX.onSelectEgo(tile.id)`. Add ownership gating consistent with the abnormality path (claimed-units gate).
- [ ] 4.6 Run `cd frontend && npm test`. Confirm green.

## 5. Sephirah helper

- [ ] 5.1 In `frontend/app/composables/useBattleDisplay.ts`, add a `sephirahColor(sephirah: string): string` (or similar) mapping from Sephirah name to the floor's accent color, so the EGO picker tile can render a per-floor affordance without each consumer re-implementing the table. Cover all ten Sephirot.
- [ ] 5.2 Add a unit test for the mapping covering each Sephirah name plus the unknown-input fallback path.

## 6. Reference fixture

- [ ] 6.1 Add a `battle_egoSelection` case to `schema/reference-state.json` with at least two distinct `EgoChoice` entries spanning different `rarity` values, plus populated team-emotion-header fields.
- [ ] 6.2 Run `cd frontend && npm test`. Confirm the reference-fixture test parses the new case cleanly through `GameStateSchema`.

## 7. Dev mock fixture

- [ ] 7.1 Add an EGO-mode fixture under `frontend/app/dev/fixtures/` (either extending `emotion-upgrade.json` or adding `ego-upgrade.json` — apply-phase decision per design doc).
- [ ] 7.2 Verify the dev page can mount the picker in EGO mode using the new fixture. Manual: load the dev page, switch to the EGO fixture, observe the picker.

## 8. Live-game verification

- [ ] 8.1 Manual: in-game, reach an act that grants `team.egoSelectionPoint > 0`. Confirm the frontend `EmotionUpgradePicker` opens in EGO mode with the choices and team-emotion header.
- [ ] 8.2 Manual: pick an EGO card through the frontend. Confirm `_selectedEgoList` and `SpecialCardListModel` (the floor's EGO hand) receive the picked EGO. Confirm the in-game UI dismisses cleanly.
- [ ] 8.3 Manual: trigger an act that grants both `skillPoint > 0` and `egoSelectionPoint > 0`. Confirm both pickers appear in sequence (abnormality first, then EGO).
- [ ] 8.4 Manual: pick an EGO card via the in-game UI directly (not through the frontend). Confirm the `egoSelection` field clears from the broadcast state.
