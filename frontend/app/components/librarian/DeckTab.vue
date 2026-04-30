<!--
  DeckTab.vue

  Deck editor inside the librarian EditPanel.
  Left column: equipped deck cards (tap to remove).
  Right column: available card inventory with CardFilter above (tap to add).

  Props:
    lib              – librarian being edited
    state            – full game state (provides availableCards)
    editBusy         – true while an async action is in-flight
    onAddCard        – callback to add a card to the deck
    onRemoveCard     – callback to remove one copy from the deck
-->
<script setup lang="ts">
import type { LibrarianEntry, GameState, AvailableCard, DeckCardPreview, Card, ActionResult } from "~/types/game";

const props = defineProps<{
  lib: LibrarianEntry;
  state: GameState;
  editBusy: boolean;
  onAddCard: (card: AvailableCard) => Promise<ActionResult>;
  onRemoveCard: (card: DeckCardPreview) => Promise<ActionResult>;
}>();

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
 * deckPreview mutation. Each entry stands in for one optimistic copy
 * change (add or remove) for a given (cardId, packageId) pair. Reconciliation
 * is FIFO by `addedAt`, so the oldest pending edit for a key clears first
 * when the matching delta lands.
 *
 * `card` carries the pre-converted Card payload so pending-add tiles can
 * render without re-looking-up the source AvailableCard / DeckCardPreview.
 */
type PendingDeckEdit = {
  cardId: number;
  packageId: string;
  card: Card;
  addedAt: number;
};

const pendingAdds = ref<PendingDeckEdit[]>([]);
const pendingRemoves = ref<PendingDeckEdit[]>([]);

