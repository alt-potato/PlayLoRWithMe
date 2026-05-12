## Context

CustomRarityUtil (workshop id 2874916185) is a widely-used utility mod that lets other workshop mods declare additional combat-page, key-page, and passive rarities with custom RGB colours, range-icon swaps, and frame effects. It works by Harmony-patching every vanilla UI surface that renders a rarity-driven colour.

Our wire format today emits only the rarity *name* (e.g. `"Common"`, `"Unique"`, `"Special"`). Custom rarities flow through the game as `Rarity` enum values past `Unique` (CustomRarityUtil computes `Rarity = (Rarity)(4 + _RarityID)`), but the frontend has no token mapping for those values, so cards / key pages / passives with a custom rarity render with the default `--border` colour.

The two existing rarity-styling patterns in the frontend are:

- **class-based** (`PassiveList.vue`): `.passive-entry.rarity-rare { border-left-color: var(--rarity-rare); }`, etc.
- **inline-var** (`KeyPageDetail.vue`, `KeyPageTab.vue`, `PassivesTab.vue`): `:style="{'--rarity-border': rarityColorFor(rarity)}"`, with the component reading `var(--rarity-border, var(--border))`.

The inline-var pattern accepts arbitrary colour values natively; the class pattern does not. Unifying onto a single inline-var name (`--rarity-color`) is both necessary for the override path and a worthwhile cleanup.

## Goals / Non-Goals

**Goals:**

- Soft-dependency compatibility with CustomRarityUtil: when the mod is loaded, custom-rarity combat pages, key pages, and passives render with the modder-declared colours; when it is not loaded, the existing vanilla rarity rendering is byte-for-byte preserved.
- Override four colour properties: `FrameColor` (border / left-edge), `RangeIconColor` (range glyph foreground), `AbilityDescColor` (description body text), `AbilityKeywordColor` (bracketed-keyword highlight colour).
- Keep the C# mod free of any hard reference to `CustomRarityUtil.dll`: the project must build and run on systems that do not have the workshop mod installed.
- Unify the frontend rarity-styling pattern onto a single `--rarity-color` inline-var convention, with sibling vars for the other three override surfaces.
- Preserve all existing scenarios from `key-page-rarity-indicator`, `combat-card-display`, `card-keyword-highlighting`, and `color-tokens`.

**Non-Goals:**

- Range icon *image* swaps (`<RangeIcon Range="Near">LeftPage_MeleeAttack</RangeIcon>`). We honour the colour but keep our existing icon mapping.
- Frame artwork swaps (`LeftFrame` / `RightFrame` / `FrontFrame` from CustomRarityUtil).
- `FrameEffect` (Rainbow, Glow) animations — these are visually distracting in a web UI and require significant additional state.
- `FrameLinearColor` (the "page-edge glow" alpha-blend) — no current frontend surface has an analogue.
- Custom rarity behaviour beyond colour: `DeckLimit`, `DropRate`, `DropMax`, `FrameEffect`, and the rest of `CardRarityXmlInfo`. These are gameplay/data concerns that the base game handles correctly without us; we only mirror the visual signal.
- Editing CustomRarityUtil itself or shipping any redistributed copy of its artefacts.

## Decisions

### Reflection-based soft dependency

**Decision:** at mod init, probe `Type.GetType("CustomRarityUtil.Xml.CardRarityXmlList, CustomRarityUtil")`. If the type resolves, cache a `Func<string, Rarity, object>` delegate for `Singleton<CardRarityXmlList>.Instance.GetCardRarityXmlInfo(string, Rarity)` and similar field-accessor delegates for the four `FrameColor` / `RangeIconColor` / `AbilityDescColor` / `AbilityKeywordColor` `Color` properties. Cache once; the hot path is a direct delegate invoke.

**Alternative considered:** Per-call `Type.GetType` + `MethodInfo.Invoke`. Rejected — speed-dice / hand / passive serialization runs on every state push, and several dozen lookups per snapshot × per-call reflection cost is noticeable. Cached delegates make the hot path ~one virtual call.

