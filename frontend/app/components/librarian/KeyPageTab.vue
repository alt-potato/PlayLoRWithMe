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
import { toggleSet } from "~/utils/setReactive";

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

/** Sorted distinct chapter values, ascending (1 → 7). */
const chapters = computed(() => {
  const set = new Set(availableKeyPages.value.map((kp) => kp.chapter));
  return ["All", ...Array.from(set).sort((a, b) => a - b).map(String)];
});

const chapterFilter = ref("All");

const filteredPages = computed(() => {
  if (chapterFilter.value === "All") return availableKeyPages.value;
  const ch = Number(chapterFilter.value);
  return availableKeyPages.value.filter((kp) => kp.chapter === ch);
});

interface BookGroup {
  bookIcon: string;
  name: string;
  pages: AvailableKeyPage[];
}

/** Key pages grouped by book, preserving backend sort order. Empty groups omitted. */
const groupedPages = computed((): BookGroup[] => {
  const groups: BookGroup[] = [];
  const seen = new Map<string, BookGroup>();
  for (const kp of filteredPages.value) {
    let group = seen.get(kp.bookIcon);
    if (!group) {
      group = { bookIcon: kp.bookIcon, name: kp.bookGroupName, pages: [] };
      seen.set(kp.bookIcon, group);
      groups.push(group);
    }
    group.pages.push(kp);
  }
  return groups;
});

/** Tracks which book groups are collapsed by bookIcon. */
const collapsedGroups = ref(new Set<string>());

const toggleGroup = (bookIcon: string) => toggleSet(collapsedGroups, bookIcon);

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
    <!-- Left: filter + key page grid -->
    <div class="kp-col kp-col--grid">
      <div class="col-header">Key Pages</div>
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
      <div class="kp-grid">
        <div v-if="!groupedPages.length" class="col-empty">No key pages available.</div>
        <template v-for="group in groupedPages" :key="group.bookIcon">
          <button
            class="book-group-header"
            :class="{ 'book-group-header--collapsed': collapsedGroups.has(group.bookIcon) }"
            @click="toggleGroup(group.bookIcon)"
          >
            <span class="book-group-chevron">{{ collapsedGroups.has(group.bookIcon) ? "▸" : "▾" }}</span>
            <span class="book-group-name">{{ group.name }}</span>
          </button>
          <template v-if="!collapsedGroups.has(group.bookIcon)">
            <button
              v-for="kp in group.pages"
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
          </template>
        </template>
      </div>
    </div>

    <!-- Right: detail + equip action -->
    <div class="kp-col kp-col--detail">
      <div class="col-header">Details</div>
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
</template>

<style scoped>
/*
 * Layout mirrors DeckTab: two columns that stack on mobile and go
 * side-by-side at >=700px, with a hairline border divider, matching
 * .col-header, .col-empty, and breakpoint padding.
 */
.kp-tab {
  display: flex;
  flex-direction: column;
  gap: var(--sp-3);
  height: 100%;
  overflow: hidden;
  min-height: 0;
}

.kp-col {
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
}

.col-empty {
  font-size: var(--fs-xs);
  color: var(--text-3);
  padding: var(--sp-2) 0;
}

.chapter-pills {
  display: flex;
  flex-wrap: wrap;
  gap: var(--sp-1);
  flex-shrink: 0;
}

.chapter-pill {
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
}

/*
 * Chapter is a single-select filter — bolder solid-gold fill on the
 * active pill to match CardFilter's .filter-pill--single treatment.
 */
.chapter-pill.active {
  background: var(--gold);
  color: var(--gold-ink);
  border-color: var(--gold-bright);
}

.kp-grid {
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: var(--sp-1);
  flex: 1;
  min-height: 0;
  align-content: flex-start;
}

/*
 * Side-by-side at >=700px. Matches DeckTab: browse on the left (filter +
 * many tiles), details on the right (narrower pane with hairline divider).
 */
@media (min-width: 700px) {
  .kp-tab {
    flex-direction: row;
    gap: var(--sp-3);
  }

  .kp-col--grid {
    flex: 1;
  }

  .kp-col--detail {
    flex: 0 0 35%;
    border-left: 1px solid var(--border);
    padding-left: var(--sp-3);
  }
}

/* Roomier breathing space at the wide desktop breakpoint — matches DeckTab. */
@media (min-width: 1200px) {
  .kp-tab {
    gap: var(--sp-3);
    padding: var(--sp-4);
  }

  .kp-col--detail {
    flex: 0 0 30%;
    padding-left: var(--sp-4);
  }
}

.book-group-header {
  display: flex;
  align-items: center;
  gap: var(--sp-2);
  padding: var(--sp-1) var(--sp-2);
  border: none;
  background: transparent;
  color: var(--text-2);
  cursor: pointer;
  font-size: var(--fs-xs);
  font-family: var(--font-display);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  transition: color var(--duration-fast) var(--ease-out);
}

.book-group-header:hover {
  color: var(--gold-bright);
}

.book-group-chevron {
  font-size: var(--fs-2xs);
  flex-shrink: 0;
  width: 0.8em;
}

.book-group-name {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.kp-tile {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--sp-2) var(--sp-3);
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--bg-card);
  color: var(--text-1);
  cursor: pointer;
  text-align: left;
  transition: background var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out),
    box-shadow var(--duration-fast) var(--ease-out);
  font-size: var(--fs-sm);
  font-family: var(--font-display);
  box-shadow: var(--shadow-sm);
}

.kp-tile:hover {
  background: var(--bg-card-2);
  box-shadow: var(--shadow-gold);
}

.kp-tile--selected {
  border-color: var(--gold-bright);
  background: var(--gold-glow);
}

.kp-tile--equipped {
  border-left: 3px solid var(--gold-bright);
}

.kp-tile-name {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.kp-tile-equipped-by {
  flex-shrink: 0;
  font-size: var(--fs-3xs);
  color: var(--gold);
  opacity: 0.85;
  margin-left: var(--sp-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 7rem;
}

.kp-tile-speed {
  flex-shrink: 0;
  color: var(--text-3);
  font-size: var(--fs-xs);
  margin-left: var(--sp-2);
}

.equip-btn {
  padding: var(--sp-2) var(--sp-4);
  border-radius: var(--radius-md);
  border: 1px solid var(--gold);
  background: transparent;
  color: var(--gold);
  cursor: pointer;
  font-size: var(--fs-sm);
  font-family: var(--font-display);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  transition: background var(--duration-fast) var(--ease-out),
    color var(--duration-fast) var(--ease-out);
  align-self: flex-start;
  margin-top: auto;
}

.equip-btn:not(:disabled):hover {
  background: var(--gold);
  color: var(--gold-ink);
}

.equip-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.equip-error {
  font-size: var(--fs-xs);
  color: var(--crimson-hi);
}
</style>
