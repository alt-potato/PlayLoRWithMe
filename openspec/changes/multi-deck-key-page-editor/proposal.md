## Why

Some key pages (notably **The Purple Tear**, id `250035`, via passive `PassiveAbility_250127`) hold up to four `DeckModel` slots that the game swaps between during combat — one per stance. Mods can introduce additional multi-deck key pages; the engine flags them generically via `BookOption.MultiDeck`. Today the frontend only serializes and edits the *currently active* deck (`BookModel.GetCardListFromCurrentDeck`), so librarians equipped with multi-deck pages are stuck with whatever cards were in deck 0 — slots 1–3 are invisible and uneditable. This blocks normal use of stance-changing librarians outside of single-player vanilla.

## What Changes

- **Serializer** emits, for each librarian, a `decks` array of length 1 (single-deck books) or 4 (multi-deck books) instead of a single `deckPreview`. Each entry carries `index`, optional `label`, and `cards`.
- **Server action handlers** (`addCardToDeck` / `removeCardFromDeck`) accept an optional `deckIndex` (default `0`). Handlers temporarily switch to the requested deck via `BookModel.ChangeDeck`, mutate, then restore the previous active index — so spectators don't see a phantom in-game stance switch from an editor action.
- **DeckTab.vue** renders a tab strip above the equipped-deck column when the librarian's key page is multi-deck. Each tab shows the per-deck label (stance name where known, generic fallback otherwise) and per-deck count badge. Selecting a tab swaps which deck the editor reads/writes.
- **Stance-name lookup** maps a small set of known multi-deck key page IDs to localized stance labels (e.g. `250035` → `["Slash", "Penetrate", "Hit", "Defense"]`). Unknown multi-deck pages (mod-added, future content) fall back to `Deck 1–4`.
- **Optimistic edit machinery** keys pending tiles by `(deckIndex, cardId, packageId)` rather than `(cardId, packageId)`, so a pending add to deck 1 doesn't reconcile against a delta on deck 0.
- **Wire schema** extends `addCardToDeck` / `removeCardFromDeck` actions with `deckIndex?: number` and replaces `deckPreview: CardId[]` on `LibrarianEntry` with `decks: DeckPreview[]` (where `DeckPreview` is `{ index, label?, cards }`). **BREAKING** for anyone reading `lib.deckPreview`; an internal-only contract, no compatibility shim needed.

## Capabilities

### New Capabilities

- `multi-deck-key-page-editor`: behavior for surfacing and editing all `DeckModel` slots of a multi-deck key page in the librarian editor — tab strip, per-deck addressing, label resolution, fallback for unknown multi-deck books.

### Modified Capabilities

- `optimistic-deck-edit`: pending-tile lifecycle becomes per-deck. Pending keys gain a `deckIndex` dimension; FIFO reconciliation runs against the matching deck's `cards` array; cap and per-card-limit math is per deck (each `DeckModel` independently caps at 9, per-card limit is per-key-page so it sums across all decks).
- `wire-contract-schema`: action payloads gain optional `deckIndex`; `LibrarianEntry.deckPreview` is replaced by `decks: DeckPreview[]`; `KeyPage` (in librarian-management context) gains `isMultiDeck: boolean`.

## Impact

- `mod/GameStateSerializer.cs` — change librarian deck serialization to emit `decks` array; surface `isMultiDeck` on key page; iterate `BookModel.GetDeckAll_nocopy()`.
- `mod/Server.cs` — `HandleAddCardToDeck` / `HandleRemoveCardFromDeck` accept and act on `deckIndex`; switch active deck around the mutation and restore.
- `frontend/app/types/game.ts` — schema updates: `DeckPreviewSchema`, `LibrarianEntrySchema.decks`, `KeyPageSchema.isMultiDeck`, `addCardToDeckSchema.deckIndex?`, `removeCardFromDeckSchema.deckIndex?`. Drift test will catch and require regeneration of `schema/gamestate.schema.json` and `schema/reference-state.json`.
- `frontend/app/components/librarian/DeckTab.vue` — tab strip + per-deck rendered state.
- `frontend/app/components/librarian/EditPanel.vue` and `LibrarianManager.vue` — `addCardToDeck` / `removeCardFromDeck` callbacks accept a `deckIndex` argument.
- `frontend/app/dev/fixtures/` and `frontend/app/dev/useMockBackend.ts` — fixtures updated to the new shape; mock backend supports per-deck dispatch.
- No changes to battle-time UI; per the user's call, in-battle behavior is already driven by the engine's own deck swaps.
