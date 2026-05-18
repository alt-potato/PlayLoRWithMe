<!--
  KeyPageTab.vue

  Key page picker tab inside the librarian EditPanel. Chapter-filtered grid of
  available inventory key pages on the left, detailed stat view on the right.

  Every librarian carries an immutable origin/base key page
  (`UnitDataModel.defaultBook` in the engine). The base is NOT rendered as a
  selectable tile — it's a pure fallback that the engine auto-equips when
  no inventory page is selected. The user returns to it by selecting their
  currently-equipped inventory tile and pressing "Unequip"; the action button
  re-labels itself based on which tile is selected.

  Props:
    lib           – librarian being edited
    state         – full game state (provides availableKeyPages)
    editBusy      – true while an async action is in-flight (disables actions)
    onEquipPage   – callback to equip the selected inventory key page
    onUnequipPage – callback to return the librarian to their base
-->
<script setup lang="ts">
import type { LibrarianEntry, AvailableKeyPage, GameState } from "~/types/game";
import type { AnyKeyPage } from "~/components/librarian/KeyPageDetail.vue";
import { toggleSet } from "~/utils/setReactive";
import { rarityStyle } from "~/utils/rarityStyle";

function rarityStyleFor(kp: AvailableKeyPage): Record<string, string> {
  return rarityStyle({
    rarity: kp.rarity,
    rarityColor: kp.rarityColor,
    rarityRangeIconColor: kp.rarityRangeIconColor,
    rarityAbilityColor: kp.rarityAbilityColor,
    rarityKeywordColor: kp.rarityKeywordColor,
  });
}

const props = defineProps<{
  lib: LibrarianEntry;
  state: GameState;
  editBusy: boolean;
  onEquipPage: (kp: AvailableKeyPage) => Promise<void>;
  onUnequipPage: () => Promise<void>;
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

/**
 * Selected inventory tile's `instanceId`, or `null` for the default state.
 * When `null`, the selection logically resolves to the currently-equipped
 * page (which may be the base — `lib.keyPage` always reflects whatever the
 * engine considers active).
 */
const selectedInstanceId = ref<number | null>(null);

/** True when the librarian is currently on their base key page. */
const isOnBase = computed(
  () => props.lib.keyPage.instanceId === props.lib.baseKeyPage.instanceId,
);

/**
 * Detail-pane content. Defaults to the currently-equipped page (which may be
 * the base — when on-base, `lib.keyPage` IS the base, carrying the same
 * fields as `lib.baseKeyPage`). Always returns a concrete `KeyPage` so the
 * detail panel never blanks out — users want to see what they're wearing,
 * regardless of whether it's an inventory page or the engine fallback.
 */
const selectedPage = computed((): AnyKeyPage => {
  const id = selectedInstanceId.value ?? props.lib.keyPage.instanceId;
  if (id != null) {
    const found = availableKeyPages.value.find((kp) => kp.instanceId === id);
    if (found) return found;
  }
  // The equipped page won't appear in `availableKeyPages` (that list only
  // includes free-to-equip pages); fall back to the librarian-owned keyPage
  // object, which always carries the full shape.
  return props.lib.keyPage;
});

function selectPage(kp: AvailableKeyPage) {
  selectedInstanceId.value = kp.instanceId;
}

const equipError = ref<string | null>(null);

/**
 * Three button states. Note that "unequip" is reached only by selecting the
 * librarian's currently-equipped inventory tile — there is no separate base
 * tile or dedicated Unequip control.
 *   "equip"  — selected is an inventory page not currently equipped.
 *   "unequip"— selected is the currently-equipped inventory page (off-base).
 *   "hidden" — no valid selection (on-base with nothing clicked, or selection
 *              points at a stale instanceId).
 */
type ActionState = "equip" | "unequip" | "hidden";

const actionState = computed((): ActionState => {
  const id = selectedInstanceId.value ?? props.lib.keyPage.instanceId;
  if (id == null) return "hidden";
  if (id === props.lib.keyPage.instanceId) {
    // selection (default or explicit) is the equipped page: only "unequip"
    // makes sense, and only when there's actually something to unequip from
    // (i.e. the equipped page isn't already the base).
    return isOnBase.value ? "hidden" : "unequip";
  }
  const found = availableKeyPages.value.find((kp) => kp.instanceId === id);
  return found ? "equip" : "hidden";
});

const actionLabel = computed(() =>
  actionState.value === "unequip" ? "Unequip" : "Equip",
);

async function performAction() {
  equipError.value = null;
  try {
    if (actionState.value === "unequip") {
      await props.onUnequipPage();
    } else if (actionState.value === "equip") {
      const id = selectedInstanceId.value ?? props.lib.keyPage.instanceId;
      const kp = availableKeyPages.value.find((k) => k.instanceId === id);
      if (kp) await props.onEquipPage(kp);
    }
  } catch (e) {
    equipError.value = String(e);
  }
}
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
              :style="rarityStyleFor(kp)"
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
      <!--
        The action button is the ONLY entry point to unequipping. Selecting
        the librarian's currently-equipped tile (the default selection when
        off-base) flips its label to "Unequip" and switches the button to
        the destructive red variant. Any other tile reads "Equip". When
        the librarian is already on their base, no action is available and
        the button stays hidden.
      -->
      <button
        v-if="actionState !== 'hidden'"
        class="equip-btn"
        :class="{ 'equip-btn--unequip': actionState === 'unequip' }"
        :disabled="editBusy"
        @click="performAction"
      >
        {{ actionLabel }}
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
  /* --rarity-color is set via :style when the key page has a rarity field; */
  /* falls back to the default border colour for combat-context payloads. */
  border: 1px solid var(--rarity-color, var(--border));
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

/*
 * Unequip variant: destructive accent so the user sees at a glance that
 * pressing this returns the librarian to their base (rather than equipping
 * something new). Uses --crimson-hi for parity with other destructive cues
 * in the editor (e.g. the equip-error text below).
 */
.equip-btn--unequip {
  border-color: var(--crimson-hi);
  color: var(--crimson-hi);
}

.equip-btn--unequip:not(:disabled):hover {
  background: var(--crimson-hi);
  color: var(--text-1);
}

.equip-error {
  font-size: var(--fs-xs);
  color: var(--crimson-hi);
}
</style>
