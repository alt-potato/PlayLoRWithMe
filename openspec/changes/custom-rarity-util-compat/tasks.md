## 1. C# soft-dependency probe

- [ ] 1.1 Add a HintPath `<Reference Include="CustomRarityUtil">` to `mod/PlayLoRWithMe.csproj` resolving through `$(LorWorkshopDir)\2874916185\Assemblies\CustomRarityUtil.dll`, marked `<Private>False</Private>` so the optional DLL is never bundled
- [ ] 1.2 Create `mod/CustomRarityProbe.cs` with a `HasCru` assembly-presence gate (cached) and a non-inlined `LookupOverride(string packageId, Rarity rarity)` method that calls `Singleton<CardRarityXmlList>.Instance.GetCardRarityXmlInfo` directly and reads the four colour properties off the returned `CardRarityXmlInfo`
- [ ] 1.3 Define the `RarityOverride` POCO with four `(byte R, byte G, byte B)` tuples; `TryGet(packageId, rarity)` dispatches `HasCru ? LookupOverride(...) : null`
- [ ] 1.4 Confirm the build runs `0 Warning(s) 0 Error(s)` AND that `bin/Debug/PlayLoRWithMe/Assemblies/` does not contain a copy of `CustomRarityUtil.dll`

## 2. Serializer emission

- [ ] 2.1 Add a private helper `WriteRarityColorOverrides(JsonWriter o, string packageId, Rarity rarity)` in `GameStateSerializer.cs` that calls `CustomRarityProbe.TryGet` and, on a non-null result, emits the four `#rrggbb` fields
- [ ] 2.2 Call the helper at each emission site: card serialization (hand/deck/EGO), slotted card serialization, passive serialization, key page serialization (battle + librarian), available key page serialization, available card serialization, deck preview card serialization
- [ ] 2.3 Verify vanilla rarities skip the probe (early return on enum value ≤ `Rarity.Special`); confirm via a unit-test-style fixture round-trip that vanilla card JSON is byte-identical to the pre-change output
- [ ] 2.4 `cd mod && dotnet build` runs `0 Warning(s) 0 Error(s)`

## 3. Schema extension

- [ ] 3.1 Extend `CardSchema`, `SlottedCardEntrySchema`, `PassiveSchema`, `KeyPageSchema`, `AvailableKeyPageSchema`, `AvailableCardSchema`, `DeckCardPreviewSchema` with the four optional `rarity*Color: z.optional(z.string())` fields in `frontend/app/types/game.ts`
- [ ] 3.2 Regenerate `schema/gamestate.schema.json` via `npm run generate-schema`
- [ ] 3.3 Add a wire-contract drift test case asserting that a payload carrying all four overrides parses cleanly; a payload omitting them parses cleanly
- [ ] 3.4 `npm test` (from `frontend/`) passes including the wire-contract drift test

## 4. Frontend: app.vue tokens

- [ ] 4.1 Add `--rarity-range-icon-color`, `--rarity-ability-color`, `--rarity-keyword-color` defaults to `app.vue`'s `:root` block (matching the pre-change visual rendering: gold, text-2, the existing card-keyword-highlighting gold respectively)
- [ ] 4.2 Confirm no component currently sets these names (grep returns zero hits before the migration steps below)

## 5. Frontend: unify rarity styling onto `--rarity-color`

- [ ] 5.1 Refactor `components/unit/PassiveList.vue` from class-based `.rarity-rare` etc. to setting `--rarity-color` inline via `:style`; CSS reads `border-left-color: var(--rarity-color, var(--border))`. Remove the per-rarity class rules
- [ ] 5.2 Rename `--rarity-border` to `--rarity-color` in `components/librarian/KeyPageDetail.vue`, `components/librarian/KeyPageTab.vue`, `components/librarian/PassivesTab.vue` (inline-style binding name + the `var(--rarity-color, var(--border))` references in the matching `<style>` blocks)
- [ ] 5.3 Add a shared helper (likely in `composables/useBattleDisplay.ts` or a new `utils/rarityStyle.ts`) that turns `{rarity, rarityColor, rarityRangeIconColor, rarityAbilityColor, rarityKeywordColor}` into a Vue inline style object containing the four CSS vars; vanilla-rarity lookups resolve through `var(--rarity-<name>)`
- [ ] 5.4 `npm test` passes; visually verify with the existing fixtures (`battle-sampler.json`, `main-librarian.json`, `battle-setting.json`) that the rarity outlines render unchanged for vanilla rarities

## 6. Frontend: combat-card surfaces honour overrides

- [ ] 6.1 `components/HandCard.vue` reads the four `rarity*Color` fields off the `Card` prop and sets them as inline CSS vars on its root element
- [ ] 6.2 `components/SlottedCard.vue` does the same for the slotted card's rarity overrides where applicable
- [ ] 6.3 `components/CardDetail.vue` does the same
- [ ] 6.4 `components/CardRangeIcon.vue` (or inline rule in its caller) reads the glyph foreground colour from `var(--rarity-range-icon-color, var(--gold))`
- [ ] 6.5 Card description body text reads from `var(--rarity-ability-color, <previous default>)` everywhere `abilityDesc` / per-die `desc` is rendered
- [ ] 6.6 The card-keyword-highlighting span colour reads from `var(--rarity-keyword-color, <previous default>)`
- [ ] 6.7 `npm test` passes; visually confirm with fixtures

## 7. Fixture coverage

- [ ] 7.1 Add a synthetic custom-rarity card entry to `frontend/app/dev/fixtures/battle-sampler.json` with all four override fields set to distinguishable hex values
- [ ] 7.2 Add a corresponding custom-rarity passive entry; add a custom-rarity key page entry in `frontend/app/dev/fixtures/main-librarian.json`
- [ ] 7.3 Visually verify the override path in the dev UI (`npm run dev` from `frontend/`, load the fixtures)

## 8. Final validation

- [ ] 8.1 `cd mod && dotnet build` — `0 Warning(s) 0 Error(s)`
- [ ] 8.2 `cd frontend && npm test` — all suites pass including wire-contract drift
- [ ] 8.3 With CustomRarityUtil and one of its consumer mods installed, launch LoR with the local mod build and confirm a custom-rarity page renders with the modder-declared colours; uninstall CustomRarityUtil and confirm vanilla rendering is unchanged
- [ ] 8.4 Update `MEMORY.md` if any non-obvious lookup pattern emerges that future sessions would benefit from knowing
