<!--
  DeckTab.vue

  Deck editor inside the librarian EditPanel.
  Left column: equipped deck cards (tap to remove).
  Right column: available card inventory with CardFilter above (tap to add).

  For multi-deck key pages (e.g. The Purple Tear) a tab strip above the deck
  column exposes all four deck slots with stance labels resolved client-side.
  Pending edits and cap math are tracked per (deckIndex, cardId+packageId).

  Props:
    lib              – librarian being edited
    state            – full game state (provides availableCards)
    editBusy         – true while an async action is in-flight
    onAddCard        – callback to add a card to the active deck slot
    onRemoveCard     – callback to remove one copy from the active deck slot
-->
<script setup lang="ts">
import type { LibrarianEntry, GameState, AvailableCard, DeckCardPreview, Card, ActionResult } from "~/types/game";
import { STATE_GENERATION } from "~/composables/useStateGeneration";
import { resolveDeckLabels } from "~/utils/multiDeckLabels";

const props = defineProps<{
  lib: LibrarianEntry;
  state: GameState;
  editBusy: boolean;
  onAddCard: (card: AvailableCard, deckIndex: number) => Promise<ActionResult>;
  onRemoveCard: (card: DeckCardPreview, deckIndex: number) => Promise<ActionResult>;
}>();

/**
 * Active deck slot index. Local to the editor — the wire shape carries
 * `deckIndex` on every action, so the server has no notion of "currently
 * selected tab". Defaults to 0 (the first slot, always present).
 */
const activeDeckIndex = ref(0);

/** True when the equipped key page exposes more than one deck slot. */
const isMultiDeck = computed(() => props.lib.keyPage.isMultiDeck === true);

/**
 * Snapshot of the librarian's deck slots. `decks` is guaranteed by the
 * serializer to have length 1 or 4, but consumers defensively fall back to a
 * single empty slot rather than crash if a malformed payload arrives.
 */
const decks = computed(() =>
  props.lib.decks?.length ? props.lib.decks : [{ index: 0, cards: [] }],
);

/** Cards in the active tab's deck slot. */
const activeDeckCards = computed<DeckCardPreview[]>(
  () => decks.value.find((d) => d.index === activeDeckIndex.value)?.cards ?? [],
);

/**
 * Fallback stance/deck labels, indexed by deck index 0..3. Resolved
 * client-side via the small `multiDeckLabels` table when the wire payload
 * omits a per-deck `label` (the mod resolves that through
 * BattleEffectTextsXmlList for known books, so it carries the player's
 * game-language strings; unknown books fall back here).
 */
const fallbackDeckLabels = computed(() =>
  resolveDeckLabels(props.lib.keyPage.bookPackageId, props.lib.keyPage.bookId),
);

/** Label for a given deck index — prefers the wire label, then the fallback. */
function deckLabelFor(deckIndex: number): string {
  const fromWire = decks.value.find((d) => d.index === deckIndex)?.label;
  if (fromWire) return fromWire;
  return fallbackDeckLabels.value[deckIndex] ?? `Deck ${deckIndex + 1}`;
}

/**
 * Cards compatible with this librarian's key page range type.
 * Melee pages can only equip Near range cards; Range pages can only equip
 * non-Near cards; Hybrid pages can equip anything.
 */
/**
 * Cards available to add to this librarian's deck.
 * Page-exclusive (OnlyPage) cards for this key page are prepended so they
 * appear first regardless of cost. The rest are range-filtered per BookXmlInfo.RangeType:
 * Melee blocks Far; Range blocks Near; Hybrid allows all.
 */
const allAvailableCards = computed(() => {
  const onlyCards = props.lib.onlyCards ?? [];
  const cards = props.state.availableCards ?? [];
  const rangeType = props.lib.keyPage.equipRangeType;
  let filtered: typeof cards;
  // Filter by equip range: Melee pages can't use Far cards; Range pages can't use Near cards.
  if (rangeType === "Melee")
    filtered = cards.filter((c) => c.range !== "Far");
  else if (rangeType === "Range")
    filtered = cards.filter((c) => c.range !== "Near");
  else
    filtered = cards;
  return [...onlyCards, ...filtered];
});
const filteredCards = ref<AvailableCard[]>([]);

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
type PendingDeckEdit = {
  deckIndex: number;
  cardId: number;
  packageId: string;
  card: Card;
  addedAt: number;
};

const pendingAdds = ref<PendingDeckEdit[]>([]);
const pendingRemoves = ref<PendingDeckEdit[]>([]);

