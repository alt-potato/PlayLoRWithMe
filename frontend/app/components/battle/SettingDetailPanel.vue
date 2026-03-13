<!--
  Inline detail panel shown when a BattleSetting unit card is expanded.

  Displays max HP, max stagger gauge, speed range, resistance table, passives,
  and deck card preview. All data is optional — sections are omitted when absent.

  Props:
    unit – the pre-battle unit (Unit | AllyUnit from GameState)
    flip – when true, flip horizontal layout for right-aligned ally cards
-->
<script setup lang="ts">
import type { Unit, AllyUnit, Card, DeckCardPreview } from "~/types/game";

const props = defineProps<{
  unit: Unit | AllyUnit;
  flip?: boolean;
}>();

const kp = computed(() => props.unit.keyPage);
const passives = computed(() => props.unit.passives ?? []);
const deck = computed(() => props.unit.deckPreview ?? []);

const totalCards = computed(() =>
  deck.value.reduce((sum, c) => sum + (c.count ?? 1), 0),
);

/**
 * Converts a DeckCardPreview to a minimal Card shape so it can be passed to
 * HandCard. id/index are set to the list position — HandCard does not use them
 * internally; they're only needed for type compatibility.
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

const detailCard = ref<Card | null>(null);
</script>

<template>
  <div class="detail-panel" :class="{ 'detail-panel--flip': flip }">
    <!-- Resistance table -->
    <div class="section-label">Resistances</div>
    <UnitResistanceTable v-if="kp?.resistances" :resistances="kp.resistances" />

    <!-- Passives -->
    <template v-if="passives.length">
      <div class="section-label">Passives</div>
      <div class="passive-list">
        <div
          v-for="(p, i) in passives"
          :key="i"
          class="passive-row"
          :class="{ 'passive-row--negative': p.isNegative }"
        >
          <span
            class="passive-name"
            :style="{ color: rarityColor(p.rare ?? '') }"
            >{{ p.name }}</span
          >
          <span v-if="p.desc" class="passive-desc">{{ p.desc }}</span>
        </div>
      </div>
    </template>

    <!-- Deck preview — uses HandCard tiles; long-press opens CardDetail -->
    <template v-if="deck.length">
      <div class="section-label">
        Deck
        <span class="section-count">{{ totalCards }}</span>
      </div>
      <div class="deck-cards">
        <HandCard
          v-for="(card, i) in deck"
          :key="i"
          :card="previewToCard(card, i)"
          :count="card.count"
          readonly
          @detail="detailCard = previewToCard(card, i)"
        />
      </div>
    </template>

    <CardDetail
      v-if="detailCard"
      :card="detailCard"
      @close="detailCard = null"
    />
  </div>
</template>

<style scoped>
.detail-panel {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

/* ── Section labels ───────────────────────────────────────────────────────── */
.section-label {
  font-family: var(--font-display);
  font-size: 0.5rem;
  text-transform: uppercase;
  letter-spacing: 0.12em;
  color: var(--text-3);
  margin-top: 0.1rem;
  display: flex;
  align-items: center;
  gap: 0.3em;
}

.detail-panel--flip .section-label {
  justify-content: flex-end;
}

@media (max-width: 599px) {
  .detail-panel--flip .section-label {
    justify-content: flex-start;
  }
}

.section-count {
  color: var(--text-3);
  font-family: var(--font-body);
  font-size: 0.58rem;
  letter-spacing: 0;
}

/* ── Passives ─────────────────────────────────────────────────────────────── */
.passive-list {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.passive-row {
  display: flex;
  flex-direction: column;
  gap: 0.05rem;
}

.detail-panel--flip .passive-row {
  align-items: flex-end;
}

@media (max-width: 599px) {
  .detail-panel--flip .passive-row {
    align-items: flex-start;
  }
}

.passive-name {
  font-family: var(--font-display);
  font-size: 0.6rem;
  font-weight: 600;
  letter-spacing: 0.03em;
}

.passive-row--negative .passive-name {
  color: var(--text-red) !important;
}

.passive-desc {
  font-family: var(--font-body);
  font-size: 0.58rem;
  color: var(--text-3);
  line-height: 1.35;
}

/* ── Deck preview ─────────────────────────────────────────────────────────── */
.deck-cards {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}
</style>
