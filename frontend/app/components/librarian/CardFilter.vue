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
const chapterFilter = ref<string>("All");
const costFilter = ref<string>("All");
const rarityFilter = ref<string>("All");
const showAdvanced = ref(false);
/** Selected dice-type details (OR within this set, AND with others). */
const diceTypeFilter = ref<Set<string>>(new Set());
const diceCountFilter = ref<string>("All");

const chapters = computed(() => {
  const set = new Set(
    props.cards
      .map((c) => c.chapter)
      .filter((ch): ch is number => ch != null),
  );
  return ["All", ...Array.from(set).sort((a, b) => b - a).map(String)];
});

const rarities = computed(() => {
  const set = new Set(props.cards.map((c) => c.rarity).filter(Boolean));
  return ["All", ...Array.from(set).sort()];
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

function toggleDiceType(detail: string) {
  const next = new Set(diceTypeFilter.value);
  if (next.has(detail)) next.delete(detail);
  else next.add(detail);
  diceTypeFilter.value = next;
}

const filtered = computed(() => {
  const search = searchText.value.trim().toLowerCase();
  const ch = chapterFilter.value === "All" ? null : Number(chapterFilter.value);
  const cost = costFilter.value === "All" ? null : costFilter.value;
  const rarity = rarityFilter.value === "All" ? null : rarityFilter.value;
  const diceTypes = diceTypeFilter.value;
  const diceCount = diceCountFilter.value === "All" ? null : diceCountFilter.value;

  return props.cards.filter((c) => {
    if (
      search &&
      !c.name.toLowerCase().includes(search) &&
      !c.abilityDesc?.toLowerCase().includes(search)
    )
      return false;
    if (ch != null && c.chapter !== ch) return false;
    if (rarity && c.rarity !== rarity) return false;
    if (cost != null) {
      if (cost === "5+" ? c.cost < 5 : c.cost !== Number(cost)) return false;
    }
    if (diceCount != null) {
      const count = (c.dice ?? []).length;
      if (diceCount === "4+" ? count < 4 : count !== Number(diceCount)) return false;
    }
    if (diceTypes.size > 0) {
      const cardDetails = new Set((c.dice ?? []).map((d) => d.detail));
      if (![...diceTypes].some((dt) => cardDetails.has(dt))) return false;
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

    <!-- Chapter pills -->
    <div v-if="chapters.length > 2" class="filter-pills">
      <button
        v-for="ch in chapters"
        :key="ch"
        class="filter-pill"
        :class="{ active: chapterFilter === ch }"
        @click="chapterFilter = ch"
      >
        {{ ch === "All" ? "All" : `Ch.${ch}` }}
      </button>
    </div>

    <!-- Cost pills -->
    <div class="filter-pills">
      <button
        v-for="label in ['All', ...COST_LABELS]"
        :key="label"
        class="filter-pill"
        :class="{ active: costFilter === label }"
        @click="costFilter = label"
      >
        {{ label === "All" ? "Any cost" : label }}
      </button>
    </div>

    <!-- Rarity -->
    <div class="filter-pills">
      <button
        v-for="r in rarities"
        :key="r"
        class="filter-pill"
        :class="{ active: rarityFilter === r }"
        @click="rarityFilter = r"
      >
        {{ r }}
      </button>
    </div>

    <!-- Advanced toggle -->
    <button class="advanced-toggle" @click="showAdvanced = !showAdvanced">
      {{ showAdvanced ? "▾" : "▸" }} Advanced
    </button>

    <template v-if="showAdvanced">
      <!-- Dice count -->
      <div class="filter-section-label">Dice count</div>
      <div class="filter-pills">
        <button
          v-for="label in ['All', ...DICE_COUNT_LABELS]"
          :key="label"
          class="filter-pill"
          :class="{ active: diceCountFilter === label }"
          @click="diceCountFilter = label"
        >
          {{ label === "All" ? "Any" : label }}
        </button>
      </div>

      <!-- Dice type -->
      <div v-if="diceTypes.length" class="filter-section-label">Dice type</div>
    </template>

    <div v-if="showAdvanced && diceTypes.length" class="filter-pills">
      <button
        v-for="dt in diceTypes"
        :key="dt"
        class="filter-pill"
        :class="{ active: diceTypeFilter.has(dt) }"
        @click="toggleDiceType(dt)"
      >
        {{ dt }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.card-filter {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  flex-shrink: 0;
}

.filter-search {
  padding: 0.3rem 0.5rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: var(--bg, #1a1a1a);
  color: var(--text-1);
  font-size: 0.72rem;
  width: 100%;
  box-sizing: border-box;
}

.filter-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
}

.filter-pill {
  font-size: 0.62rem;
  padding: 0.15rem 0.45rem;
  border-radius: 999px;
  border: 1px solid var(--border-mid);
  background: transparent;
  color: var(--text-2);
  cursor: pointer;
  transition: background 0.12s, color 0.12s;
  white-space: nowrap;
}

.filter-pill.active {
  background: var(--gold);
  color: #000;
  border-color: var(--gold);
}

.advanced-toggle {
  font-size: 0.65rem;
  color: var(--text-3);
  background: transparent;
  border: none;
  cursor: pointer;
  text-align: left;
  padding: 0;
}

.filter-section-label {
  font-size: 0.6rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-3);
  margin-top: 0.1rem;
}
</style>
