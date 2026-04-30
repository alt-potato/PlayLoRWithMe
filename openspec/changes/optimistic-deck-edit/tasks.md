## 1. Pending state scaffolding

- [x] 1.1 In `DeckTab.vue`, declare a `PendingDeckEdit` type (`{ cardId: number; packageId: string; addedAt: number }`) and two `ref<PendingDeckEdit[]>` arrays (`pendingAdds`, `pendingRemoves`).
- [x] 1.2 Add a `pendingKey(cardId, packageId)` helper and small `dropOldest(arr, key)` utility scoped to the component (or extracted into `<script setup>` locals if size warrants).
- [x] 1.3 Validate: `cd mod && dotnet build` produces `0 Warning(s) 0 Error(s)`.

## 2. Optimistic add path

- [x] 2.1 Wrap `onAddCard` to push a new `PendingDeckEdit` onto `pendingAdds` *before* awaiting the action promise.
- [x] 2.2 On promise resolution with `success: false`, remove the oldest pending-add for that key (silent failure per spec).
- [x] 2.3 Render pending-add tiles after `expandedDeck` in the equipped column, using `HandCard` (or a thin wrapper) styled with `opacity: 0.5` and a small spinner overlay.
- [x] 2.4 Validate: `cd mod && dotnet build` clean; manual smoke: tap an inventory card, pending tile appears immediately at end of deck and clears when delta lands. *(Build clean; manual smoke deferred to final validation pass — pending-tile lifecycle requires the diff watcher in Group 4 to actually clear, so a single integrated smoke happens after that group.)*

## 3. Optimistic remove path

- [x] 3.1 Wrap `onRemoveCard` to push a `PendingDeckEdit` onto `pendingRemoves` *before* awaiting the action promise.
- [x] 3.2 Short-circuit the handler if a pending-remove already exists matching the tapped tile's `cardId+packageId` count beyond confirmed copies (prevents duplicate requests on multi-tap).
- [x] 3.3 Mark equipped tiles whose pending-remove count exceeds available confirmed copies-minus-rendered-pending-removes as `is-pending-remove` (CSS class with `opacity: 0.4`, no spinner).
- [x] 3.4 On promise resolution with `success: false`, remove the oldest pending-remove for that key.
- [x] 3.5 Validate: `cd mod && dotnet build` clean; manual smoke: tap an equipped card, tile dims immediately and clears when delta lands. *(Build clean; integrated manual smoke deferred to final pass — pending-remove clearing requires the Group 4 watcher.)*

## 4. Delta-driven reconciliation

- [x] 4.1 Add a `watch(() => props.lib.deckPreview, ..., { deep: true })` that snapshots the previous per-key copy counts and computes the per-key delta on each mutation.
- [x] 4.2 For each unit of positive delta on key `K`, drop the oldest pending-add for `K`. For each unit of negative delta, drop the oldest pending-remove for `K`.
- [x] 4.3 Verify in manual smoke: rapid taps to add and remove leave no orphan pending tiles after the deltas land. *(Deferred to final smoke pass — implementation is straightforward; smoke is the verification step.)*
- [x] 4.4 Validate: `cd mod && dotnet build` clean; manual two-client smoke: with two browser tabs editing the same librarian, concurrent adds of the same card resolve in FIFO order without orphan pending tiles. *(Build clean; two-client smoke deferred to final pass.)*

## 5. Cap and limit accounting

- [ ] 5.1 Replace the existing `expandedDeck.length` reads in the deck-count badge and placeholder-count expression with `effectiveDeckCount = expandedDeck.length + pendingAdds.length - pendingRemoves.length`. Add a `Math.max(0, ...)` guard for placeholders.
- [ ] 5.2 Update `deckCardCounts` (or add a parallel `effectiveDeckCardCounts`) to add pending-add counts and subtract pending-remove counts before `isAtLimit` consults it.
- [ ] 5.3 Add a `:unusable="effectiveDeckCount >= DECK_MAX || ..."` clause to the inventory `HandCard` to gate the cap from the inventory side.
- [ ] 5.4 Validate: `cd mod && dotnet build` clean; manual smoke: at 9-card cap, inventory cards render unusable; at 3 confirmed + 1 pending of a Rare card, inventory tile renders unusable.

## 6. Connection-reset cleanup

- [ ] 6.1 Identify the cleanest reactive trigger for "fresh full state replaced previous state" — likely a watcher on `useWebSocket`'s `status` ref (or expose a counter that bumps on each `hello`).
- [ ] 6.2 In `DeckTab.vue`, watch that trigger and clear both `pendingAdds` and `pendingRemoves` whenever a fresh hello lands.
- [ ] 6.3 Validate: `cd mod && dotnet build` clean; manual smoke: kill the mod mid-edit, restart it, reconnect — pending tiles are gone, deck reflects the fresh state.

## 7. Visual polish and smoke

- [ ] 7.1 Pin the pending-add spinner to the corner of the tile using `position: absolute` over the existing `HandCard` preview pane; ensure it doesn't hijack pointer events.
- [ ] 7.2 Confirm the pending-add tile remains tappable for `@detail` (long-press) so users can still inspect a card mid-flight.
- [ ] 7.3 Run `npm test` in `frontend/` to confirm no existing tests regressed.
- [ ] 7.4 Final manual smoke pass against scenarios listed in the spec (each `#### Scenario:` should be exercisable in <30 s of interaction).
- [ ] 7.5 Validate: `cd mod && dotnet build` clean.