/** Compose the (cardId, packageId) FIFO key used by pending edits and deckCardCounts. */
function pendingKey(cardId: number, packageId: string): string {
  return `${cardId}_${packageId}`;
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
    if (e && pendingKey(e.cardId, e.packageId) === key) {
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

/** Builds a key → count map from a deckPreview list. Pure helper so it
 * can be shared between the reactive `deckCardCounts` and the
 * reconciliation watcher's snapshot. */
function countDeckPreview(preview: DeckCardPreview[]): Map<string, number> {
  const map = new Map<string, number>();
  for (const entry of preview) {
    if (!entry.cardId) continue;
    const key = pendingKey(entry.cardId.id, entry.cardId.packageId);
    map.set(key, (map.get(key) ?? 0) + entry.count);
  }
  return map;
}

/** Map of "cardId_packageId" → copies already in the deck. */
const deckCardCounts = computed(() => countDeckPreview(props.lib.deckPreview));

/**
 * Mutable snapshot of the previous deckPreview counts, used by the
 * reconciliation watcher to compute per-key deltas. Initialized from the
 * current preview so the first mutation after mount diffs against the
 * mounted state, not an empty map.
 */
let prevDeckCounts = countDeckPreview(props.lib.deckPreview);

/**
 * Reconciliation watcher: every deckPreview mutation produces per-key
 * count deltas, which clear pending edits FIFO. A positive delta on key
 * `K` (server confirmed an add) drops the oldest pending-add for `K`;
 * a negative delta drops the oldest pending-remove. The action-promise
 * is intentionally not consulted here — the diff alone is the source of
 * truth for "what the server actually did".
 */
watch(
  () => props.lib.deckPreview,
  (next) => {
    const nextCounts = countDeckPreview(next);
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
 * Expands the grouped `deckPreview` (one entry per unique card with a `count`)
 * into one tile per physical copy. The deck-editor surface mirrors the
 * 9-slot deck the game actually equips, so duplicates need to occupy
 * distinct visible slots rather than collapse behind a ×N badge.
 */
const expandedDeck = computed(() =>
  props.lib.deckPreview.flatMap((entry) =>
    Array.from({ length: entry.count }, () => entry),
  ),
);

/**
 * Deck size the cap math reasons about — mirrors what the deck WILL be
 * once pending edits reconcile. Confirmed tiles plus in-flight adds
 * minus in-flight removes. Used by the deck-count badge, placeholder
 * count, and the inventory cap gate so all three stay in sync with the
 * user's intent rather than the lagging confirmed state.
 */
const effectiveDeckCount = computed(() =>
  Math.max(0, expandedDeck.value.length + pendingAdds.value.length - pendingRemoves.value.length),
);

/** Empty slots remaining; auto-filled with default cards before combat. Clamped at 0. */
const emptySlotCount = computed(() =>
  Math.max(0, DECK_MAX - effectiveDeckCount.value),
);

/**
 * Per-card copy count adjusted for in-flight edits — mirrors what the
 * deck WILL hold of each card once pending adds/removes reconcile. The
 * inventory's at-limit gate consults this rather than the raw confirmed
 * counts so a user can't queue past the per-rarity cap by tapping
 * faster than the server responds.
 */
const effectiveDeckCardCounts = computed(() => {
  const map = new Map(deckCardCounts.value);
  for (const p of pendingAdds.value) {
    const k = pendingKey(p.cardId, p.packageId);
    map.set(k, (map.get(k) ?? 0) + 1);
  }
  for (const p of pendingRemoves.value) {
    const k = pendingKey(p.cardId, p.packageId);
    map.set(k, (map.get(k) ?? 0) - 1);
  }
  return map;
});

function isAtLimit(card: AvailableCard): boolean {
  const key = pendingKey(card.cardId.id, card.cardId.packageId);
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
 * cycle as the tap. On `ok: false` the oldest matching pending-add is
 * dropped silently. Successes are cleared by the deckPreview-diff
 * watcher once the matching delta lands.
 */
async function handleAddCard(card: AvailableCard) {
  const entry: PendingDeckEdit = {
    cardId: card.cardId.id,
    packageId: card.cardId.packageId,
    card: availableToCard(card, pendingAdds.value.length),
    addedAt: Date.now(),
  };
  pendingAdds.value.push(entry);
  const result = await props.onAddCard(card);
  if (!result.ok) {
    dropOldest(pendingAdds.value, pendingKey(entry.cardId, entry.packageId));
  }
}

/**
 * Per-key count of in-flight pending-removes. Used to (a) gate enqueueing
 * a duplicate remove for an already-pending tile and (b) decide which
 * confirmed tiles to dim in the rendered deck.
 */
const pendingRemoveCounts = computed(() => {
  const map = new Map<string, number>();
  for (const p of pendingRemoves.value) {
    const key = pendingKey(p.cardId, p.packageId);
    map.set(key, (map.get(key) ?? 0) + 1);
  }
  return map;
});

/**
 * Walks `expandedDeck` and marks the first N tiles per key as
 * `pendingRemove`, where N is the in-flight pending-remove count for
 * that key. Tiles for the same cardId are interchangeable, so picking
 * the leftmost ones is purely a visual choice.
 */
const renderedDeck = computed(() => {
  const remaining = new Map(pendingRemoveCounts.value);
  return expandedDeck.value.map((preview) => {
    if (!preview.cardId) return { preview, pendingRemove: false };
    const key = pendingKey(preview.cardId.id, preview.cardId.packageId);
    const left = remaining.get(key) ?? 0;
    if (left > 0) {
      remaining.set(key, left - 1);
      return { preview, pendingRemove: true };
    }
    return { preview, pendingRemove: false };
  });
});

/**
 * Optimistic remove: dims the tile in place and dispatches the action.
 * Short-circuits when every confirmed copy of this card is already
 * pending-remove, preventing duplicate requests on rapid multi-tap.
 * On `ok: false` the oldest matching pending-remove is dropped silently;
 * successes are cleared by the deckPreview-diff watcher.
 */
async function handleRemoveCard(preview: DeckCardPreview) {
  if (!preview.cardId) return;
  const key = pendingKey(preview.cardId.id, preview.cardId.packageId);
  const confirmed = deckCardCounts.value.get(key) ?? 0;
  const alreadyPending = pendingRemoveCounts.value.get(key) ?? 0;
  if (alreadyPending >= confirmed) return;

  const entry: PendingDeckEdit = {
    cardId: preview.cardId.id,
    packageId: preview.cardId.packageId,
    card: previewToCard(preview, pendingRemoves.value.length),
    addedAt: Date.now(),
  };
  pendingRemoves.value.push(entry);
  const result = await props.onRemoveCard(preview);
  if (!result.ok) {
    dropOldest(pendingRemoves.value, key);
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
      <div class="card-grid">
        <div
          v-for="(entry, i) in renderedDeck"
          :key="`copy-${i}`"
          class="deck-tile"
          :class="{ 'is-pending-remove': entry.pendingRemove }"
        >
          <HandCard
            :card="previewToCard(entry.preview, i)"
            :unusable="editBusy || !entry.preview.cardId"
            @click="handleRemoveCard(entry.preview)"
            @detail="detailCard = previewToCard(entry.preview, i)"
          />
        </div>
        <!-- pending-add tiles render after the confirmed deck so the user
             sees the new card "land" at the end of the deck while waiting
             for the server's deckPreview delta. -->
        <div
          v-for="(p, i) in pendingAdds"
          :key="`pending-add-${i}-${p.addedAt}`"
          class="pending-tile deck-tile"
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

/*
 * Pending-remove tile wrapper. Dims the existing HandCard in place
 * without a spinner — the user already targeted this specific tile,
 * so a spinner would just clutter. The wrapper exists so the dim
 * effect doesn't compose with HandCard's own opacity-driven states
 * (e.g. .hcard--unusable) in surprising ways during edge cases.
 */
.deck-tile {
  flex-shrink: 0;
}
.deck-tile.is-pending-remove {
  opacity: 0.4;
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
