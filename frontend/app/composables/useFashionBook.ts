/**
 * useFashionBook.ts
 *
 * Shared resolver for the "active fashion book" — the body source fed into
 * LibrarianAppearancePreview. Three callers (LibrarianManager, EditPanel,
 * CustomizePanel) all need this same three-tier resolution, differing only in
 * whether the source fields come from the server-authoritative LibrarianEntry
 * or from the CustomizePanel's local draft. Keeping this as a plain function
 * (not a composable wrapping `computed`) lets each caller wrap the call in
 * their own `computed(...)` without fighting the reactivity contract.
 *
 * Auto-imported by Nuxt; no import statements needed in .vue files.
 */

import type { FashionBook, LibrarianEntry, CustomizeOptions } from "~/types/game";

/**
 * Source fields that determine which fashion book is active. Accepts both the
 * server-authoritative values on `LibrarianEntry` and the local draft values
 * used by the customize panel.
 */
export interface FashionBookSource {
  workshopSkin?: string | null;
  customBookId?: number | null;
  customBookPackageId?: string | null;
}

/**
 * Resolves the fashion book to render in the appearance preview, in priority
 * order: workshop skin (cloth overlay) -> custom book projection -> key page's
 * own body composite. Returns null when the librarian has no body at all.
 */
export function resolveFashionBook(
  source: FashionBookSource,
  lib: LibrarianEntry,
  customizeOptions: CustomizeOptions | null | undefined,
): FashionBook | null {
  // 1. workshop skin (cloth overlay)
  if (source.workshopSkin) {
    const skin = customizeOptions?.workshopSkins?.find(
      (s) => s.contentFolderIdx === source.workshopSkin,
    );
    if (skin) {
      return {
        id: 0,
        fileStem: `ws_${skin.contentFolderIdx}`,
        name: skin.name,
        rangeType: "Hybrid",
        replacesHead: skin.replacesHead ?? false,
        hasFrontLayer: skin.hasFrontLayer,
        headTiltDeg: skin.headTiltDeg,
        pivotFracX: skin.pivotFracX,
        pivotFracY: skin.pivotFracY,
        feetYFrac: skin.feetYFrac,
        bodyW: skin.bodyW,
        bodyH: skin.bodyH,
      };
    }
  }

  // 2. custom book projection
  const projId = source.customBookId;
  if (projId != null && projId >= 0) {
    const projPkg = source.customBookPackageId ?? "";
    const found = customizeOptions?.fashionBooks?.find(
      (fb) => fb.id === projId && (fb.packageId ?? "") === projPkg,
    );
    if (found) return found;
  }

  // 3. key page's own body composite
  const bookId = lib.keyPage.bookId;
  if (bookId == null) return null;
  return {
    id: bookId,
    packageId: lib.keyPage.bookPackageId,
    name: lib.keyPage.name,
    rangeType: lib.keyPage.equipRangeType ?? "",
    replacesHead: lib.keyPageReplacesHead ?? false,
    hasFrontLayer: lib.keyPageHasFrontLayer,
    headTiltDeg: lib.keyPageHeadTiltDeg,
    pivotFracX: lib.keyPagePivotFracX,
    pivotFracY: lib.keyPagePivotFracY,
    hidesBackHair: lib.keyPageHidesBackHair,
    skinGender: lib.keyPageSkinGender,
    feetYFrac: lib.keyPageFeetYFrac,
    bodyW: lib.keyPageBodyW,
    bodyH: lib.keyPageBodyH,
  };
}
