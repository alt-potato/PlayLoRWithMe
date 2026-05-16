## Context

The in-game `RoundEndPhase_ChoiceEmotionCard` resolves a per-act level-up sequence through `StageLibraryFloorModel.StartPickEmotionCard`, which dispatches between two branches based on `team.skillPoint` and `team.egoSelectionPoint`:

```csharp
// abbreviated
if (skillPoint-equivalent > 0) {
    var list = CreateSelectableList(currentSelectEmotionLevel);
    if (list.Count > 0) ui_levelup.Init(_selectedList.Count, list);
} else if (team.egoSelectionPoint > 0) {
    var list2 = CreateSelectableEgoList();
    if (list2.Count > 0) ui_levelup.InitEgo(_selectableEgoList.Count, list2);
}
```

The mod currently patches only `LevelUpUI.Init` and `OnPickPassiveCard`, so the abnormality branch is fully wired. The EGO branch has zero coverage: `InitEgo` and `OnPickEgoCard` are uninstrumented, no state container exists, no wire payload is emitted, and `ActionInjector` has no handler.

`EmotionEgoXmlInfo` is a thin XML shape — `id`, `_CardId` (`LorId`), `Sephirah`, `isLock`. The interesting metadata (name, cost, range, rarity, ability description, dice faces) lives on the `BattleDiceCardModel` resolved from `_CardId` via the standard card-detail XML lists. The frontend already renders this metadata for hand cards via `BattleDiceCardSchema`, so we can reuse the shape.

`OnPickEgoCard` is simpler than `OnPickPassiveCard`:

```csharp
public void OnPickEgoCard(EmotionEgoXmlInfo egoCard)
{
    if (team.egoSelectionPoint <= 0) return;
    team.egoSelectionPoint--;
    if (egoCard != null) {
        _selectedEgoList.Add(egoCard);
        Singleton<SpecialCardListModel>.Instance.AddCard(egoCard.CardId, _floorModel.Sephirah);
        if (team.skillPoint > 0) { _selectableList = ...; _selectableEgoList = ...; }
        team.SetMaxEgoCooltimeCoin();
        team.UpdateEgoCooltimeCoin();
    }
}
```

No target argument. The mod's `selectEgo` handler only needs the choice's XmlInfo `id` to look it up in the current selectable list.

## Goals / Non-Goals

**Goals:**

- Surface EGO selection prompts to the frontend with enough metadata to render the EGO choice (card name, cost, dice, range, rarity floor color, Sephirah icon).
- Allow remote players to commit an EGO pick via a new `selectEgo` action.
- Reuse the `EmotionUpgradePicker` chrome (backdrop, header, team-emotion bar) to keep the frontend overlay surface consistent.
- Cover the new wire shape with the existing reference-fixture drift test.

**Non-Goals:**

