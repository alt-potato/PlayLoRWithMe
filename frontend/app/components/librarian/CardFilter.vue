<!--
  CardFilter.vue

  Filtering controls for the card inventory. Filters are composable (AND logic).
  Emits the filtered card list whenever any control changes.

  Props:
    cards – full list of AvailableCard to filter

  Emits:
    filtered – filtered AvailableCard[] after applying all active filters
-->
<script setup lang="ts">
import type { AvailableCard } from "~/types/game";

const props = defineProps<{ cards: AvailableCard[] }>();
const emit = defineEmits<{ filtered: [cards: AvailableCard[]] }>();

const searchText = ref("");
/** Chapter is single-select (at most one value) — "All" = no filter. */
const chapterFilter = ref<string>("All");
const showAdvanced = ref(false);
/**
 * All remaining filters are multi-select: an empty set means "no filter",
 * otherwise a card matches if any selected value matches (OR within a
 * filter, AND across filters).
 */
const costFilter = ref<Set<string>>(new Set());
const rarityFilter = ref<Set<string>>(new Set());
const diceCountFilter = ref<Set<string>>(new Set());
const diceTypeFilter = ref<Set<string>>(new Set());

const chapters = computed(() => {
  const set = new Set(
    props.cards
      .map((c) => c.chapter)
      .filter((ch): ch is number => ch != null),
  );
  return ["All", ...Array.from(set).sort((a, b) => a - b).map(String)];
});

const rarities = computed(() => {
  const set = new Set(props.cards.map((c) => c.rarity).filter(Boolean));
  return Array.from(set).sort();
});

/** All distinct die detail values across the card pool. */
const diceTypes = computed(() => {
  const set = new Set<string>();
  for (const c of props.cards) {
    for (const d of c.dice ?? []) {
      if (d.detail) set.add(d.detail);
    }
  }
  return Array.from(set).sort();
});

const COST_LABELS = ["0", "1", "2", "3", "4", "5+"];
const DICE_COUNT_LABELS = ["0", "1", "2", "3", "4+"];

/** Toggle a value in a Set-backed ref; replaces the Set to trigger reactivity. */
function toggleSet(setRef: Ref<Set<string>>, value: string) {
  const next = new Set(setRef.value);
  if (next.has(value)) next.delete(value);
  else next.add(value);
  setRef.value = next;
}

/** "5+" matches cost >= 5; all other labels match exact numeric cost. */
function matchesCost(card: AvailableCard, selection: Set<string>): boolean {
  if (selection.size === 0) return true;
  const label = card.cost >= 5 ? "5+" : String(card.cost);
  return selection.has(label);
}

/** "4+" matches dice count >= 4; all other labels match exact count. */
function matchesDiceCount(card: AvailableCard, selection: Set<string>): boolean {
  if (selection.size === 0) return true;
  const count = (card.dice ?? []).length;
  const label = count >= 4 ? "4+" : String(count);
  return selection.has(label);
}

const filtered = computed(() => {
  const search = searchText.value.trim().toLowerCase();
  const ch = chapterFilter.value === "All" ? null : Number(chapterFilter.value);
  const raritySet = rarityFilter.value;
  const costSet = costFilter.value;
  const diceCountSet = diceCountFilter.value;
  const diceTypeSet = diceTypeFilter.value;

  return props.cards.filter((c) => {
    if (
      search &&
      !c.name.toLowerCase().includes(search) &&
      !c.abilityDesc?.toLowerCase().includes(search)
    )
      return false;
    if (ch != null && c.chapter !== ch) return false;
    if (raritySet.size > 0 && !raritySet.has(c.rarity)) return false;
    if (!matchesCost(c, costSet)) return false;
    if (!matchesDiceCount(c, diceCountSet)) return false;
    if (diceTypeSet.size > 0) {
      const cardDetails = new Set((c.dice ?? []).map((d) => d.detail));
      if (![...diceTypeSet].some((dt) => cardDetails.has(dt))) return false;
    }
    return true;
  });
});

watch(filtered, (val) => emit("filtered", val), { immediate: true });
</script>

