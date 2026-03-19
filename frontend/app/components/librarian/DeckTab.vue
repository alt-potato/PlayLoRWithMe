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
  // Mirrors BookModel.AddCardFromInventoryToCurrentDeck range checks.
  if (rangeType === "Melee")
    filtered = cards.filter((c) => c.range !== "Far");
  else if (rangeType === "Range")
    filtered = cards.filter((c) => c.range !== "Near");
  else
    filtered = cards;
  return [...onlyCards, ...filtered];
});
const filteredCards = ref<AvailableCard[]>([]);

const deckTotal = computed(() =>
  props.lib.deckPreview.reduce((s, c) => s + c.count, 0),
);

/** Per-rarity copy limit, mirroring DiceCardXmlInfo.GetCardLimit. */
function cardLimit(rarity: string): number {
  return rarity === "Unique" ? 1 : 3;
}

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

function isAtLimit(card: AvailableCard): boolean {
  const key = `${card.cardId.id}_${card.cardId.packageId}`;
  return (deckCardCounts.value.get(key) ?? 0) >= cardLimit(card.rarity);
}

const detailCard = ref<Card | null>(null);

/**
 * Converts a DeckCardPreview to a minimal Card shape for HandCard rendering.
 * id/index are positional; HandCard does not use them for actions.
 */
function previewToCard(p: DeckCardPreview, i: number): Card {
  return {
    id: { id: i, packageId: 0 },
    index: i,
    name: p.name,
    cost: p.cost,
    range: p.range,
    rarity: p.rarity,
    dice: p.dice,
    abilityDesc: p.abilityDesc,
  };
}

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
    <!-- Left: equipped deck — click a card to remove one copy -->
    <div class="deck-col deck-col--equipped">
      <div class="col-header">
        Deck
        <span class="col-count">{{ deckTotal }}</span>
      </div>
      <div v-if="!lib.deckPreview.length" class="col-empty">No cards equipped.</div>
      <div v-else class="card-grid">
        <HandCard
          v-for="(card, i) in lib.deckPreview"
          :key="(card.cardId?.id ?? i) + '_' + (card.cardId?.packageId ?? i)"
          :card="previewToCard(card, i)"
          :count="card.count"
          :unusable="editBusy || !card.cardId"
          @click="onRemoveCard(card)"
          @detail="detailCard = previewToCard(card, i)"
        />
      </div>
    </div>

    <!-- Right: available cards — click a card to add one copy -->
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

    <CardDetail v-if="detailCard" :card="detailCard" @close="detailCard = null" />
  </div>
</template>

<style scoped>
.deck-tab {
  display: flex;
  gap: 0.75rem;
  height: 100%;
  overflow: hidden;
  min-height: 0;
}

.deck-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  gap: 0.4rem;
}

.deck-col--available {
  border-left: 1px solid var(--border);
  padding-left: 0.75rem;
}

.col-header {
  font-size: 0.65rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-3);
  display: flex;
  align-items: center;
  gap: 0.4rem;
  flex-shrink: 0;
}

.col-count {
  background: var(--border-mid);
  border-radius: 999px;
  padding: 0 0.4rem;
  font-size: 0.6rem;
  color: var(--text-2);
}

.col-empty {
  font-size: 0.72rem;
  color: var(--text-3);
  padding: 0.3rem 0;
}

.card-grid {
  overflow-y: auto;
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  align-content: flex-start;
  flex: 1;
  min-height: 0;
}

/* Mobile: stack columns */
@media (max-width: 599px) {
  .deck-tab {
    flex-direction: column;
  }

  .deck-col--available {
    border-left: none;
    border-top: 1px solid var(--border);
    padding-left: 0;
    padding-top: 0.5rem;
  }
}
</style>
