<!--
  KeyPageTab.vue

  Key page picker tab inside the librarian EditPanel. Displays a chapter-filtered
  grid of available key pages on the left and a detailed stat view on the right.
  The currently selected tile defaults to the librarian's equipped key page.

  Props:
    lib         – librarian being edited
    state       – full game state (provides availableKeyPages)
    editBusy    – true while an async action is in-flight (disables equip)
    onEquipPage – callback to equip the selected key page
-->
<script setup lang="ts">
import type { LibrarianEntry, AvailableKeyPage, GameState } from "~/types/game";
import type { AnyKeyPage } from "~/components/librarian/KeyPageDetail.vue";

const props = defineProps<{
  lib: LibrarianEntry;
  state: GameState;
  editBusy: boolean;
  onEquipPage: (kp: AvailableKeyPage) => Promise<void>;
}>();

const availableKeyPages = computed(() => props.state.availableKeyPages ?? []);

/**
 * Maps key page instanceId → the name of the librarian currently using it.
 * Used to show an "Equipped by X" label on key page tiles.
 */
const equippedByMap = computed(() => {
  const map = new Map<number, string>();
  for (const floor of props.state.floors ?? []) {
    for (const lib of floor.librarians) {
      const id = lib.keyPage.instanceId;
      if (id != null) map.set(id, lib.name);
    }
  }
  return map;
});

/** Sorted distinct chapter values, descending (matches server sort). */
const chapters = computed(() => {
  const set = new Set(availableKeyPages.value.map((kp) => kp.chapter));
  return ["All", ...Array.from(set).sort((a, b) => b - a).map(String)];
});

const chapterFilter = ref("All");

const filteredPages = computed(() => {
  if (chapterFilter.value === "All") return availableKeyPages.value;
  const ch = Number(chapterFilter.value);
  return availableKeyPages.value.filter((kp) => kp.chapter === ch);
});

/** Selected key page for the detail view. Default = current equipped page from inventory. */
const selectedInstanceId = ref<number | null>(null);

const selectedPage = computed((): AnyKeyPage => {
  if (selectedInstanceId.value != null) {
    const found = availableKeyPages.value.find(
      (kp) => kp.instanceId === selectedInstanceId.value,
    );
    if (found) return found;
  }
  // Fallback: find the currently equipped key page in inventory, or show existing keyPage data.
  const current = availableKeyPages.value.find(
    (kp) => kp.instanceId === props.lib.keyPage.instanceId,
  );
  return current ?? props.lib.keyPage;
});

function selectPage(kp: AvailableKeyPage) {
  selectedInstanceId.value = kp.instanceId;
}

const equipError = ref<string | null>(null);

async function equip(kp: AvailableKeyPage) {
  equipError.value = null;
  try {
    await props.onEquipPage(kp);
  } catch (e) {
    equipError.value = String(e);
  }
}

const isCurrentEquipped = computed(() => {
  if (selectedInstanceId.value == null) return true;
  return selectedInstanceId.value === props.lib.keyPage.instanceId;
});

const selectedIsAvailable = computed(
  () =>
    selectedInstanceId.value != null &&
    availableKeyPages.value.some((kp) => kp.instanceId === selectedInstanceId.value),
);
</script>

