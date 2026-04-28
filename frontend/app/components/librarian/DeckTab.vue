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
import type { LibrarianEntry, GameState, AvailableCard, DeckCardPreview, Card } from "~/types/game";

const props = defineProps<{
  lib: LibrarianEntry;
  state: GameState;
  editBusy: boolean;
  onAddCard: (card: AvailableCard) => Promise<void>;
  onRemoveCard: (card: DeckCardPreview) => Promise<void>;
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

/** Map of "cardId_packageId" → copies already in the deck. */
const deckCardCounts = computed(() => {
  const map = new Map<string, number>();
  for (const entry of props.lib.deckPreview) {
    if (!entry.cardId) continue;
    const key = `${entry.cardId.id}_${entry.cardId.packageId}`;
    map.set(key, (map.get(key) ?? 0) + entry.count);
  }
  return map;
});

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

/** Empty slots remaining; auto-filled with default cards before combat. Clamped at 0. */
const emptySlotCount = computed(() =>
  Math.max(0, DECK_MAX - expandedDeck.value.length),
);

function isAtLimit(card: AvailableCard): boolean {
  const key = `${card.cardId.id}_${card.cardId.packageId}`;
  return (deckCardCounts.value.get(key) ?? 0) >= cardLimit(card.rarity);
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
          :unusable="editBusy || card.count <= 0 || isAtLimit(card)"
          @click="onAddCard(card)"
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
        <span class="deck-count">{{ expandedDeck.length }} / {{ DECK_MAX }}</span>
      </div>
      <LibrarianKeyPageDetail class="deck-keypage" :key-page="lib.keyPage" :compact="true" />
      <div class="card-grid">
        <HandCard
          v-for="(card, i) in expandedDeck"
          :key="`copy-${i}`"
          :card="previewToCard(card, i)"
          :unusable="editBusy || !card.cardId"
          @click="onRemoveCard(card)"
          @detail="detailCard = previewToCard(card, i)"
        />
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