- Modifying the abnormality selection flow (covered by `stop-abnormality-select-loop-sound` and the existing wire contract).
- Re-skinning the picker. The visual chrome stays as-is; only the card grid switches based on selection mode.
- Adding any cross-floor EGO equip management UI (the floor's `SpecialCardListModel` ego hand display lives in `LibrarianManager` / floor display and is out of scope here).
- Adding fixture for `team.skillPoint > 0 && team.egoSelectionPoint > 0` edge case where StartPickEmotionCard alternates — the existing patches handle it because each `Init`/`InitEgo` call is independently observed.

## Decisions

### D1: Separate `egoSelection` wire field, not a polymorphic `emotionSelection`

Add a sibling top-level optional field `egoSelection` alongside `abnormalitySelection`, with its own schema (`EgoSelectionSchema`). Do not generalize into a polymorphic `emotionSelection` with a discriminator.

**Why:**
- Choice payloads diverge materially: EGO choices carry battle-card data (`cost`, `dice`, `range`, `rarity`, `sephirah`), abnormality choices carry script-keyed XML (`targetType`, `state`, `desc`, `flavorText`). Cramming both behind one shape creates large optional/nullable surfaces.
- The game's own code separates them (`Init` vs `InitEgo`, `OnPickPassiveCard` vs `OnPickEgoCard`); mirroring that structurally on the wire makes the mod-side serializer trivially obvious.
- Mutually exclusive at runtime (`StartPickEmotionCard` only opens one at a time), so two optional fields don't add ambiguity — at most one is populated per snapshot.

**Alternatives considered:**

- **Unified `emotionSelection: { mode: "abnormality" | "ego", choices: …, … }`.** Smaller top-level state surface but forces both sides to handle the discriminator and complicates Zod schemas (each `mode` value gates which optional fields are present). Rejected.

### D2: EGO choice payload mirrors `BattleDiceCardSchema` + EGO metadata

`EgoChoiceSchema` carries:

- `id: number` — `EmotionEgoXmlInfo.id`, used as the `choiceId` in the `selectEgo` action.
- `cardId: EntryId` — the resolved `LorId` of the EGO card, for sprite/asset lookup.
- `name: string` — card display name (`BattleDiceCardModel.GetName()`).
- `cost: number` — light cost from the resolved card.
- `range: CardRange` — same `Near|Far|FarArea|FarAreaEach|Special|Instance` enum.
- `rarity: string` — same string-rarity convention used by `CardSchema`. EGO cards use `ZAYIN|TETH|HE|WAW|ALEPH` floor names.
- `sephirah: string` — `EmotionEgoXmlInfo.Sephirah.ToString()`. Useful for floor-color icons.
- `dice: DieFace[]` — same shape as `BattleDiceCardSchema.dice`. Lets the picker show dice faces without a separate detail tab.
- `desc?: string` — ability description if non-empty.

**Why:** the picker should show enough to make an informed choice — cost, dice, range, rarity, name. The shape reuses `DieFaceSchema` so the frontend can use existing dice-rendering components.

**Alternatives considered:**

- **Embed a full `BattleDiceCard` per choice.** Over-fetches fields the picker doesn't show (`abilityDesc`, etc.). Marginal cost, but the leaner shape keeps the wire payload focused.
- **Just `id` + `name` + `desc`, defer the rest to a click-through detail.** Picker becomes ambiguous — players can't compare options without extra clicks. Rejected.

### D3: Frontend rendering — extend `EmotionUpgradePicker`, not a sibling component

Modify `EmotionUpgradePicker.vue` to accept either `abnormalitySelection` OR `egoSelection` (one optional prop each, exactly one populated at a time) and switch the card-grid rendering inside the existing chrome.

**Why:** the chrome (backdrop, panel, header with team emotion stats, click-outside cancel) is identical between modes. Duplicating the chrome into a sibling `EgoUpgradePicker` would force coordinated maintenance for any chrome change.

The card-grid divergence (different content per tile, no SelectOne target step for EGO) is handled by a small mode-switch (`v-if`/component dispatch) inside the panel.

**Alternatives considered:**

- **Sibling `EgoUpgradePicker.vue`.** Clean separation but duplicates ~80% of the layout. Rejected.
- **Generic `EmotionPicker` that takes a "card renderer" slot.** Cleaner architecturally but overkill for two modes that share a stable chrome. Rejected.

### D4: New `selectEgo` action, separate from `selectAbnormality`

Add a new `ClientAction` variant: `{ type: "selectEgo", choiceId: number }`. The handler resolves `choiceId` against `EgoSelectionState.Choices` and calls `floor.OnPickEgoCard(choice)`.

**Why:**

- The payload shapes differ (`selectAbnormality` carries optional `targetUnitId`; `selectEgo` does not). A discriminator-on-`selectAbnormality` design would require optional fields that mean different things, churning the existing handler.
- The mod-side state machines are distinct (`AbnormalitySelectionState` vs `EgoSelectionState`); a single handler that has to check both would be branchier than two simple handlers.

**Field naming**: `choiceId` rather than `cardId`. The action references the XmlInfo `id`, not the resolved `BattleDiceCardModel`'s `LorId`. Naming the field `cardId` would be misleading because `EgoChoiceSchema.cardId` is the resolved card's `EntryId`.

### D5: Dismissal — reuse `OnSelectHide(force: true)`

After `floor.OnPickEgoCard(...)`, dismiss `LevelUpUI` via `OnSelectHide(force: true)` — same pattern as `stop-abnormality-select-loop-sound` proposes for the abnormality path.

**Why:**

- `LevelUpUI.InitEgo` does NOT play `_loopSound` (only `Init` does), so there is no loop-sound bug on the EGO path. But `InitEgo` still calls `InitBase`, which sets up the canvas. The same `OnSelectHide` exit path mirrors the in-game post-pick flow (the `OnSelectEgo` / `OnSelectEgoCard` handlers also start `OnSelectRoutine` which dismisses via `DisableRoutine` → `SetRootCanvas(false)`).
- Even though the loop sound isn't an issue here, using the same exit method as the abnormality path means both action handlers behave consistently with the in-game UI.

**Compatibility with `stop-abnormality-select-loop-sound`**: if that proposal lands first, this change adopts the same pattern straightforwardly. If this proposal lands first, the abnormality fix is independent and unaffected.

### D6: Patch surface — patch both open and commit, exactly like abnormality

- `[HarmonyPatch(typeof(LevelUpUI), "InitEgo")] Prefix(List<EmotionEgoXmlInfo> egoList)` — sets `EgoSelectionState.IsActive = true`, captures `Choices` and `Floor`, then `Broadcast()`.
- `[HarmonyPatch(typeof(StageLibraryFloorModel), "OnPickEgoCard")] Postfix()` — clears state and `Broadcast()`.

Captures both in-game picks (host clicks an EGO card via the in-game UI) and remote picks (via `selectEgo` action — which calls `OnPickEgoCard` and so triggers the same Postfix). One code path, two trigger sources, as already established for abnormality.

## Risks / Trade-offs

- **`BattleDiceCardModel.GetName()` / cost / dice availability for an EGO card not yet in the user's hand.** The serializer resolves these from the XML lists keyed by `LorId`. The same lookup pattern is used for hand cards today, so the data is available. → Validate during apply by serializing a `battle_egoSelection` snapshot and parsing it through `GameStateSchema`.
- **Sephirah enum vs string on the wire.** Serializing as `Sephirah.ToString()` is simpler than re-encoding to a fixed enum on the frontend. The Zod schema uses `z.string()`, which accepts any value. Consumers that want to render a Sephirah icon will need a mapping table (`Malkuth` → MALKUTH icon, etc.). → Pre-build the mapping in `useBattleDisplay` so consumers don't reinvent it.
- **EGO selection while abnormality selection is also active.** `StartPickEmotionCard` only opens one at a time (it returns immediately after `Init` or `InitEgo`). The serializer emits at most one of the two fields per snapshot. → Document the invariant in the spec but don't enforce it at the schema level (both are optional).
- **Reference-state EGO case maintenance.** Adding a `battle_egoSelection` case to `schema/reference-state.json` adds one more fixture to maintain. Cost is low and the wire-contract test enforces non-drift. → Accepted.

## Migration Plan

Additive change. No data migration. No removals. Order of commits:

1. Wire surface (mod patches, state, serializer block, action handler).
2. Schema (Zod types).
3. Frontend rendering (picker extension, Stage mount, BATTLE_CTX action).
4. Reference fixture + dev mock fixture.

Each commit builds independently. Rollback is per-commit revert. No downstream consumers exist yet because the field is new.

## Open Questions

- **EGO ability description source.** `BattleDiceCardModel` exposes the localized name + dice. The full ability description (used in the in-game tooltip) lives in `BattleCardAbilityDescXmlList`. The apply-phase task should verify which lookup keyed by what (script name vs `LorId`) gives the right text — same investigation pattern that uncovered the right source for abnormality `desc`. Documenting as an apply-phase verification step rather than locking it in here.
- **Dev mock fixture: extend `emotion-upgrade.json` or add new `ego-upgrade.json`?** Lean toward a sibling `ego-upgrade.json` so each fixture name maps to a single picker mode. Decided during apply.