/** Compose the (deckIndex, cardId, packageId) FIFO key. */
function pendingKey(deckIndex: number, cardId: number, packageId: string): string {
  return `${deckIndex}_${cardId}_${packageId}`;
}

/**
 * Removes the oldest pending edit whose key matches. Mutates in place
 * because pending arrays are FIFO; entries are appended on tap and
 * removed front-to-back as deltas arrive. Returns true if a match was
 * found and dropped.
 */
function dropOldest(arr: PendingDeckEdit[], key: string): boolean {
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
function cardLimit(rarity: string): number {
  return rarity === "Unique" ? 1 : 3;
}

/**
 * Maximum cards a deck can hold. Mirrors `DeckModel.maxDeckCount` in the
 * game DLL — slots beyond what the player explicitly equips are filled
 * with default cards before combat starts, so we surface them visually
 * as placeholder tiles.
 */
const DECK_MAX = 9;

/**
 * Builds a per-deck-slot count map keyed by `pendingKey(deckIndex, cardId, packageId)`.
 * Shared between the reactive `deckCardCounts` and the reconciliation
 * watcher's snapshot so both diff against the same shape.
 */
function countDecks(decksArr: { index: number; cards: DeckCardPreview[] }[]): Map<string, number> {
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

/** Map of `(deckIndex, cardId, packageId)` → confirmed copies. */
const deckCardCounts = computed(() => countDecks(decks.value));

/**
 * Mutable snapshot of the previous per-deck counts, used by the
 * reconciliation watcher to compute per-key deltas. Initialised from the
 * current decks array so the first mutation after mount diffs against the
 * mounted state, not an empty map.
 */
let prevDeckCounts = countDecks(decks.value);

/**
 * Reconciliation watcher: every `decks` mutation produces per-key count
 * deltas, which clear pending edits FIFO on the matching deck slot. A
 * positive delta on `(deckIndex, cardId, packageId)` (server confirmed an
 * add to that slot) drops the oldest pending-add for that key; negative
 * delta drops the oldest pending-remove. The action-promise is
 * intentionally not consulted — the diff alone is the source of truth.
 */
watch(
  () => props.lib.decks,
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
  { deep: true },
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
 * Connection-reset cleanup: a fresh full-state replacement (initial
 * connect, reconnect, or resync) bumps STATE_GENERATION. Any pending
 * edits queued before the bump may have been lost server-side, so we
 * discard them rather than leave phantom tiles. The new full state is
 * the new authoritative baseline; the diff watcher resets to it on the
 * next decks tick.
 */
const stateGeneration = inject(STATE_GENERATION, ref(0));
watch(stateGeneration, () => {
  pendingAdds.value = [];
  pendingRemoves.value = [];
  prevDeckCounts = countDecks(decks.value);
});

/**
 * Expands the active tab's grouped card list (one entry per unique card with
 * a `count`) into one tile per physical copy. The deck-editor surface
 * mirrors the 9-slot deck the game actually equips, so duplicates need to
 * occupy distinct visible slots rather than collapse behind a ×N badge.
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


const detailCard = ref<Card | null>(null);

function availableToCard(c: AvailableCard, i: number): Card {
  return {
    id: { id: c.cardId.id, packageId: Number(c.cardId.packageId) || 0 },
    index: i,
    name: c.name,
    cost: c.cost,
    range: c.range,
    rarity: c.rarity,
    dice: c.dice,
    abilityDesc: c.abilityDesc,
  };
}

/**
 * Optimistic add: pushes a pending-add entry before awaiting the server
 * response so the deck-editor reflects the change in the same render
 * cycle as the tap. The pending entry carries the active deck index so
 * the diff watcher only clears it when that slot's count increments.
 */
async function handleAddCard(card: AvailableCard) {
  const deckIndex = activeDeckIndex.value;
  const entry: PendingDeckEdit = {
    deckIndex,
    cardId: card.cardId.id,
    packageId: card.cardId.packageId,
    card: availableToCard(card, pendingAdds.value.length),
    addedAt: Date.now(),
  };
  pendingAdds.value.push(entry);
  const result = await props.onAddCard(card, deckIndex);
  if (!result.ok) {
    dropOldest(pendingAdds.value, pendingKey(deckIndex, entry.cardId, entry.packageId));
  }
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

/**
 * Optimistic remove: dims the tile in place and dispatches the action.
 * Short-circuits when every confirmed copy of this card on the active
 * deck slot is already pending-remove, preventing duplicate requests on
 * rapid multi-tap.
 */
async function handleRemoveCard(preview: DeckCardPreview) {
  if (!preview.cardId) return;
  const deckIndex = activeDeckIndex.value;
  const cardKey = `${preview.cardId.id}_${preview.cardId.packageId}`;
  const fullKey = pendingKey(deckIndex, preview.cardId.id, preview.cardId.packageId);
  const confirmed = deckCardCounts.value.get(fullKey) ?? 0;
  const alreadyPending = pendingRemoveCounts.value.get(cardKey) ?? 0;
  if (alreadyPending >= confirmed) return;

  const entry: PendingDeckEdit = {
    deckIndex,
    cardId: preview.cardId.id,
    packageId: preview.cardId.packageId,
    card: previewToCard(preview, pendingRemoves.value.length),
    addedAt: Date.now(),
  };
  pendingRemoves.value.push(entry);
  const result = await props.onRemoveCard(preview, deckIndex);
  if (!result.ok) {
    dropOldest(pendingRemoves.value, fullKey);
  }
}
</script>

<template>
  <div class="deck-tab">
    <!-- Left: available cards — filter + click to add one copy -->
    <div class="deck-col deck-col--available">
      <div class="col-header">Add Cards</div>
      <LibrarianCardFilter :cards="allAvailableCards" @filtered="filteredCards = $event" />
      <div v-if="!filteredCards.length" class="col-empty">No cards match.</div>
      <div v-else class="card-grid">
        <HandCard
          v-for="(card, i) in filteredCards"
          :key="card.cardId.id + '_' + card.cardId.packageId"
          :card="availableToCard(card, i)"
          :count="card.count"
          :unusable="
            editBusy ||
            card.count <= 0 ||
            isAtLimit(card) ||
            effectiveDeckCount >= DECK_MAX
          "
          @click="handleAddCard(card)"
          @detail="detailCard = availableToCard(card, i)"
        />
      </div>
    </div>

    <!-- Right: equipped deck — click a card to remove one copy. Empty slots
         are surfaced as placeholders to communicate the 9-card cap and the
         fact that the engine will auto-fill them with default cards. -->
    <div class="deck-col deck-col--equipped">
      <div class="col-header">
        Deck
        <span class="deck-count">{{ effectiveDeckCount }} / {{ DECK_MAX }}</span>
      </div>
      <LibrarianKeyPageDetail class="deck-keypage" :key-page="lib.keyPage" :compact="true" />
      <!-- Multi-deck tab strip — only rendered for key pages with the
           BookOption.MultiDeck flag (e.g. The Purple Tear). Single-deck books
           (the 99% case) hide this row entirely so the editor visually matches
           its pre-multi-deck shape. The active tab's count is already shown
           in the column header's deck-count badge above. -->
      <div v-if="isMultiDeck" class="deck-tabs" role="tablist" aria-label="Deck slot">
        <button
          v-for="d in decks"
          :key="`deck-tab-${d.index}`"
          class="deck-tab-btn"
          :class="{ active: d.index === activeDeckIndex }"
          role="tab"
          :aria-selected="d.index === activeDeckIndex"
          @click="activeDeckIndex = d.index"
        >
          {{ deckLabelFor(d.index) }}
        </button>
      </div>
      <div class="card-grid">
        <HandCard
          v-for="(preview, i) in renderedDeck"
          :key="`copy-${i}`"
          :card="previewToCard(preview, i)"
          :unusable="editBusy || !preview.cardId"
          @click="handleRemoveCard(preview)"
          @detail="detailCard = previewToCard(preview, i)"
        />
        <!-- pending-add tiles for the active tab only render after the
             confirmed deck so the user sees the new card "land" at the end
             of the deck while waiting for the server's delta on this slot. -->
        <div
          v-for="(p, i) in pendingAddsForActive"
          :key="`pending-add-${p.deckIndex}-${i}-${p.addedAt}`"
          class="pending-tile"
        >
          <HandCard
            :card="p.card"
            :readonly="true"
            @detail="detailCard = p.card"
          />
          <span class="pending-spinner" aria-label="Adding card" />
        </div>
        <div
          v-for="i in emptySlotCount"
          :key="`placeholder-${i}`"
          class="deck-placeholder"
          :title="`Empty slot ${expandedDeck.length + i} — auto-filled with a default card before combat.`"
        ></div>
      </div>
    </div>

    <CardDetail v-if="detailCard" :card="detailCard" @close="detailCard = null" />
  </div>
</template>

<style scoped>
.deck-tab {
  display: flex;
  flex-direction: column;
  gap: var(--sp-3);
  height: 100%;
  overflow: hidden;
  min-height: 0;
}

.deck-col {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  gap: var(--sp-2);
  flex: 1;
  min-height: 0;
}

.col-header {
  font-size: var(--fs-md);
  font-family: var(--font-display);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--gold-bright);
  flex-shrink: 0;
  display: flex;
  align-items: baseline;
  gap: var(--sp-2);
}

/* Compact "N / MAX" indicator next to the Deck header. Muted so the
   header text remains the focal element and the count reads as metadata. */
.deck-count {
  font-family: var(--font-body);
  font-size: var(--fs-xs);
  letter-spacing: 0;
  text-transform: none;
  color: var(--text-3);
}

.col-empty {
  font-size: var(--fs-xs);
  color: var(--text-3);
  padding: var(--sp-2) 0;
}

.card-grid {
  /* explicit shorthand: horizontal is clipped (no scrollbar even when a
     hovered card's absolute-positioned detail overlay extends past the
     grid); vertical scrolls as before. without explicit overflow-x the
     browser promotes it to `auto` alongside overflow-y, producing a
     horizontal scrollbar whenever the overlay pokes out. */
  overflow: hidden auto;
  scrollbar-gutter: stable;
  display: flex;
  flex-wrap: wrap;
  gap: var(--sp-2);
  align-content: flex-start;
  flex: 1;
  min-height: 0;
}

.deck-keypage {
  flex-shrink: 0;
  padding-top: 0;
  padding-bottom: var(--sp-1);
}

/*
 * Multi-deck tab strip — matches the EditPanel's `.tab-bar` underline
 * pattern (transparent background, gold underline when active) so the
 * deck selector reads as navigation rather than a control. Tabs wrap on
 * narrow viewports because Purple Tear's four stance labels can blow past
 * the equipped column width at mobile sizes.
 */
.deck-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 0;
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

.deck-tab-btn {
  padding: var(--sp-2) var(--sp-3);
  background: transparent;
  border: none;
  color: var(--text-3);
  cursor: pointer;
  font-size: var(--fs-sm);
  font-family: var(--font-display);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  transition:
    color var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out);
}

.deck-tab-btn:hover {
  color: var(--text-1);
}

.deck-tab-btn.active {
  color: var(--gold-bright);
  border-bottom-color: var(--gold-bright);
}

/*
 * Pending-add tile wrapper. The inner HandCard renders normally; the
 * wrapper provides reduced opacity and a corner spinner so the user
 * sees the card while it's being committed. `pointer-events: none` on
 * the spinner keeps long-press detail open (HandCard handles its own
 * touch events through its root div).
 */
.pending-tile {
  position: relative;
  opacity: 0.5;
  flex-shrink: 0;
}

.pending-spinner {
  position: absolute;
  top: 0.2rem;
  right: 0.2rem;
  width: 0.7rem;
  height: 0.7rem;
  border: 2px solid var(--gold-bright);
  border-top-color: transparent;
  border-radius: 50%;
  animation: pending-spin 0.7s linear infinite;
  pointer-events: none;
}

@keyframes pending-spin {
  to { transform: rotate(360deg); }
}

/*
 * Empty deck slot tile. Shape and width match HandCard's preview pane
 * (5.5rem wide, 5:7 aspect ratio) so equipped cards and placeholders
 * line up on a shared baseline grid. Dashed border + muted fill mark
 * the tile as a non-card slot rather than an unusable card.
 */
.deck-placeholder {
  flex-shrink: 0;
  width: 5.5rem;
  aspect-ratio: 5 / 7;
  border: 1px dashed var(--border-mid);
  background: var(--bg-card-2);
  opacity: 0.55;
  cursor: default;
  user-select: none;
  /* HandCard's border wraps the 5.5rem preview from outside, so the visible
     card occupies 5.5rem + 2px. Opting into content-box makes the dashed
     border sit outside the 5.5rem box too, matching the card footprint
     instead of shrinking 2px under the global border-box default. */
  box-sizing: content-box;
}

/*
 * Side-by-side at >=700px. Layout mirrors KeyPageTab: browse on the left
 * (filter + many tiles), details on the right (equipped deck — capped at
 * 9 cards so it only needs a narrow strip). Hairline divider between.
 */
@media (min-width: 700px) {
  .deck-tab {
    flex-direction: row;
    gap: var(--sp-3);
  }

  .deck-col--available {
    flex: 1;
  }

  .deck-col--equipped {
    flex: 0 0 35%;
    border-left: 1px solid var(--border);
    padding-left: var(--sp-3);
  }
}

/* Roomier breathing space at the wide desktop breakpoint. */
@media (min-width: 1200px) {
  .deck-tab {
    gap: var(--sp-3);
    padding: var(--sp-4);
  }

  .deck-col--equipped {
    flex: 0 0 30%;
    padding-left: var(--sp-4);
  }
}
</style>