<template>
  <div class="card-filter">
    <!-- Text search -->
    <input
      v-model="searchText"
      class="filter-search"
      placeholder="Search cards..."
      type="search"
    />

    <!-- Chapter pills (single-select; "All" is the implicit default) -->
    <div v-if="chapters.length > 2" class="filter-pills">
      <button
        v-for="ch in chapters"
        :key="ch"
        class="filter-pill filter-pill--single"
        :class="{ active: chapterFilter === ch }"
        @click="chapterFilter = ch"
      >
        {{ ch === "All" ? "All" : `Ch.${ch}` }}
      </button>
    </div>

    <!-- Advanced toggle -->
    <button class="advanced-toggle" @click="showAdvanced = !showAdvanced">
      {{ showAdvanced ? "▾" : "▸" }} Advanced
    </button>

    <template v-if="showAdvanced">
      <!-- Cost (multi-select) -->
      <div class="filter-section-label">Cost</div>
      <div class="filter-pills">
        <button
          v-for="label in COST_LABELS"
          :key="label"
          class="filter-pill"
          :class="{ active: costFilter.has(label) }"
          @click="toggleSet(costFilter, label)"
        >
          {{ label }}
        </button>
      </div>

      <!-- Rarity (multi-select) -->
      <div v-if="rarities.length" class="filter-section-label">Rarity</div>
      <div v-if="rarities.length" class="filter-pills">
        <button
          v-for="r in rarities"
          :key="r"
          class="filter-pill"
          :class="{ active: rarityFilter.has(r) }"
          @click="toggleSet(rarityFilter, r)"
        >
          {{ r }}
        </button>
      </div>

      <!-- Dice count (multi-select) -->
      <div class="filter-section-label">Dice count</div>
      <div class="filter-pills">
        <button
          v-for="label in DICE_COUNT_LABELS"
          :key="label"
          class="filter-pill"
          :class="{ active: diceCountFilter.has(label) }"
          @click="toggleSet(diceCountFilter, label)"
        >
          {{ label }}
        </button>
      </div>

      <!-- Dice type (multi-select) -->
      <div v-if="diceTypes.length" class="filter-section-label">Dice type</div>
      <div v-if="diceTypes.length" class="filter-pills">
        <button
          v-for="dt in diceTypes"
          :key="dt"
          class="filter-pill"
          :class="{ active: diceTypeFilter.has(dt) }"
          @click="toggleSet(diceTypeFilter, dt)"
        >
          {{ dt }}
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.card-filter {
  display: flex;
  flex-direction: column;
  gap: var(--sp-2);
  flex-shrink: 0;
}

.filter-search {
  padding: var(--sp-2) var(--sp-3);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-mid);
  background: var(--bg-card-2);
  color: var(--text-1);
  font-size: var(--fs-sm);
  width: 100%;
  box-sizing: border-box;
  transition: border-color var(--duration-fast) var(--ease-out),
    box-shadow var(--duration-fast) var(--ease-out);
}

.filter-search::placeholder {
  color: var(--text-3);
  font-size: var(--fs-sm);
}

.filter-search:focus {
  outline: none;
  border-color: var(--gold-dim);
  box-shadow: var(--shadow-gold);
}

.filter-pills {
  display: flex;
  flex-wrap: wrap;
  gap: var(--sp-1);
}

.filter-pill {
  font-size: var(--fs-xs);
  font-family: var(--font-display);
  padding: var(--sp-1) var(--sp-3);
  border-radius: var(--radius-pill);
  border: 1px solid var(--border-mid);
  background: transparent;
  color: var(--text-2);
  cursor: pointer;
  transition: background var(--duration-fast) var(--ease-out),
    color var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out);
  white-space: nowrap;
}

.filter-pill:hover {
  color: var(--text-1);
  border-color: var(--border-hi);
}

/*
 * Multi-select pills (cost, rarity, dice count/type) use a subdued tint
 * when active — they can stack, so no single toggle should dominate.
 */
.filter-pill.active {
  background: var(--gold-ink);
  color: var(--gold-bright);
  border-color: var(--gold-dim);
}

/*
 * Single-select pills (chapter) use a bolder solid fill — one and only
 * one value is the current selection, and the user should feel it.
 */
.filter-pill--single.active {
  background: var(--gold);
  color: var(--gold-ink);
  border-color: var(--gold-bright);
}

.advanced-toggle {
  font-size: var(--fs-xs);
  color: var(--text-3);
  background: transparent;
  border: none;
  cursor: pointer;
  text-align: left;
  padding: 0;
  font-family: var(--font-display);
  text-transform: uppercase;
  letter-spacing: 0.06em;
}

.advanced-toggle:hover {
  color: var(--text-2);
}

.filter-section-label {
  font-size: var(--fs-xs);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-2);
  font-family: var(--font-display);
  margin-top: var(--sp-1);
}
</style>
