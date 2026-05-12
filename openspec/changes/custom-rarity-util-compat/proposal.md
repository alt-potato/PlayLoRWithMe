## Why

The CustomRarityUtil workshop mod (id 2874916185) lets other mods declare additional combat-page, key-page, and passive rarities with custom colours. Today our serializer emits only the rarity *name* (e.g. `"Common"`, `"Unique"`), so a workshop unit whose passive is declared with a custom rarity renders with no visible rarity affordance — the frontend has no way to colour an unknown rarity. Players who run common workshop content (chapter mods, page packs) lose a glanceable cue that's present in vanilla LoR.

## What Changes

- The C# serializer queries `CustomRarityUtil.Xml.CardRarityXmlList` via a direct API call and, when the mod is loaded and a card/key-page/passive has a custom rarity, emits four new optional override hex strings: `rarityColor`, `rarityRangeIconColor`, `rarityAbilityColor`, `rarityKeywordColor`.
- Vanilla rarities are unchanged on the wire — no override fields are emitted, and the frontend continues to use the existing `--rarity-common`/`--rarity-uncommon`/etc. tokens.
- Frontend rarity styling unifies onto a single `--rarity-color` inline-var pattern (plus `--rarity-range-icon-color`, `--rarity-ability-color`, `--rarity-keyword-color`). `PassiveList`'s class-based approach migrates to the inline-var approach already used by `KeyPageDetail`, `KeyPageTab`, and `PassivesTab`.
- Schema (`game.ts`) extends `CardSchema`, `PassiveSchema`, `KeyPageSchema`, `AvailableKeyPageSchema`, `AvailableCardSchema`, `DeckCardPreviewSchema`, and `SlottedCardEntrySchema` with the four optional override fields.
- The C# mod takes a HintPath reference to `CustomRarityUtil.dll` for type-safe API access (`Private=False`, so the optional DLL is never bundled). The runtime soft-dep guarantee is enforced via an assembly-presence check that gates a non-inlined CDC-using method — when CustomRarityUtil is absent the CLR never JITs the gated method and never resolves its types, so the mod stays loadable.
- **Out of scope** (explicitly): range icon image swaps, frame artwork swaps, `FrameEffect` (Rainbow/Glow), and `FrameLinearColor`.

## Capabilities

### New Capabilities

- `custom-rarity-compat`: the contract for emitting and consuming custom-rarity colour overrides — reflection probe behaviour, payload shape, fallback semantics, and what the frontend does when a payload carries override hexes versus when it does not.

### Modified Capabilities

- `color-tokens`: extends the `:root` token set with `--rarity-range-icon-color`, `--rarity-ability-color`, and `--rarity-keyword-color` defaults; introduces the convention that rarity-styled surfaces read these via inline-set sibling vars rather than per-rarity classes.
- `key-page-rarity-indicator`: the existing `--rarity-border` inline-var pattern is renamed/aligned to `--rarity-color` and gains override-via-hex semantics; tile-selection and equipped-indicator rules unchanged.
- `combat-card-display`: range glyph picks up `--rarity-range-icon-color` when set, ability/keyword text picks up `--rarity-ability-color`/`--rarity-keyword-color` when set, otherwise unchanged.
- `card-keyword-highlighting`: keyword colour reads `--rarity-keyword-color` when set on the card surface, falling back to the existing bright-gold default.
- `wire-contract-schema`: schema admits four new optional fields on the relevant card/key-page/passive shapes.

## Impact

- **C#**: new `CustomRarityProbe` helper (assembly-presence gate + non-inlined CustomRarityUtil-typed lookup); ~four emission sites in `GameStateSerializer.cs` add optional override hex output; new HintPath csproj reference to `CustomRarityUtil.dll` (Private=False so the optional DLL is not bundled); DLL size delta within the existing wire-contract-schema budget.
- **Frontend**: `PassiveList.vue` refactors from class-based rarity styling onto the inline-var pattern; `HandCard`/`SlottedCard`/`CardDetail`/`CardRangeIcon` accept the new override vars; `app.vue` gains the new default tokens. No public API changes; all override fields are optional, so older snapshots and battle-context payloads continue to render unchanged.
- **Tests**: extend `useBattleDisplay.test.ts` (or add a new helper test) for the rarity-style override resolution; fixture updates to exercise the override path in `battle-sampler.json`.
- **Dev workflow**: no new dependencies; build still completes via `cd mod && dotnet build`. Players without CustomRarityUtil installed see no change.
