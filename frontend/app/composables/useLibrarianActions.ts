import type { InjectionKey } from "vue";
import type { ActionResult } from "~/types/game";

/**
 * Actions available on the librarian management screen, provided by app.vue
 * and injected by LibrarianManager and its descendants. Avoids prop-drilling
 * callbacks through multiple component layers.
 */
export interface LibrarianActions {
  sendAction: (action: Record<string, unknown>) => Promise<ActionResult>;
  lockLibrarian: (floorIndex: number, unitIndex: number) => Promise<ActionResult>;
  unlockLibrarian: (floorIndex: number, unitIndex: number) => Promise<ActionResult>;
  renameLibrarian: (floorIndex: number, unitIndex: number, name: string) => Promise<ActionResult>;
  equipKeyPage: (floorIndex: number, unitIndex: number, bookInstanceId: number) => Promise<ActionResult>;
  addCardToDeck: (floorIndex: number, unitIndex: number, cardId: number, packageId: string) => Promise<ActionResult>;
  removeCardFromDeck: (floorIndex: number, unitIndex: number, cardId: number, packageId: string) => Promise<ActionResult>;
}

export const LIBRARIAN_ACTIONS: InjectionKey<LibrarianActions> = Symbol("LibrarianActions");

/** Accent color keyed by floorIndex (0 = Malkuth ... 9 = Keter). */
export const FLOOR_COLORS: Record<number, string> = {
  0: "#be9966", // Malkuth
  1: "#6968c4", // Yesod
  2: "#e5881b", // Hod
  3: "#4ed564", // Netzach
  4: "#ffe527", // Tiphereth
  5: "#ff3326", // Gebura
  6: "#5ccaf6", // Chesed
  7: "#957704", // Binah
  8: "#7c7b7c", // Hokma
  9: "#dddddd", // Keter
};

/** Returns the accent color for a given floor index, falling back to grey. */
export function floorColor(floorIdx: number): string {
  return FLOOR_COLORS[floorIdx] ?? "#888";
}