<template>
  <div class="kp-tab">
    <!-- Chapter filter pills -->
    <div class="chapter-pills">
      <button
        v-for="ch in chapters"
        :key="ch"
        class="chapter-pill"
        :class="{ active: chapterFilter === ch }"
        @click="chapterFilter = ch"
      >
        {{ ch === "All" ? "All" : `Ch.${ch}` }}
      </button>
    </div>

    <div class="kp-columns">
      <!-- Left: key page grid -->
      <div class="kp-grid-col">
        <div v-if="!filteredPages.length" class="kp-empty">No key pages available.</div>
        <button
          v-for="kp in filteredPages"
          :key="kp.instanceId"
          class="kp-tile"
          :class="{
            'kp-tile--selected': selectedInstanceId === kp.instanceId,
            'kp-tile--equipped': kp.instanceId === lib.keyPage.instanceId,
          }"
          @click="selectPage(kp)"
        >
          <span class="kp-tile-name">{{ kp.name }}</span>
          <span v-if="equippedByMap.get(kp.instanceId)" class="kp-tile-equipped-by">
            {{ equippedByMap.get(kp.instanceId) }}
          </span>
          <span class="kp-tile-speed">{{ kp.speedMin }}–{{ kp.speedMax }}</span>
        </button>
      </div>

      <!-- Right: detail + equip action -->
      <div class="kp-detail-col">
        <LibrarianKeyPageDetail :key-page="selectedPage" />
        <div v-if="equipError" class="equip-error">{{ equipError }}</div>
        <button
          class="equip-btn"
          :disabled="editBusy || isCurrentEquipped || !selectedIsAvailable"
          @click="selectedIsAvailable && equip(availableKeyPages.find(kp => kp.instanceId === selectedInstanceId)!)"
        >
          {{ isCurrentEquipped ? "Equipped" : "Equip" }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.kp-tab {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  height: 100%;
  overflow: hidden;
}

.chapter-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 0.3rem;
  flex-shrink: 0;
}

.chapter-pill {
  font-size: 0.65rem;
  padding: 0.2rem 0.5rem;
  border-radius: 999px;
  border: 1px solid var(--border-mid);
  background: transparent;
  color: var(--text-2);
  cursor: pointer;
  transition: background 0.15s, color 0.15s;
}

.chapter-pill.active {
  background: var(--gold);
  color: #000;
  border-color: var(--gold);
}

.kp-columns {
  display: flex;
  gap: 0.75rem;
  flex: 1;
  overflow: hidden;
  min-height: 0;
}

.kp-grid-col {
  flex: 1;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.kp-detail-col {
  flex: 1;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  border-left: 1px solid var(--border);
  padding-left: 0.75rem;
}

.kp-empty {
  font-size: 0.72rem;
  color: var(--text-3);
  padding: 0.5rem;
}

.kp-tile {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.3rem 0.5rem;
  border-radius: 4px;
  border: 1px solid var(--border);
  background: transparent;
  color: var(--text-1);
  cursor: pointer;
  text-align: left;
  transition: background 0.12s, border-color 0.12s;
  font-size: 0.72rem;
}

.kp-tile:hover {
  background: var(--bg-2, rgba(255, 255, 255, 0.04));
}

.kp-tile--selected {
  border-color: var(--gold);
  background: rgba(201, 162, 39, 0.12);
}

.kp-tile--equipped {
  border-left: 3px solid var(--gold);
}

.kp-tile-name {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.kp-tile-equipped-by {
  flex-shrink: 0;
  font-size: 0.6rem;
  color: var(--gold);
  opacity: 0.8;
  margin-left: 0.4rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 6rem;
}

.kp-tile-speed {
  flex-shrink: 0;
  color: var(--text-3);
  font-size: 0.65rem;
  margin-left: 0.4rem;
}

.equip-btn {
  padding: 0.35rem 0.9rem;
  border-radius: 4px;
  border: 1px solid var(--gold);
  background: transparent;
  color: var(--gold);
  cursor: pointer;
  font-size: 0.72rem;
  font-family: var(--font-display);
  transition: background 0.12s, color 0.12s;
  align-self: flex-start;
  margin-top: auto;
}

.equip-btn:not(:disabled):hover {
  background: var(--gold);
  color: #000;
}

.equip-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.equip-error {
  font-size: 0.65rem;
  color: var(--crimson-hi);
}

/* Mobile: stack columns vertically */
@media (max-width: 599px) {
  .kp-columns {
    flex-direction: column;
  }

  .kp-detail-col {
    border-left: none;
    border-top: 1px solid var(--border);
    padding-left: 0;
    padding-top: 0.5rem;
  }
}
</style>
