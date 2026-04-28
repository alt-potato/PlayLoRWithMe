## Context

Combat cards already advertise rarity via `cardBorderColor()` (HandCard.vue),
which maps the wire-side `rarity` string to one of five `--rarity-*` CSS
tokens. Key pages share the same `Rarity` enum on the C# side
(`BookXmlInfo.Rarity`), but the field is not currently emitted on the wire,
so the frontend has nothing to render. Players browsing the EditPanel pages
must open each one to see whether it's a Common/Unique/etc.

Three customization surfaces show key pages today:

- `KeyPageTab.vue` `.kp-tile` — the picker grid in the EditPanel, with
  `kp-tile--equipped` (gold left-border) and `kp-tile--selected` (full gold
  border) state classes already in play.
- `KeyPageDetail.vue` `.kp-detail` — the right-side detail pane, also
  embedded in DeckTab (compact mode), PassivesTab (compact mode), and the
  EditPanel header.
- `PassivesTab.vue` `.source-tile` — the passive-source picker grid (same
  data source as `availableKeyPages`).

Combat surfaces (`SettingDetailPanel.vue`, battle `DisplayCard.vue`) also
render `keyPage.name` but should not advertise rarity — it has no tactical
meaning during combat and would just add chrome.

## Goals / Non-Goals

**Goals:**

- Surface key page rarity at-a-glance on the three customization surfaces
  above, using the same colour palette as combat cards.
- Keep the existing equipped/selected affordances working without visual
  conflict.
- Avoid emitting rarity onto combat payloads — the indicator should not
  appear in combat regardless of frontend logic.

**Non-Goals:**

- No new CSS tokens; reuse the existing `--rarity-*` palette.
- No icons, text labels, or rarity badges — purely the outline.
- No changes to `LibrarianManager.vue` floor roster (out of scope per
  product direction).
- No changes to combat rendering paths.

## Decisions

### Decision 1: enforce "not in combat" at the C# layer, not the frontend

The clean way to express "rarity is irrelevant in combat" is to *not send it*
on combat-context payloads. The serializer has three distinct key-page
emission sites: the librarian-owned `keyPage` (in the floor/librarian loop),
the BattleSetting `keyPage` (pre-battle preview, around line 1366), and the
in-battle `WriteKeyPage` helper (around line 1656). Adding `rarity` only to
the librarian-owned site (and to `availableKeyPages`) means the BattleSetting
preview and battle units never receive the field. `KeyPageDetail.vue` already
treats `rarity` as optional, so when it's invoked from the EditPanel's
DeckTab/PassivesTab/header it shows the outline, and any future combat-side
caller would simply receive `undefined` and render no outline.

**Alternative considered:** emit rarity universally, gate display in the
frontend by inspecting an `isCombat` flag or by wrapping `KeyPageDetail`
with a `:show-rarity` prop. Rejected — duplicates state, easier to forget,
adds a frontend prop that exists only to suppress something.

### Decision 2: full-border tint, leveraging existing equipped/selected overrides

Setting `borderColor` on `.kp-tile` to a rarity color, leaving every other
border property unchanged:

- `kp-tile--equipped` declares `border-left: 3px solid var(--gold-bright)`
  *after* the base rule, so the left edge stays gold while the other three
  sides take the rarity tint. Reads as "equipped *and* this rarity".
- `kp-tile--selected` declares `border-color: var(--gold-bright)` for all
  four sides, so selection wins over rarity. Acceptable — the user has just
  picked the tile, the rarity of the *other* unselected tiles is what
  matters at that moment.

`KeyPageDetail.vue` `.kp-detail` adds a 1px rarity border around the whole
panel. `PassivesTab.vue` `.source-tile` mirrors the `kp-tile` treatment.

**Alternative considered:** small colored pip glyph next to the name. Rejected
because (a) the user explicitly chose the outline as "neatest" and (b) the pip
adds an inline element to scan for, whereas the outline is peripheral
information that doesn't compete for attention.

### Decision 3: optional `rarity` on shared `KeyPageSchema`, not a new type

`KeyPageSchema` is already shared by librarian-owned and battle-unit key
pages. Adding `rarity?: string` keeps a single source of truth and lets the
C# layer toggle presence by emission-site. Splitting into
`LibrarianKeyPage` vs `BattleKeyPage` would force every consumer to choose
the right type and cascades through `KeyPageDetail`'s `AnyKeyPage` union.

## Risks / Trade-offs

- **Risk:** Workshop key pages may have malformed/missing `Rarity` in their
  XML. → **Mitigation:** `BookXmlInfo.Rarity` is a non-nullable enum; XML
  deserialization defaults it to `Common` when absent. The frontend treats
  the field as optional regardless.
- **Risk:** Common rarity (saturated green) appears on most chapter-1 tiles
  and could be mistaken for a "good"/"available" cue. → **Trade-off:** This
  is accurate to the data — most early-game pages are Common — and matches
  the combat-card convention where Common cards are likewise everywhere.
  Acceptable.
- **Risk:** A future contributor adds a third key-page emission site (e.g.
  for a new librarian-management feature) and forgets to include `rarity`,
  causing a regression where the indicator silently disappears. →
  **Mitigation:** the wire-contract-schema spec adds an explicit
  requirement that customization-surface key pages MUST include `rarity`;
  drift would be caught by a future schema-fixture test.
- **Trade-off:** Selected tile masks the rarity color. Acceptable — selection
  is transient and the user can see neighboring tiles' rarities.

## Migration Plan

No migration concerns. The wire field is additive and optional; older clients
parsing newer payloads via Zod's permissive default will simply ignore an
unknown field. Newer clients receiving older payloads (e.g. during a
WebSocket reconnect after a mod hot-reload) see `rarity === undefined` and
skip the outline — same as the combat path.
