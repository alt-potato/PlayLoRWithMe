## Why

Combat cards already advertise their rarity with a colored border (Common
green, Uncommon blue, Rare purple, Unique gold, Special red), but key pages —
which carry the same `Rarity` enum on the C# side — show no such indicator.
Players browsing the inventory and customization panels currently can't tell at
a glance which key pages are Unique versus Common, so deck/page planning
requires opening each page in detail. Combat itself has no use for the rarity
information (it isn't a tactical input), so the indicator should appear only on
non-combat surfaces.

## What Changes

- C# serializer emits `rarity` on the librarian-owned `keyPage` and on every
  `availableKeyPages` entry, sourced from `BookXmlInfo.Rarity`. Battle-unit
  key pages remain rarity-less so the field is naturally absent from combat
  payloads.
- Wire schema gains an optional `rarity?: string` on both `KeyPageSchema` and
  `AvailableKeyPageSchema`.
- Three frontend surfaces tint their tile/panel border with the matching
  `--rarity-*` CSS token when `rarity` is present:
  - `KeyPageTab.vue` `.kp-tile` (the picker grid)
  - `KeyPageDetail.vue` `.kp-detail` (the right-side detail pane, also embedded
    in DeckTab and PassivesTab in compact mode and in the EditPanel header)
  - `PassivesTab.vue` `.source-tile` (the passive-source picker grid)
- Existing equipped/selected indicators continue to work: `kp-tile--selected`
  still overrides all four sides to gold, and `kp-tile--equipped`'s gold
  left-border still overrides the rarity color on the left edge.

## Capabilities

### New Capabilities

- `key-page-rarity-indicator`: rules for surfacing key page rarity on
  customization and selection surfaces while excluding combat.

### Modified Capabilities

- `wire-contract-schema`: `KeyPage` and `AvailableKeyPage` gain optional
  `rarity` fields.

## Impact

- `mod/GameStateSerializer.cs`: two new emission sites (one per key-page
  serialization path).
- `frontend/app/types/game.ts`: optional `rarity` on two schemas; regenerates
  `schema/gamestate.schema.json`.
- `frontend/app/components/librarian/KeyPageTab.vue`,
  `KeyPageDetail.vue`, `PassivesTab.vue`: inline `:style` border color binding.
- `frontend/app/dev/fixtures/main-librarian.json`: add `rarity` to existing
  available key pages so the dev-mode preview exercises the new outlines.
- No changes to battle code paths; "not relevant in combat" is enforced by
  withholding the field at the C# layer, not by frontend gating.
- No new CSS tokens — reuses the existing `--rarity-common/uncommon/rare/unique/special`.
