## 1. Backend: Verify EquipCustomCoreBook and extend fashionBooks serialization

- [x] 1.1 Use ilspycmd to inspect `UnitDataModel.EquipCustomCoreBook` and confirm it accepts any `BookModel` (not just ones from `CustomCoreBookInventoryModel`); note the finding in design.md's Open Questions
- [x] 1.2 In `GameStateSerializer.cs`, after writing the existing fashion books loop, add a second pass over `BookInventoryModel.Instance?.GetBookList_equip()` that emits entries where `book.IsWorkshop && !string.IsNullOrEmpty(book.ClassInfo?.GetCharacterSkin())`; include `packageId` field (`book.ClassInfo.id.packageId`), appearance metadata from `AppearanceCache.FashionMeta`, and all existing `fashionBooks` fields
- [x] 1.3 In the librarian serialization block, serialize `customBookPackageId` alongside `customBookId` when `customBook.ClassInfo.id.packageId` is non-empty
- [x] 1.4 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`

## 2. Backend: Update setCustomization handler

- [x] 2.1 In `Server.cs` `setCustomization` handler, read optional `customBookPackageId` string; when non-empty and `cbid >= 0`, look up via `new LorId(cbid, packageId)` instead of `new LorId(cbid)`; skip equip silently if the book is not found
- [x] 2.2 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`

## 3. Frontend: Types and ProjectionTab

- [x] 3.1 In `frontend/app/types/game.ts`, add `packageId?: string` to `FashionBook`; add `customBookPackageId?: string` to `CustomizePayload`
- [x] 3.2 In `CustomizePanel.vue`, add `customBookPackageId: string` to the draft (initialized from `lib.customBookPackageId ?? ""`); pass it as prop to `ProjectionTab`; include it in the `setCustomization` payload (omit the key when empty)
- [x] 3.3 In `LibrarianEntry` in `game.ts`, add `customBookPackageId?: string`
- [x] 3.4 In `ProjectionTab.vue`, add `customBookPackageId: string` prop and `update:customBookPackageId` emit; update the `active` check to `book.id === customBookId && (book.packageId ?? '') === customBookPackageId`; emit `update:customBookPackageId` alongside `update:customBookId` when a book is selected or unequipped (emit `''` on unequip)
- [x] 3.5 Run `cd frontend && npm run typecheck` — expect no type errors
