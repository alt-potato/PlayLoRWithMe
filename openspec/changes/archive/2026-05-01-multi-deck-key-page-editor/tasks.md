## 1. Wire schema (frontend types)

- [x] 1.1 Add `DeckPreviewSchema` (`{ index, label?, cards }`) to `frontend/app/types/game.ts`
- [x] 1.2 Replace `deckPreview` with `decks: DeckPreview[]` on `LibrarianEntrySchema`; add `isMultiDeck: boolean` to the librarian-context `KeyPageSchema` only
- [x] 1.3 Add optional `deckIndex?: number` (range 0..3) to `addCardToDeck` and `removeCardFromDeck` variants of `ClientActionSchema`
- [x] 1.4 Run `npm run generate-schema` and commit the regenerated `schema/gamestate.schema.json`
- [x] 1.5 Update `schema/reference-state.json` so every librarian carries a valid `decks` array (and at least one librarian exercises a multi-deck shape if feasible without inventing payload data); rerun drift test
- [x] 1.6 `cd frontend && npm test` — drift + reference-fixture tests green

## 2. Mod serializer (C#)

- [x] 2.1 In `GameStateSerializer.cs`, change librarian-deck emission from `deckPreview` to a `decks` array. For single-deck books: one entry, `index: 0`. For `book.IsMultiDeck()`: four entries (`_deckList[0..3]`) with `index: i`. Do not emit `label` server-side
- [x] 2.2 Emit `isMultiDeck: bool` on the librarian-context `keyPage` block (not on battle-unit `keyPage`)
- [x] 2.3 `cd mod && dotnet build` — 0 Warnings, 0 Errors. Confirm `decks`/`isMultiDeck` shapes by inspecting a state payload via the running mod or mock

## 3. Mod action handlers (C#)

- [x] 3.1 In `Server.cs::HandleAddCardToDeck`, parse optional `deckIndex` (default `0`). Validate `0 <= deckIndex < 4` and reject with `"deckIndex out of range"` otherwise
- [x] 3.2 If `book.IsMultiDeck() === false` and `deckIndex !== 0`, reject with `"key page is not multi-deck"`
- [x] 3.3 Capture `prevIdx = book.GetCurrentDeckIndex()`, call `book.ChangeDeck(deckIndex)`, run the existing add path, restore via `book.ChangeDeck(prevIdx)` in a `finally` block
- [x] 3.4 Apply the same validation + transient-switch pattern in `HandleRemoveCardFromDeck`
- [x] 3.5 `cd mod && dotnet build` — 0 Warnings, 0 Errors

## 4. Frontend deck-label mapping

- [x] 4.1 Add `frontend/app/utils/multiDeckLabels.ts` exporting `KNOWN_MULTI_DECK_LABELS` (initially `{ "0:250035": ["Slash", "Penetrate", "Hit", "Defense"] }`) and a `resolveDeckLabels(packageId, id): string[]` helper that returns the known labels or the generic `["Deck 1", "Deck 2", "Deck 3", "Deck 4"]` fallback
- [x] 4.2 Add unit tests in `frontend/app/utils/multiDeckLabels.test.ts` covering: known mapping (Purple Tear), generic fallback for unknown ID, handling of missing packageId
- [x] 4.3 `cd frontend && npm test` — new tests pass

## 5. Frontend action plumbing

- [x] 5.1 In `useLibrarianActions.ts`, extend `addCardToDeck` / `removeCardFromDeck` callback signatures to accept an optional `deckIndex?: number` argument and forward it onto the dispatched action
- [x] 5.2 Update `LibrarianManager.vue`'s callback wrappers to pass `deckIndex` through to `useLibrarianActions`
- [x] 5.3 Update `EditPanel.vue`'s prop types/wrappers so `addCardToDeck` / `removeCardFromDeck` accept and pass `deckIndex`
- [x] 5.4 `cd frontend && npm run typecheck` (or `npm test` which exercises tsc) — clean

## 6. DeckTab.vue: tab strip + per-deck pending state

- [x] 6.1 Read `lib.decks` instead of `lib.deckPreview`; introduce `activeDeckIndex` ref defaulting to `0`
- [x] 6.2 Re-key `pendingAdds` / `pendingRemoves` Maps from `pendingKey(cardId, packageId)` to `pendingKey(deckIndex, cardId, packageId)`
- [x] 6.3 Update `expandedDeck`, `renderedDeck`, `effectiveDeckCount`, `effectiveDeckCardCounts` to read from `decks[activeDeckIndex]` and the active tab's pending entries only
- [x] 6.4 Update the `props.lib.decks` watcher to compute deltas per deck index and reconcile pending state per-key (deckIndex+cardId+packageId)
- [x] 6.5 Update `STATE_GENERATION` watcher to clear pending state across all deck indices on connection reset
- [x] 6.6 Render a tab strip when `lib.keyPage.isMultiDeck === true`. Each tab: label from `resolveDeckLabels(...)`, `(N/9)` badge, active styling. Tab strip is hidden for single-deck books (existing visual unchanged)
- [x] 6.7 Pass `activeDeckIndex` into `handleAddCard` / `handleRemoveCard` when calling the `addCardToDeck` / `removeCardFromDeck` actions
- [x] 6.8 `cd frontend && npm test` — DeckTab logic tests still green (update or add tests as needed)

## 7. Dev fixtures + mock backend

- [x] 7.1 Update existing `librarian` fixtures in `frontend/app/dev/fixtures/` to use the `decks` shape; add a fixture entry exercising a multi-deck Purple-Tear-style librarian (`isMultiDeck: true`, four decks with distinct contents)
- [x] 7.2 Update `useMockBackend.ts` so its `addCardToDeck` / `removeCardFromDeck` handlers respect `deckIndex`, mutate the requested deck, and validate `deckIndex` against `isMultiDeck`
- [x] 7.3 `cd frontend && npm test` — fixture-parsing test passes; mock backend tests (if any) green

## 8. End-to-end build + smoke

- [x] 8.1 `cd mod && dotnet build` — 0 Warnings, 0 Errors
- [x] 8.2 Run the mod against the live game with a librarian equipped with The Purple Tear. Confirm: tab strip appears with stance labels; tapping each tab shows the corresponding deck; adding/removing on each deck targets only that deck; switching decks does not visibly change the librarian's stance in-game; cap and per-card limits enforced per tab (manual test, prompt user if needed)
- [x] 8.3 Confirm single-deck librarians render no tab strip and behave identically to before
