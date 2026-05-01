## Context

`BookModel` (a key page) always allocates four `DeckModel` instances in `_deckList`. For most key pages only `_deckList[0]` is used; `BookModel.IsMultiDeck()` returns `true` for key pages whose `BookXmlInfo.optionList` contains `BookOption.MultiDeck`. The vanilla example is The Purple Tear (`250035`), whose `PassiveAbility_250127` swaps the active deck via `BookModel.ChangeDeck(idx)` on stance changes during combat. Mods can author additional multi-deck key pages.

Today:

- `GameStateSerializer` writes a single `deckPreview: CardId[]` per librarian, sourced from `bookItem.GetCardListFromCurrentDeck()` — slots 1–3 are invisible and uneditable.
- `Server.cs::HandleAddCardToDeck` / `HandleRemoveCardFromDeck` call `bookItem.AddCardFromInventoryToCurrentDeck` / `MoveCardFromCurrentDeckToInventory` — they always mutate the active deck.
- `DeckTab.vue` renders one column for `expandedDeck` and dispatches add/remove without a deck index.
- `optimistic-deck-edit` keys pending tiles by `(cardId, packageId)`; FIFO reconciliation runs against `props.lib.deckPreview`.

The user-facing consequence: librarians equipped with The Purple Tear (or any modded multi-deck book) appear to have only one editable deck. The other three are whatever the underlying save state contains, with no path to inspect or change them via the web UI.

## Goals / Non-Goals

**Goals:**

- Surface every `DeckModel` slot for multi-deck key pages in the librarian editor.
- Let any session that holds the librarian's edit lock add/remove cards in any deck slot.
- Preserve the existing optimistic-edit responsiveness, per-deck.
- Label decks with stance-specific names where the mapping is known (Purple Tear → Slash/Penetrate/Hit/Defense), with a reasonable generic fallback for unknown multi-deck books (vanilla future content, mod-added).
- Keep editor-driven deck-slot mutations transparent to other observers — i.e. an editor switching to deck 2 to add a card MUST NOT cause the in-game character to swap stance.

**Non-Goals:**

- Battle-time UI changes. The engine already swaps decks on stance change; battle state already reflects the active deck through normal serialization. A spectator can already see the active deck change. Surfacing the inactive decks during battle is out of scope.
- Persistent display of "current stance" on the librarian roster page. The current active index is derivable but not actively highlighted in this change — single-deck books have nothing to highlight, and multi-deck books only have a meaningful "current" index during combat.
- Authoring a generic stance-name lookup that pulls from the engine's localization tables. Stance names are hardcoded in C# passive classes, not in any XML the serializer can introspect cheaply. We use a small, hand-maintained mapping plus generic fallback.
- Per-deck rename. Decks are positionally addressed by the engine; renaming them would not survive a save round-trip.

## Decisions

### Wire shape: replace `deckPreview` with a `decks` array

`LibrarianEntry` currently carries `deckPreview: CardId[]`. We replace it with `decks: DeckPreview[]`, where:

```ts
type DeckPreview = {
  index: number;          // 0..3
  label?: string;         // present for multi-deck books; absent for single-deck
  cards: CardId[];
};
```

For single-deck books: `decks.length === 1`, `decks[0].index === 0`, `decks[0].label` is omitted. For multi-deck books: `decks.length === 4`, each entry has a label. This is a **BREAKING** wire change for the editor flow, but `deckPreview` is only consumed inside `librarian/*` components on the frontend; we update those call sites in the same change.

**Why not keep `deckPreview` and add a parallel `decks` field?** Maintaining both doubles the serialization cost and invites drift. The frontend consumers are all in-tree.

**Why not always emit `decks.length === 4`?** It would inflate every state message for every librarian by ~30 bytes for empty trailing slots that the editor would never show. Keeping single-deck books at length 1 is materially cheaper given hundreds of librarians.

### Where to surface `isMultiDeck`

We add `isMultiDeck: boolean` to the `KeyPage` shape carried on `LibrarianEntry` (the librarian-management context, not the battle-unit one). This lets `DeckTab.vue` decide whether to render the tab strip without inferring it from `decks.length` (which would conflate single-deck books with empty multi-deck slots).

### Action payload extension

`addCardToDeck` and `removeCardFromDeck` gain an optional `deckIndex?: number` field. Default `0`. The mod validates `0 ≤ deckIndex < 4` and rejects out-of-range with `{ ok: false, error: "deckIndex out of range" }`. Single-deck books accept only `deckIndex === 0`; otherwise reject with `{ ok: false, error: "key page is not multi-deck" }`.

### Server-side deck switching is transient and restored

`HandleAddCardToDeck` / `HandleRemoveCardFromDeck` capture `book.GetCurrentDeckIndex()`, call `book.ChangeDeck(deckIndex)`, perform the mutation, then restore via `book.ChangeDeck(prevIdx)`. This is on the Unity main thread (already guaranteed by the marshal in `OnWebSocketMessage` from the prior change), so no other code observes the in-flight switch. Importantly:

- Outside of battle, `ChangeDeck` is a pure pointer swap on `BookModel._deck` — no buffs, no skin change, no sound.
- During battle, `PassiveAbility_250127.ChangeStance_*` does the player-visible work (skin/buf/sound) and *then* calls `unitData.GetDeckForBattle(idx)` and `owner.ChangeBaseDeck(...)`. `BookModel.ChangeDeck` itself does not invoke that side-effect path.

