## Context

The Projection tab lets players set an appearance projection for a librarian using "fashion books" — dedicated costume items from `CustomCoreBookInventoryModel`. Workshop key pages (mods from the LoR workshop/mod system) can also define a character skin via `CharacterSkin` in their XML, but are not included in the fashion book list because they live in `BookInventoryModel` instead.

The primary blocker is identity: fashion books use a plain `int` ID (`LorId.id`), but workshop books have a compound `LorId` (`.id` + `.packageId`). The existing `customBookId: int` field on the serialized librarian and in the `setCustomization` action cannot uniquely identify a workshop book.

## Goals / Non-Goals

**Goals:**
- Workshop key pages with a non-empty `CharacterSkin` appear in the fashion list in the Projection tab.
- Selecting a workshop page as projection sends it to the backend and equips it correctly.
- The active projection is serialized correctly when it comes from a workshop book.

**Non-Goals:**
- Showing workshop pages that have no `CharacterSkin` (no visible skin difference).
- Appearance preview sprites for workshop body skins (those are extracted separately by `AppearanceCache`).
- Deduplication with the custom core book inventory — if a mod adds a book to both inventories it will appear twice; that is an edge case out of scope.

## Decisions

### Extend `fashionBooks` with workshop entries

**Decision:** Add workshop key pages with `CharacterSkin` from `BookInventoryModel.GetBookList_equip()` to the existing `fashionBooks` array in the serialized state. Add an optional `packageId` field to `FashionBook`.

**Alternatives considered:**
- *Flag `availableKeyPages` with `hasFashion`*: Would require the frontend to merge two lists and change the ProjectionTab's data source, adding complexity without benefit.
- *Separate `workshopFashionPages` array*: Redundant with `fashionBooks`; the frontend would need to merge them for display anyway.

### Compound identity: parallel `packageId` field

**Decision:** Add `packageId?: string` to `FashionBook`. Fashion books from the core inventory omit it (implying `""` / vanilla). The `active` check in the tab becomes `book.id === customBookId && (book.packageId ?? "") === customBookPackageId`. The `setCustomization` action gains an optional `customBookPackageId` string field.

**Alternatives considered:**
- *Encode as `"packageId:id"` string*: Requires changing `customBookId` from `int` to `string` everywhere, including the existing non-workshop path and the serialized librarian field.
- *Use `instanceId`*: Instance IDs are session-local and not stable across reloads; unsuitable as a persistent identifier.

### Backend lookup via `LorId(id, packageId)`

When `customBookPackageId` is non-empty in `setCustomization`, the handler uses `Singleton<BookXmlList>.Instance?.GetData(new LorId(cbid, packageId))` instead of `new LorId(cbid)`. This correctly resolves the workshop book's XML data before calling `EquipCustomCoreBook`.

**Note:** `EquipCustomCoreBook(new BookModel(bxi))` requires ilspycmd confirmation that it accepts any `BookModel`, not only those from `CustomCoreBookInventoryModel`. If it validates the source inventory, an alternative path (e.g., cloning from the equip inventory) may be needed.

### Frontend draft: parallel `customBookPackageId` field

The `CustomizePanel` draft and `ProjectionTab` props gain a `customBookPackageId: string` alongside `customBookId: number`. When the user selects a fashion book, both fields are updated together. When unequipping (`-1`), `customBookPackageId` is reset to `""`.

## Risks / Trade-offs

- **`EquipCustomCoreBook` may reject non-inventory books** → Verify with ilspycmd before implementing the handler; fall back to constructing from the equip inventory if needed.
- **Numeric ID collision across mods** → Two workshop mods could use the same `.id` value; `packageId` disambiguates, but `AppearanceCache.FashionMeta` is currently keyed by `int` only. Workshop skin sprites will fall back to the default preview if there is a collision (low likelihood).
- **Double appearance in list** → If a mod registers its key page in both `CustomCoreBookInventoryModel` and `BookInventoryModel`, it may appear twice. Not harmful, just visual noise. Deduplication can be added later.

## Open Questions

- ~~Does `EquipCustomCoreBook` accept any `BookModel`, or only one sourced from `CustomCoreBookInventoryModel`?~~ **Resolved:** `EquipCustomCoreBook` accepts any `BookModel`; it only validates `canNotEquip` and range-type compatibility.
- `LorId(string packageId, int id)` — note parameter order (packageId first). Use `new LorId(packageId, cbid)` in the handler.
