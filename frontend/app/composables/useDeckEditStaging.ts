/**
 * useDeckEditStaging.ts
 *
 * Optimistic-edit reconciliation for the librarian deck editor (`DeckTab.vue`).
 *
 * Deck add/remove taps render immediately (before the server round-trip
 * resolves) so the editor stays responsive to spam-tapping. This composable
 * owns the bookkeeping that makes that safe: the FIFO pending-edit queues,
 * the diff against server-confirmed `decks` counts that clears them, the
 * per-active-tab cap/limit math the UI gates taps on, and the connection-reset
 * cleanup that discards phantom pending edits after a resync.
 *
 * It performs no I/O of its own: server truth arrives as refs, and it owns no
 * WebSocket/DOM state, which keeps the whole reconciliation state machine
 * testable without a component or a socket. See `openspec/specs/optimistic-deck-edit`
 * for the behavioural contract this implements.
 */

import { computed, ref, watch } from "vue";
import type { Ref } from "vue";
import type { AvailableCard, Card, DeckCardPreview, DeckPreview, LibrarianEntry } from "~/types/game";

/** Maximum cards a deck can hold, mirroring `DeckModel.maxDeckCount` in the game DLL. */
export const DECK_MAX = 9;

/**
 * In-flight deck edit waiting for the server to broadcast the matching
 * `decks[deckIndex]` mutation. Each entry stands in for one optimistic
 * copy change (add or remove) on a specific deck slot. Reconciliation is
 * FIFO per (deckIndex, cardId, packageId) — the oldest pending edit for a
 * key clears first when the matching delta lands on that slot.
 *
 * `card` carries the pre-converted Card payload so pending-add tiles can
 * render without re-looking-up the source AvailableCard / DeckCardPreview.
 */
export type PendingDeckEdit = {
  deckIndex: number;
  cardId: number;
  packageId: string;
  card: Card;
  addedAt: number;
};

/** Compose the (deckIndex, cardId, packageId) FIFO key. */
export function pendingKey(deckIndex: number, cardId: number, packageId: string): string {
  return `${deckIndex}_${cardId}_${packageId}`;
}

/**
 * Removes the oldest pending edit whose key matches. Mutates in place
 * because pending arrays are FIFO; entries are appended on tap and
 * removed front-to-back as deltas arrive. Returns true if a match was
 * found and dropped.
 */
export function dropOldest(arr: PendingDeckEdit[], key: string): boolean {
  for (let i = 0; i < arr.length; i++) {
    const e = arr[i];
    if (e && pendingKey(e.deckIndex, e.cardId, e.packageId) === key) {
      arr.splice(i, 1);
      return true;
    }
  }
  return false;
}

/** Maximum copies of a card allowed in a deck, by rarity. */
export function cardLimit(rarity: string): number {
  return rarity === "Unique" ? 1 : 3;
}

/**
 * Builds a per-deck-slot count map keyed by `pendingKey(deckIndex, cardId, packageId)`.
 * Shared between the reactive `deckCardCounts` and the reconciliation
 * watcher's snapshot so both diff against the same shape.
 */
function countDecks(decksArr: DeckPreview[]): Map<string, number> {
  const map = new Map<string, number>();
  for (const deck of decksArr) {
    for (const entry of deck.cards) {
      if (!entry.cardId) continue;
      const key = pendingKey(deck.index, entry.cardId.id, entry.cardId.packageId);
      map.set(key, (map.get(key) ?? 0) + entry.count);
    }
  }
  return map;
}

export interface DeckEditStagingOptions {
  /** Server truth for the librarian being edited. */
  lib: Ref<LibrarianEntry>;
  /**
   * Bumps on a fresh full-state payload across a connection boundary
   * (initial connect / reconnect). Mid-session resyncs do NOT bump this —
   * those reconcile through the `decks` diff watcher below instead, since a
   * still-alive server can confirm or reject pending edits made during the gap.
   */
  stateGeneration: Ref<number>;
}