**Alternative considered:** Hard-reference CustomRarityUtil and rely on Unity's assembly-load tolerance. Rejected — this couples our build to a workshop artefact we don't control, breaks builds for contributors who don't have the workshop installed locally, and would require the workshop mod's load order to be enforced by every player.

### Helper class `CustomRarityProbe`

**Decision:** a new `mod/CustomRarityProbe.cs` exposes a single static method:

```csharp
public static RarityOverride? TryGet(string packageId, Rarity rarity);
```

where `RarityOverride` is a small POCO with four nullable `(byte R, byte G, byte B)` tuples. The helper returns `null` quickly if the type was not found at init; otherwise it invokes the cached delegate and converts the Unity `Color`s to byte tuples.

This keeps the reflection plumbing in one place. `GameStateSerializer` calls it at each emission site and, if the result is non-null, emits the four optional fields via `WriteRgbHex`.

### Hex encoding on the wire

**Decision:** emit `#rrggbb` strings (lowercase, no alpha). Six chars per field, optional — total wire cost when present is ~32 bytes per overridden surface.

**Alternative considered:** Emit `[r, g, b]` integer tuples. Rejected — the frontend would have to convert to CSS colour strings client-side; hex strings are CSS-ready and trivially debuggable.

### packageId resolution

**Decision:** the `packageId` we pass to CustomRarityUtil is the same `packageId` we already carry on cards / passives / key-page entries (a `string` for workshop mods, the empty string `""` for vanilla). When the rarity is vanilla (`Common`..`Special`), we skip the probe entirely.

This means the probe activates *only* for custom rarities, by definition. Vanilla rarities never carry overrides — keeping the wire format unchanged for the common case.

### Frontend: unify on `--rarity-color`

**Decision:** four sibling CSS vars on rarity-styled surfaces:

| Var | Purpose | Default fallback |
|---|---|---|
| `--rarity-color` | left-edge / border colour | per-rarity-class lookup |
| `--rarity-range-icon-color` | `CardRangeIcon` foreground | `--gold` |
| `--rarity-ability-color` | description body text | `--text-2` (or component default) |
| `--rarity-keyword-color` | bracketed-keyword colour | the existing card-keyword-highlighting gold |

Rarity-styled components compute the per-rarity class lookup (e.g. `--rarity-common`) into a single `style="--rarity-color: ..."` inline assignment, optionally overridden by the four payload hex strings when present. The CSS reads `var(--rarity-color, var(--border))`.

**Alternative considered:** Keep `PassiveList`'s class-based pattern and just add a fifth `.rarity-custom` class with an inline `style="border-left-color: #xxxxxx"` override. Rejected — the dual pattern is a smell flagged earlier; one inline-var pattern is simpler to teach, simpler to test, and the migration is small.

### Component coverage

**Decision:** the unify pass touches:

- `PassiveList.vue` — biggest delta (class → inline-var)
- `KeyPageDetail.vue`, `KeyPageTab.vue`, `PassivesTab.vue` — rename `--rarity-border` to `--rarity-color`
- `HandCard.vue`, `SlottedCard.vue`, `CardDetail.vue` — add the four var hooks (rarity colour was previously rendered via `.rarity-tag`; this keeps the chip but also passes the overrides into surrounding panel styles)
- `CardRangeIcon.vue` — reads `--rarity-range-icon-color` for fill/stroke
- `CardFilter.vue` — if it renders rarity chips, gets the same hex-override path

Each component receives the payload's optional override fields as inline-style values; absent payload values mean the default fallback kicks in.

### Schema additions

**Decision:** four optional fields, all `z.optional(z.string())` (hex string), added to:

- `CardSchema`
- `PassiveSchema`
- `KeyPageSchema`
- `AvailableKeyPageSchema`
- `AvailableCardSchema`
- `DeckCardPreviewSchema`
- `SlottedCardEntrySchema`

