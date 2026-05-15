## 1. Wire contract — `baseKeyPage` on `LibrarianEntry`

- [x] 1.1 Add `baseKeyPage: KeyPageSchema` (required) to `LibrarianEntrySchema` in `frontend/app/types/game.ts`. Document its purpose and the librarian-only emission contract in a JSDoc comment.
- [x] 1.2 Refactor the librarian-context key-page emission block in `mod/GameStateSerializer.cs` into a reusable local writer (e.g. `WriteLibrarianKeyPage(JsonObjectWriter, BookModel)`) so `keyPage` and `baseKeyPage` share one source of truth. Call it for both `unit.bookItem` (as `keyPage`) and `unit.unitData.defaultBook` (as `baseKeyPage`).
- [x] 1.3 Verify there is no leak into battle-unit emission: confirm `WriteUnitBattleData` and any battle-phase `keyPage` writer do NOT add `baseKeyPage`. No code change required if confirmed.
- [x] 1.4 Update `frontend/app/dev/useMockBackend.ts` fixtures so every mocked `LibrarianEntry` carries a `baseKeyPage` (mirror the existing `keyPage` snapshot when no separate base data is meaningful for the fixture). Also covers `schema/reference-state.json`.
- [x] 1.5 Build validation: `cd mod && dotnet build` → `0 Warning(s) 0 Error(s)`. Run `npm test` in `frontend/` — all existing tests still pass; the dev-mock-backend fixture schema validation does not regress.

## 2. Wire contract — `unequipKeyPage` action variant

- [x] 2.1 Add `unequipKeyPage` to `ClientActionSchema` in `frontend/app/types/game.ts`: `{ type: "unequipKeyPage", floorIndex: number, unitIndex: number }`.
- [x] 2.2 Add `unequipKeyPage(floorIndex, unitIndex): Promise<ActionResult>` to the `LibrarianActions` interface in `frontend/app/composables/useLibrarianActions.ts`, and wire the real implementation through `useWebSocket.ts` alongside the existing `equipKeyPage` call site. Also threaded through the destructure + `provide()` block in `app.vue`.
- [x] 2.3 Build validation: `npm run generate` succeeds. (`npm run check` shows only a pre-existing typecheck error in `useBattleDisplay.test.ts` from `d4c87c2`, unrelated.)

## 3. Server handler — `HandleUnequipKeyPage`

- [x] 3.1 Add a `case "unequipKeyPage"` dispatch in `mod/Server.cs` that calls `HandleUnequipKeyPage(client, r, reqId)` on the Unity main thread, mirroring the existing `equipKeyPage` dispatch.
- [x] 3.2 Implement `HandleUnequipKeyPage`: reuse `ValidateLibrarianEdit` for lock + index checks. If `unit.bookItem == unit.defaultBook` already, reply success without broadcasting. Otherwise call `unit.EquipBook(null)`, verify `unit.bookItem == unit.defaultBook`, refresh the character renderer with `refreshCardInventory: true`, and `SaveAndBroadcast` on success. On post-condition failure return a specific error message.
- [x] 3.3 Build validation: `cd mod && dotnet build` → `0 Warning(s) 0 Error(s)`.

## 4. Mock backend parity

- [x] 4.1 In `frontend/app/dev/useMockBackend.ts`, add an `unequipKeyPage` handler that follows the existing log-only pattern (logs with `[mock]` prefix, resolves `{ ok: true }`, no state mutation). Expose it on the returned object alongside `equipKeyPage`.
- [x] 4.2 Also update the `dev-mock-backend` spec's enumerated handler list to include `unequipKeyPage`. Delta added at `openspec/changes/key-page-unequip-to-base/specs/dev-mock-backend/spec.md`.
- [x] 4.3 Build validation: `npm test` passes; existing tests unaffected (133/133).

## 5. UI — pinned base tile in `KeyPageTab`

- [x] 5.1 In `frontend/app/components/librarian/KeyPageTab.vue`, the action button doubles as the Unequip affordance. Selecting the currently-equipped inventory tile flips the label to "Unequip"; any other tile reads "Equip". No separate base tile is rendered (revised after user feedback — see proposal/design Decision 3).
- [x] 5.2 `selectedInstanceId: number | null` defaults to the equipped page when off-base, or `null` when on-base. `selectedPage` resolves to `null` for the on-base default; the detail pane renders a placeholder via `v-if`.
- [x] 5.3 `actionState` machine: `equip` / `unequip` / `hidden`. `editBusy` disables the button when shown; `hidden` removes it from the DOM (no greyed-out "Equipped" sitting there with no action available).
- [x] 5.4 Threaded `onUnequipPage` prop through `LibrarianManager.vue` → `EditPanel.vue` → `KeyPageTab.vue`. (The "pinned-tile-outside-groupedPages" comment requirement from the first pass is moot — no such tile exists.)

## 6. UI polish & accessibility

- [x] 6.1 No base-tile styling required after revision. `.kp-tile--base` and `.kp-tile-base-badge` rules removed. The Unequip path reuses the existing `.equip-btn` slot — no new visual surface.
- [x] 6.2 No base tile means no separate aria target; the existing inventory tiles already carry accessible names via their tile-name span. The placeholder "Select a key page to view details" string serves as the on-base accessible state.
- [x] 6.3 End-to-end wiring verified by grep: `useWebSocket.ts` → `app.vue` provide → `LibrarianManager.onUnequipPage` → `EditPanel` prop → `KeyPageTab.performAction()`. Manual browser walkthrough recommended by the user before merge.

## 7. Validation & commit

- [x] 7.1 Final build: `cd mod && dotnet build` → `0 Warning(s) 0 Error(s)`.
- [x] 7.2 Final tests: `npm test` in `frontend/` passes; 133/133 green. `openspec validate --strict` passes.
- [x] 7.3 Committed as a single atomic commit (user-approved shape — wire/server/UI layers are co-dependent and ship together).