export function useDeckEditStaging({ lib, stateGeneration }: DeckEditStagingOptions) {
  /**
   * Active deck slot index. Local to the editor — the wire shape carries
   * `deckIndex` on every action, so the server has no notion of "currently
   * selected tab". Defaults to 0 (the first slot, always present).
   */
  const activeDeckIndex = ref(0);

  /**
   * Snapshot of the librarian's deck slots. `decks` is guaranteed by the
   * serializer to have length 1 or 4, but consumers defensively fall back to a
   * single empty slot rather than crash if a malformed payload arrives.
   */
  const decks = computed<DeckPreview[]>(() =>
    lib.value.decks?.length ? lib.value.decks : [{ index: 0, cards: [] }],
  );

  /** Cards in the active tab's deck slot. */
  const activeDeckCards = computed<DeckCardPreview[]>(
    () => decks.value.find((d) => d.index === activeDeckIndex.value)?.cards ?? [],
  );

  const pendingAdds = ref<PendingDeckEdit[]>([]);
  const pendingRemoves = ref<PendingDeckEdit[]>([]);

  /** Map of `(deckIndex, cardId, packageId)` → confirmed copies. */
  const deckCardCounts = computed(() => countDecks(decks.value));

  /**
   * Mutable snapshot of the previous per-deck counts, used by the
   * reconciliation watcher to compute per-key deltas. Initialised from the
   * current decks array so the first mutation after setup diffs against the
   * mounted state, not an empty map.
   */
  let prevDeckCounts = countDecks(decks.value);

  /**
   * Reconciliation watcher: every `lib.decks` mutation produces per-key count
   * deltas, which clear pending edits FIFO on the matching deck slot. A
   * positive delta on `(deckIndex, cardId, packageId)` (server confirmed an
   * add to that slot) drops the oldest pending-add for that key; negative
   * delta drops the oldest pending-remove. The action-promise is
   * intentionally not consulted — the diff alone is the source of truth.
   *
   * Fires on every full-state replacement too (not just deltas): `gameState`
   * is a shallowRef that every patch path — initial state, delta, resync —
   * reassigns wholesale from freshly-parsed wire data (see `useWebSocket.ts`),
   * so `lib.value.decks` always gets a new array reference on those events
   * even when its content is unchanged. That guarantees this watcher runs and
   * `prevDeckCounts` never goes stale relative to the last-seen server truth,
   * with no dependency on the connection-reset watcher below also firing.
   */
  // Shallow watch — the wire patch path in `applyDelta` always reassigns
  // `lib.decks` to a new array reference when any deck slot changes, so
  // deep tracking would only add overhead. Falls back to a single-empty-slot
  // shape if the payload omits the field.
  watch(
    () => lib.value.decks,
    (next) => {
      const nextDecks = next?.length ? next : [{ index: 0, cards: [] }];
      const nextCounts = countDecks(nextDecks);
      const keys = new Set<string>([...prevDeckCounts.keys(), ...nextCounts.keys()]);
      for (const key of keys) {
        const delta = (nextCounts.get(key) ?? 0) - (prevDeckCounts.get(key) ?? 0);
        if (delta > 0) {
          for (let i = 0; i < delta; i++) dropOldest(pendingAdds.value, key);
        } else if (delta < 0) {
          for (let i = 0; i < -delta; i++) dropOldest(pendingRemoves.value, key);
        }
      }
      prevDeckCounts = nextCounts;
    },
  );

  /**
   * Clamp `activeDeckIndex` to a slot that actually exists. If the librarian
   * gets re-equipped with a single-deck key page while the editor is open on
   * tab 2, we'd otherwise render an empty deck and dispatch addCardToDeck
   * with an out-of-range deckIndex.
   */
  watch(decks, (next) => {
    const valid = new Set(next.map((d) => d.index));
    if (!valid.has(activeDeckIndex.value)) activeDeckIndex.value = 0;
  });

  /**
   * Connection-reset cleanup: a fresh full-state replacement across a
   * connection boundary (initial connect or reconnect) bumps `stateGeneration`.
   * Any pending edits queued before the bump may have been lost server-side,
   * so we discard them rather than leave phantom tiles. The new full state is
   * the new authoritative baseline; `prevDeckCounts` is reset explicitly here
   * rather than relying on the reconciliation watcher above to also fire in
   * the same tick (it does, since `lib.decks` gets a new reference too, but
   * that watcher's own trailing reset makes this redundant-by-design rather
   * than order-dependent).
   */
  watch(stateGeneration, () => {
    pendingAdds.value = [];
    pendingRemoves.value = [];
    prevDeckCounts = countDecks(decks.value);
  });

  /**
   * Expands the active tab's grouped card list (one entry per unique card with
   * a `count`) into one tile per physical copy. The deck-editor surface
   * mirrors the 9-slot deck the game actually equips, so duplicates need to
   * occupy distinct visible slots rather than collapse behind a xN badge.
   */
  const expandedDeck = computed(() =>
    activeDeckCards.value.flatMap((entry) =>
      Array.from({ length: entry.count }, () => entry),
    ),
  );

  /** Pending adds that target the active tab. */
  const pendingAddsForActive = computed(() =>
    pendingAdds.value.filter((p) => p.deckIndex === activeDeckIndex.value),
  );

  /** Pending removes that target the active tab. */
  const pendingRemovesForActive = computed(() =>
    pendingRemoves.value.filter((p) => p.deckIndex === activeDeckIndex.value),
  );

  /**
   * Deck size the cap math reasons about for the active tab — mirrors what
   * the active deck slot WILL be once pending edits reconcile. Per-deck-slot
   * because each `DeckModel` independently caps at `DECK_MAX`.
   */
  const effectiveDeckCount = computed(() =>
    Math.max(
      0,
      expandedDeck.value.length
        + pendingAddsForActive.value.length
        - pendingRemovesForActive.value.length,
    ),
  );

  /** Empty slots remaining on the active tab; auto-filled with default cards before combat. */
  const emptySlotCount = computed(() =>
    Math.max(0, DECK_MAX - effectiveDeckCount.value),
  );

  /**
   * Per-card copy count on the active deck slot, adjusted for in-flight
   * edits. Per-deck-slot rather than per-key-page because the engine's
   * `DeckModel.AddCardFromInventory` enforces copy limits independently for
   * each slot — a Rare card (limit 3) can sit at limit in deck 0 *and* deck
   * 1 simultaneously. Inventory `unusable` gating keys off this.
   */
  const effectiveDeckCardCounts = computed(() => {
    const map = new Map<string, number>();
    for (const entry of activeDeckCards.value) {
      if (!entry.cardId) continue;
      const k = `${entry.cardId.id}_${entry.cardId.packageId}`;
      map.set(k, (map.get(k) ?? 0) + entry.count);
    }
    for (const p of pendingAddsForActive.value) {
      const k = `${p.cardId}_${p.packageId}`;
      map.set(k, (map.get(k) ?? 0) + 1);
    }
    for (const p of pendingRemovesForActive.value) {
      const k = `${p.cardId}_${p.packageId}`;
      map.set(k, (map.get(k) ?? 0) - 1);
    }
    return map;
  });

  function isAtLimit(card: AvailableCard): boolean {
    const key = `${card.cardId.id}_${card.cardId.packageId}`;
    return (effectiveDeckCardCounts.value.get(key) ?? 0) >= cardLimit(card.rarity);
  }

  /**
   * Per-key count of in-flight pending-removes on the active deck slot.
   * Used to gate enqueueing a duplicate remove for an already-pending tile
   * and to drive the optimistic-hide rendering of the rendered deck.
   */
  const pendingRemoveCounts = computed(() => {
    const map = new Map<string, number>();
    for (const p of pendingRemovesForActive.value) {
      const key = `${p.cardId}_${p.packageId}`;
      map.set(key, (map.get(key) ?? 0) + 1);
    }
    return map;
  });

  /**
   * Walks `expandedDeck` and filters out one tile per pending-remove for
   * each card key (leftmost-first). Optimistic-hide model: the tapped tile
   * vanishes immediately, remaining tiles shift to close the gap, and
   * tapping the same physical position again hits the next card.
   */
  const renderedDeck = computed(() => {
    const remaining = new Map(pendingRemoveCounts.value);
    const out: DeckCardPreview[] = [];
    for (const preview of expandedDeck.value) {
      if (!preview.cardId) {
        out.push(preview);
        continue;
      }
      const key = `${preview.cardId.id}_${preview.cardId.packageId}`;
      const left = remaining.get(key) ?? 0;
      if (left > 0) {
        remaining.set(key, left - 1);
        continue; // tile is pending-remove and hidden
      }
      out.push(preview);
    }
    return out;
  });

  return {
    activeDeckIndex,
    decks,
    deckCardCounts,
    pendingAdds,
    pendingRemoves,
    pendingAddsForActive,
    pendingRemovesForActive,
    pendingRemoveCounts,
    expandedDeck,
    renderedDeck,
    effectiveDeckCount,
    emptySlotCount,
    effectiveDeckCardCounts,
    isAtLimit,
  };
}