So a transient switch by the editor is invisible. We still restore the previous index defensively in case future code adds reactive behavior on `ChangeDeck`.

### Optimistic-edit key gains a `deckIndex` dimension

`pendingAdds` / `pendingRemoves` Maps in `DeckTab.vue` are currently keyed by `pendingKey(cardId, packageId)`. We extend the key to `pendingKey(deckIndex, cardId, packageId)`. The deckPreview-diff watcher iterates each deck index in the `decks` array and reconciles per-deck FIFO. This naturally handles concurrent edits across decks: a pending add to deck 2 cannot be cleared by a delta that increments deck 0's count.

### Cap math is per deck; copy-limit math is per key page

`DeckModel.maxDeckCount = 9` is per `DeckModel` instance. Each deck slot independently caps at 9. Per-card copy limits, however, come from `DiceCardXmlInfo.Limit` and the inventory model: `DeckModel.AddCardFromInventory` checks the limit against the *current* deck only (`_deck.FindAll(...)`), and `RemoveCard` removes from inventory. So in vanilla, copy limits are per *deck slot*, not per key page. We mirror that exactly: the inventory-tile `unusable` check uses the active tab's deck count plus active-tab pending edits.

### Deck label resolution

A small `KNOWN_MULTI_DECK_LABELS: Record<string, string[]>` map in the frontend (`utils/multiDeckLabels.ts`) covers the vanilla cases. Key format is `"<packageId>:<id>"` to disambiguate workshop/base IDs (e.g. `"0:250035"` → `["Slash", "Penetrate", "Hit", "Defense"]`). Lookup falls back to `["Deck 1", "Deck 2", "Deck 3", "Deck 4"]`.

The mod side does *not* emit labels — labels are a frontend concern and don't need to round-trip. This keeps the wire payload small and avoids needing localization plumbing in C#.

**Why not emit labels from the mod?** Stance names are hardcoded in passive class methods (`PassiveAbility_250127.ChangeStance_slash`), not in any localization XML keyed by book ID. Reverse-engineering each known passive into a C# label table would duplicate the same hardcoded strings the frontend already needs to render in its own locale. Frontend-side keeps localization in one place.

**What about i18n?** The frontend currently doesn't have a translation layer; English strings are hardcoded throughout. The label table follows existing conventions; a future i18n pass would migrate it alongside the rest of the UI strings.

### Tab strip UX

For multi-deck books, `DeckTab.vue` renders a horizontal tab strip above the equipped-deck column. Each tab shows: the deck label, an `(N/9)` count badge based on confirmed + pending-add − pending-remove for that deck. The active tab is bold/underlined; tapping a tab swaps the rendered deck and updates which tab `addCardToDeck` / `removeCardFromDeck` calls target.

For single-deck books the tab strip is absent (component returns `null` for the tab strip slot). This matches existing behavior for the 99% case.

The active tab's selection state is local to `DeckTab.vue` (a `ref<number>(0)`) — it does not need to round-trip to the server, since each request explicitly carries `deckIndex`.

## Risks / Trade-offs

[Risk] **Mod-added multi-deck key pages with non-stance semantics use generic labels.** The fallback "Deck 1–4" is intentionally bland. Mod authors who want stance-specific labels would need a frontend update. → Mitigation: documented in the spec; future enhancement could let mod authors register labels via a side-channel (e.g. a workshop manifest), out of scope here.

[Risk] **Wire-format break for any external consumer of `deckPreview`.** No external consumers exist (only this repo's frontend), but a future mod or third-party UI that scraped state messages would break. → Mitigation: this is an internal contract; we do not promise wire stability. Frontend updates are atomic with serializer changes.

[Risk] **Switching active deck mid-edit could race with a stance-change passive in battle.** If an editor session adds a card to deck 2 at exactly the moment a battle stance change runs `ChangeDeck(0)` on a different thread… both run on the Unity main thread (the librarian-edit handlers were marshaled there in the prior change), so there is no interleaving. → Mitigation: already covered by main-thread marshaling.

[Risk] **Deck slots 1–3 may contain stale or invalid cards from save data.** Multi-deck books that previously had no editor exposure may have orphaned/error cards. The existing `availableCards.isError` filter and `RemoveAllErrorCard` are out-of-band — we do not auto-scrub. → Mitigation: same as the prior decision in `deck-edit-spam-lockup-investigation` (preserve user intent if a mod was uninstalled).

[Risk] **Per-card copy limit being per-deck-slot may surprise users.** A `Rare` card (limit 3) can be equipped 3 times in deck 0 *and* 3 times in deck 1 simultaneously, totaling 6 copies in inventory pulls. → Mitigation: this matches vanilla in-game behavior. Documented in the spec.

## Migration Plan

This is internal to the project (mod + bundled frontend), shipped together. No staged rollout:

1. Update wire schema (`types/game.ts`) and regenerate `schema/gamestate.schema.json`.
2. Update C# serializer to emit `decks`/`isMultiDeck`.
3. Update C# action handlers to accept and validate `deckIndex`.
4. Update `DeckTab.vue` for tabs + per-deck pending state.
5. Update `LibrarianManager.vue` / `EditPanel.vue` to thread `deckIndex` through callbacks.
6. Update fixtures and mock backend.
7. Build, smoke-test against The Purple Tear.

No rollback complexity beyond `git revert`.
