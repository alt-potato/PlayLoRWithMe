## 1. Mod-side wire surface

- [x] 1.1 In `mod/StateBroadcaster.cs`, add a new `EgoSelectionState` static container (mirroring `AbnormalitySelectionState`) with `IsActive`, `Choices: List<EmotionEgoXmlInfo>`, and `Floor: StageLibraryFloorModel` fields.
- [x] 1.2 Add `[HarmonyPatch(typeof(LevelUpUI), "InitEgo")] Patch_LevelUpInitEgo.Prefix(List<EmotionEgoXmlInfo> egoList)` that populates `EgoSelectionState` and calls `Broadcast()`. Mirror the abnormality `Patch_LevelUpInit` shape.
- [x] 1.3 Add `[HarmonyPatch(typeof(StageLibraryFloorModel), "OnPickEgoCard")] Patch_OnPickEgoCard.Postfix()` that clears `EgoSelectionState` and calls `Broadcast()`. Mirror `Patch_OnPickPassiveCard`.
- [x] 1.4 In `mod/GameStateSerializer.cs`, add a `WriteEgoSelection` block parallel to the existing abnormality block. _Cost uses `xml.Spec.Cost` rather than `BattleDiceCardModel.GetCost()` — at selection time the EGO card has no owner so per-owner cost reductions don't apply, and instantiating an ephemeral model just to call `GetCost()` would return the same `Spec.Cost` value. Same pattern used by the inventory-card listing at line ~935._
- [x] 1.5 Build: `cd mod && dotnet build`. Expect `0 Warning(s)  0 Error(s)`.

## 2. Action handler

- [x] 2.1 In `mod/ActionInjector.cs`, add a `case "selectEgo": ok = DoSelectEgo(r, out error); break;` branch in the action dispatcher.
- [x] 2.2 Implement `DoSelectEgo(JsonReader r, out string error)`: gate on `EgoSelectionState.IsActive`; require `choiceId: int`; look up the matching `EmotionEgoXmlInfo` in `EgoSelectionState.Choices`; call `floor.OnPickEgoCard(choice)` + dismiss the level-up UI. _Dismissal uses `levelup.SetRootCanvas(false)` (snap-dismiss, same final call the in-game `DisableRoutine` ends with). Originally drafted with `OnSelectHide(force: true)` per design D5, but that path swaps the panel to the "please select a librarian" `cardHidingGroup` which strands the EGO UI in the wrong state — found during live-game verification._
- [x] 2.3 Build: `cd mod && dotnet build`. Expect `0 Warning(s)  0 Error(s)`.
- [x] 2.4 **Live-fix:** Add `"selectEgo"` to the WebSocket action dispatcher in `mod/Server.cs:309`. Without this case, the server silently dropped the action via the `default:` ("Unknown WebSocket message type") branch and the frontend timed out after 5s waiting for `actionResult`. Apply-phase regression — the abnormality flow added a case here originally and the EGO change initially missed mirroring it.

## 3. Frontend schema and types