These propagate via `z.infer<>` automatically. `schema/gamestate.schema.json` regenerates on the next `npm test`.

## Risks / Trade-offs

[Reflection target name drift] → CustomRarityUtil could rename `CardRarityXmlList` or its members in a future version. Mitigation: `CustomRarityProbe` logs a single startup message when it detects the type but cannot bind one of the delegates, so the failure mode is visible in the player's log without crashing the mod.

[Load-order dependency] → CustomRarityUtil must be loaded before our mod for the probe to find the type. Mitigation: vanilla LoR loads mods alphabetically by directory name; our mod is `PlayLoRWithMe`, alphabetically after `CustomRarityUtil`-named mods. If load order ever flips, we can re-probe on first state push instead of at init — a one-line change.

[Colour contrast on the web UI] → modders may pick rarity colours that are illegible against our dark panel background (e.g. `#0a0a0a`). Mitigation: out of scope. The same problem exists in-game; we mirror the modder's intent, not second-guess it.

[`AbilityKeywordColor` collides with our highlighting default] → the card-keyword-highlighting spec mandates a "bright-gold" colour. When a custom rarity overrides this, keywords inside that card's description will recolour. This is intentional — it matches in-game behaviour — but is a subtle visual departure. Mitigation: documented in the modified `card-keyword-highlighting` requirement.

[Vanilla rarities with a custom-rarity-named book group] → not a real risk; the probe only triggers when `Rarity` is past the vanilla enum maximum. Cards with vanilla rarities skip the probe entirely.

[Wire format bloat] → four extra fields × every card/passive/key-page on every state push. Mitigation: the fields are optional and omitted entirely for vanilla rarities, which is the >99% case. The DeltaEngine path also strips unchanged fields, so steady-state cost is zero.

[Frontend test coverage] → the existing rarity-style tests are class-based assertions in component tests. Mitigation: replace with inline-var assertions; add fixture cases that exercise both the vanilla fallback path and the override path.

## Migration Plan

1. Ship `CustomRarityProbe.cs` with the reflection lookup and the `RarityOverride` POCO. Build runs green; `Probe.TryGet` returns null for everyone until CustomRarityUtil is also installed.
2. Add the four optional schema fields. Regenerate `schema/gamestate.schema.json`. Existing fixtures continue to parse (all fields optional).
3. Wire the four emission sites in `GameStateSerializer.cs` (cards, passives, key pages — see proposal for the exact list). Verified by hand against a workshop mod that ships CustomRarityUtil-based content.
4. Add new CSS tokens to `app.vue` defaults: `--rarity-range-icon-color`, `--rarity-ability-color`, `--rarity-keyword-color`.
5. Migrate `PassiveList.vue` from classes to inline-var. Verify visually using the existing dev fixtures.
6. Rename `--rarity-border` → `--rarity-color` in `KeyPageDetail`, `KeyPageTab`, `PassivesTab`. One-line refactor each.
7. Add the four var hooks on combat-card / CardRangeIcon / CardDetail surfaces.
8. Extend fixtures to include a synthetic "Custom" rarity entry with override hexes; visually confirm in dev.
9. Spec deltas land alongside code: `combat-card-display`, `key-page-rarity-indicator`, `color-tokens`, `card-keyword-highlighting`, `wire-contract-schema`, plus the new `custom-rarity-compat` capability.

Rollback: revert the proposal commits. No data migration required — overrides are optional and the absence of override fields is the pre-change state.

## Open Questions

- Should `CustomRarityProbe` re-probe on first state push if init-time lookup failed? (Defensive against load-order corner cases.) Leaning yes, with a single retry, then permanent null.
- Should we expose a `theme.customRarities` map at the top level (a one-shot dictionary of `rarity-name → {colour overrides}`) instead of repeating overrides on every card? Smaller wire bytes but more frontend state. Leaning **no** for v1 — per-card optionality is cheaper to implement and DeltaEngine strips repeats.