- [x] 3.1 In `frontend/app/types/game.ts`, add `EgoChoiceSchema` carrying `id`, `cardId: EntryIdSchema`, `name`, `cost`, `range`, `rarity`, `sephirah`, optional `dice: z.array(DieSchema)`, optional `desc`. _Used `range: z.string()` and `dice: z.array(DieSchema)` (existing convention; the schema doesn't define `CardRangeSchema`/`DieFaceSchema` as named exports — `CardSchema` itself uses plain string ranges and `DieSchema`)._
- [x] 3.2 Add `EgoSelectionSchema` carrying `choices: z.array(EgoChoiceSchema)` plus the same five optional team-header fields used by `AbnormalitySelectionSchema`.
- [x] 3.3 Add `egoSelection: z.optional(EgoSelectionSchema)` to `GameStateSchema`.
- [x] 3.4 Add `selectEgo` action variant `{ type: z.literal("selectEgo"), choiceId: z.number() }` to the `ClientActionSchema` discriminated union.
- [x] 3.5 Run `cd frontend && npm test`. _All 133 tests green; pretest regenerated `schema/gamestate.schema.json`._

## 4. Frontend rendering

- [x] 4.1 Decided: **extend** `EmotionUpgradePicker.vue` to accept either `abnormalitySelection?` or `egoSelection?` as optional props. Header chrome is identical; only the inner card grid / ally-target step differs. Matches design D3.
- [x] 4.2 EGO mode reuses `HandCard.vue` with `displayMode="full"` (same card layout used everywhere else in the app), so the per-die descriptions render at rest without needing the hover overlay. Each tile gets a small Sephirah label below it via `sephirahColor`. Picker projects each `EgoChoice` into a `Card` shape via a local `egoChoiceToCard()` helper, synthesizing `options: ["Ego"]` so `cardBorderColor` paints the crimson EGO accent. _Initially built a bespoke `EgoChoiceCard.vue` sibling tile, but it diverged from the rest of the app and didn't surface per-die descriptions — replaced with HandCard during apply per user feedback._
- [x] 4.3 In `frontend/app/components/battle/Stage.vue`, picker now mounts when **either** selection field is populated. Both fields are passed through; the picker's internal `mode` computed prefers `abnormalitySelection` when both are present (matches in-game `StartPickEmotionCard` ordering, per spec defensive scenario).
- [x] 4.4 Added `onSelectEgo(choiceId)` to `useBattleActions` (dispatches `{ type: "selectEgo", choiceId }`) and to the `BattleCtx` interface in `useBattleContext.ts`. Wired through `Stage.vue` to `provide(BATTLE_CTX, ...)`.
- [x] 4.5 Picker's EGO-mode tiles invoke an internal click handler that emits `selectEgo({ choiceId })`; `Stage.vue` routes that to `onSelectEgo`. _Ownership gating matches the abnormality path — neither path applies a client-side claims gate; server-side `IsAuthorized` is the gate, which the existing abnormality flow also relies on._
- [x] 4.6 Run `cd frontend && npm test`. _All 136 tests green._

## 5. Sephirah helper

- [x] 5.1 **Withdrawn during apply.** Added `sephirahColor`/`SEPHIRAH_COLORS` then removed when the Sephirah label was dropped from the EGO picker per user feedback — HandCard's existing chrome was enough; the per-floor accent added visual noise without payoff. `sephirah` is still emitted on the wire and present on `EgoChoice` (no client renders it currently, but it's cheap to keep for future tooltips).
- [x] 5.2 **Withdrawn during apply.** Removed alongside §5.1 — the tests covered code that no longer exists.

## 6. Reference fixture

- [x] 6.1 Added `battle_egoSelection` case to `schema/reference-state.json` with two distinct `EgoChoice` entries spanning different rarities (ZAYIN single-die EGO and WAW two-die EGO), distinct Sephirot, and populated team-emotion-header fields. _Bug found and fixed while validating against `GameStateSchema`: the initial `EgoChoiceSchema` declared `cardId: EntryIdSchema` (numeric packageId), but the C# `LorId.packageId` is always a string. Switched to `StringEntryIdSchema` to match wire shape — this is what the inventory contexts already use._
- [x] 6.2 Run `cd frontend && npm test`. _137 tests green (134 prior + 3 sephirah)._

## 7. Dev mock fixture

- [x] 7.1 Added `ego-upgrade.json` as a sibling of `emotion-upgrade.json` (design doc preference — each fixture maps to a single picker mode). Two EGO choices spanning ZAYIN/WAW rarities, different Sephirot, one/two dice, and populated team-emotion header. Registered in `FIXTURE_LOADERS`. _The fixture-parse test in `index.test.ts` validates it through `GameStateSchema` automatically._
- [x] 7.2 Manual dev-page verification — confirmed working alongside live-game §8 verification.

## 8. Live-game verification

- [x] 8.1 Manual: confirmed by user — picker opens in EGO mode with the choices and team-emotion header.
- [x] 8.2 Manual: confirmed by user — frontend pick commits cleanly and the in-game UI dismisses.
- [x] 8.3 Manual: confirmed by user — abnormality and EGO pickers appear in sequence.
- [x] 8.4 Manual: confirmed by user (implicit — "everything works" after the apply-phase fixes).
