<!--
  PassivesTab.vue

  Passive attribution tab inside the librarian EditPanel. Left column shows
  available key pages (grouped by book) that can be equipped as passive sources;
  clicking one equips it and expands to show its transferable passives. Right
  column shows the current key page's passive slots (innate greyed, attributed
  with remove buttons) and a source key pages summary with unequip controls.

  Props:
    lib                    – librarian being edited
    state                  – full game state (provides availableKeyPages)
    editBusy               – true while an async action is in-flight
    onEquipSourceBook      – equip a key page as a passive source
    onUnequipSourceBook    – unequip a source key page
    onAttributePassive     – attribute a passive from a source
    onRemoveAttributedPassive – remove an attributed passive
-->
<script setup lang="ts">
import type {
  LibrarianEntry,
  GameState,
  AvailableKeyPage,
  Passive,
  AttributedPassive,
} from "~/types/game";

const props = defineProps<{
  lib: LibrarianEntry;
  state: GameState;
  editBusy: boolean;
  onEquipSourceBook: (bookInstanceId: number) => Promise<void>;
  onUnequipSourceBook: (bookInstanceId: number) => Promise<void>;
  onAttributePassive: (
    sourceInstanceId: number,
    passiveId: number,
    passivePackageId: string,
  ) => Promise<void>;
  onRemoveAttributedPassive: (
    sourceInstanceId: number,
    passiveId: number,
    passivePackageId: string,
  ) => Promise<void>;
}>();

const availableKeyPages = computed(() => props.state.availableKeyPages ?? []);
const sourceKeyPageIds = computed(() => new Set(props.lib.sourceKeyPageIds ?? []));
const attributedPassives = computed(() => props.lib.attributedPassives ?? []);
const innatePassives = computed(() => {
  const all = props.lib.passives ?? [];
  const attr = props.lib.attributedPassives ?? [];
  if (!attr.length) return all;
  // Remove one matching passive per attributed entry (handles duplicates)
  const remaining = [...all];
  for (const ap of attr) {
    const key = `${ap.passive.id.id}:${ap.passive.id.packageId}`;
    const idx = remaining.findIndex(
      (p) => `${p.id.id}:${p.id.packageId}` === key,
    );
    if (idx >= 0) remaining.splice(idx, 1);
  }
  return remaining;
});
const passiveSlotCount = computed(() => props.lib.passiveSlotCount ?? 0);
const maxPassiveCost = computed(() => props.lib.maxPassiveCost ?? 0);
const currentPassiveCost = computed(() => props.lib.currentPassiveCost ?? 0);

const emptySlotCount = computed(
  () => passiveSlotCount.value - innatePassives.value.length - attributedPassives.value.length,
);

// ── Chapter filter (reused from KeyPageTab) ─────────────────────────────────

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

// ── Book grouping (reused from KeyPageTab) ──────────────────────────────────

interface BookGroup {
  bookIcon: string;
  name: string;
  pages: AvailableKeyPage[];
}

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

const collapsedGroups = ref(new Set<string>());

function toggleGroup(bookIcon: string) {
  const next = new Set(collapsedGroups.value);
  if (next.has(bookIcon)) next.delete(bookIcon);
  else next.add(bookIcon);
  collapsedGroups.value = next;
}

// ── Expanded source key pages ───────────────────────────────────────────────

/** Set of instanceIds currently expanded in the left column. */
const expandedSources = ref(new Set<number>());

function toggleSourceExpansion(kp: AvailableKeyPage) {
  const next = new Set(expandedSources.value);
  if (next.has(kp.instanceId)) {
    next.delete(kp.instanceId);
  } else {
    next.add(kp.instanceId);
  }
  expandedSources.value = next;
}

function isSource(kp: AvailableKeyPage): boolean {
  return sourceKeyPageIds.value.has(kp.instanceId);
}

function isExpanded(kp: AvailableKeyPage): boolean {
  return expandedSources.value.has(kp.instanceId);
}

/** Whether a key page is ineligible (attributed to another librarian). */
function isIneligible(kp: AvailableKeyPage): boolean {
  return kp.canGivePassive === false;
}

// ── Source key page names for attribution display ───────────────────────────

const sourceNameMap = computed(() => {
  const map = new Map<number, string>();
  for (const kp of availableKeyPages.value) {
    if (sourceKeyPageIds.value.has(kp.instanceId)) {
      map.set(kp.instanceId, kp.name);
    }
  }
  return map;
});

/** Count of passives attributed from each source. */
const sourcePassiveCounts = computed(() => {
  const map = new Map<number, number>();
  for (const ap of attributedPassives.value) {
    map.set(ap.sourceInstanceId, (map.get(ap.sourceInstanceId) ?? 0) + 1);
  }
  return map;
});

// ── Actions ─────────────────────────────────────────────────────────────────

const actionError = ref<string | null>(null);

async function equipSource(kp: AvailableKeyPage) {
  actionError.value = null;
  try {
    await props.onEquipSourceBook(kp.instanceId);
    // Auto-expand after equipping.
    const next = new Set(expandedSources.value);
    next.add(kp.instanceId);
    expandedSources.value = next;
  } catch (e) {
    actionError.value = String(e);
  }
}

async function unequipSource(bookInstanceId: number) {
  actionError.value = null;
  try {
    await props.onUnequipSourceBook(bookInstanceId);
    const next = new Set(expandedSources.value);
    next.delete(bookInstanceId);
    expandedSources.value = next;
  } catch (e) {
    actionError.value = String(e);
  }
}

async function attributePassive(sourceInstanceId: number, p: Passive) {
  actionError.value = null;
  try {
    await props.onAttributePassive(sourceInstanceId, p.id.id, p.id.packageId);
  } catch (e) {
    actionError.value = String(e);
  }
}

async function removeAttributed(ap: AttributedPassive) {
  actionError.value = null;
  try {
    await props.onRemoveAttributedPassive(
      ap.sourceInstanceId,
      ap.passive.id.id,
      ap.passive.id.packageId,
    );
  } catch (e) {
    actionError.value = String(e);
  }
}

/** Whether the cost cap would be exceeded by attributing a passive with this cost. */
function wouldExceedCost(passiveCost: number): boolean {
  return currentPassiveCost.value + passiveCost > maxPassiveCost.value;
}

function hasEmptySlots(): boolean {
  return emptySlotCount.value > 0;
}
</script>

<template>
  <div class="pt-tab">
    <!-- Left: source key page browser -->
    <div class="pt-col pt-col--grid">
      <div class="col-header">Source Key Pages</div>
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
      <div class="source-grid">
        <div v-if="!groupedPages.length" class="col-empty">No key pages available.</div>
        <template v-for="group in groupedPages" :key="group.bookIcon">
          <button
            class="book-group-header"
            @click="toggleGroup(group.bookIcon)"
          >
            <span class="book-group-chevron">{{ collapsedGroups.has(group.bookIcon) ? "▸" : "▾" }}</span>
            <span class="book-group-name">{{ group.name }}</span>
          </button>
          <template v-if="!collapsedGroups.has(group.bookIcon)">
            <template v-for="kp in group.pages" :key="kp.instanceId">
              <div
                class="source-tile"
                :class="{
                  'source-tile--equipped': isSource(kp),
                  'source-tile--ineligible': isIneligible(kp),
                  'source-tile--expanded': isExpanded(kp),
                }"
                @click="toggleSourceExpansion(kp)"
              >
                <span class="source-tile-chevron">{{ isExpanded(kp) ? "▾" : "▸" }}</span>
                <span class="source-tile-name">{{ kp.name }}</span>
                <span v-if="isIneligible(kp)" class="source-tile-status">
                  In use by {{ kp.passiveGivenTo }}
                </span>
                <span v-else class="source-tile-speed">
                  {{ kp.speedMin }}–{{ kp.speedMax }}
                </span>
                <button
                  v-if="isSource(kp)"
                  class="unequip-btn unequip-btn--inline"
                  :disabled="editBusy"
                  @click.stop="unequipSource(kp.instanceId)"
                >
                  Unequip
                </button>
                <button
                  v-else-if="!isIneligible(kp)"
                  class="equip-source-btn"
                  :disabled="editBusy"
                  @click.stop="equipSource(kp)"
                >
                  Equip
                </button>
              </div>
              <div v-if="isExpanded(kp)" class="source-passives">
                <UnitPassiveList :passives="kp.passives">
                  <template v-if="isSource(kp)" #action="{ passive }">
                    <button
                      v-if="passive.canTransfer !== false"
                      class="attribute-btn"
                      :disabled="editBusy || wouldExceedCost(passive.cost ?? 0) || !hasEmptySlots()"
                      @click.stop="attributePassive(kp.instanceId, passive)"
                    >
                      Attribute
                    </button>
                    <span v-else class="unique-label">Unique</span>
                  </template>
                </UnitPassiveList>
              </div>
            </template>
          </template>
        </template>
      </div>
    </div>

    <!-- Right: current passives -->
    <div class="pt-col pt-col--detail">
      <div class="col-header">Current Passives</div>

      <!-- Cost bar -->
      <div class="cost-bar">
        <span class="cost-label">Passive Cost</span>
        <span class="cost-value">{{ currentPassiveCost }} / {{ maxPassiveCost }}</span>
      </div>

      <div class="current-passives">
        <!-- Innate passives (greyed) -->
        <div
          v-for="p in innatePassives"
          :key="'innate-' + p.id.id + p.id.packageId"
          class="current-passive current-passive--innate"
        >
          <UnitPassiveList :passives="[p]" />
        </div>

        <!-- Attributed passives -->
        <div
          v-for="ap in attributedPassives"
          :key="'attr-' + ap.passive.id.id + ap.sourceInstanceId"
          class="current-passive current-passive--attributed"
        >
          <UnitPassiveList :passives="[ap.passive]">
            <template #action="{ passive: _ }">
              <button
                class="remove-btn"
                :disabled="editBusy"
                @click="removeAttributed(ap)"
              >
                ✕
              </button>
            </template>
          </UnitPassiveList>
          <div class="attributed-source">from: {{ ap.sourceName ?? "Unknown" }}</div>
        </div>

        <!-- Empty slots -->
        <div
          v-for="i in Math.max(0, emptySlotCount)"
          :key="'empty-' + i"
          class="empty-slot"
        >
          Empty slot
        </div>
      </div>

      <div v-if="actionError" class="action-error">{{ actionError }}</div>

      <!-- Source key pages summary -->
      <div v-if="sourceKeyPageIds.size > 0" class="source-summary">
        <div class="source-summary-header">
          Source Key Pages ({{ sourceKeyPageIds.size }}/4)
        </div>
        <div
          v-for="sid in sourceKeyPageIds"
          :key="sid"
          class="source-summary-row"
        >
          <div class="source-summary-info">
            <span class="source-summary-name">{{ sourceNameMap.get(sid) ?? "Unknown" }}</span>
            <span class="source-summary-count">
              {{ sourcePassiveCounts.get(sid) ?? 0 }} passives
            </span>
          </div>
          <button
            class="remove-btn"
            :disabled="editBusy"
            @click="unequipSource(sid)"
          >
            ✕
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Layout mirrors KeyPageTab / DeckTab */
.pt-tab {
  display: flex;
  flex-direction: column;
  gap: var(--sp-3);
  height: 100%;
  overflow: hidden;
  min-height: 0;
}

.pt-col {
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

.chapter-pill.active {
  background: var(--gold);
  color: var(--gold-ink);
  border-color: var(--gold-bright);
}

@media (min-width: 700px) {
  .pt-tab {
    flex-direction: row;
    gap: var(--sp-3);
  }

  .pt-col--grid {
    flex: 1;
  }

  .pt-col--detail {
    flex: 0 0 35%;
    border-left: 1px solid var(--border);
    padding-left: var(--sp-3);
  }
}

@media (min-width: 1200px) {
  .pt-tab {
    gap: var(--sp-3);
    padding: var(--sp-4);
  }

  .pt-col--detail {
    flex: 0 0 30%;
    padding-left: var(--sp-4);
  }
}

/* ── Source grid ────────────────────────────────────────────────────────────── */

.source-grid {
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: var(--sp-1);
  flex: 1;
  min-height: 0;
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

/* ── Source tiles ───────────────────────────────────────────────────────────── */

.source-tile {
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
  font-size: var(--fs-sm);
  font-family: var(--font-display);
  transition: background var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out);
}

.source-tile:hover {
  background: var(--bg-card-2);
  border-color: var(--border-mid);
}

.source-tile--equipped {
  border-color: var(--gold-bright);
  background: var(--gold-glow);
}

.source-tile--ineligible {
  opacity: 0.4;
}

.source-tile-chevron {
  font-size: var(--fs-2xs);
  color: var(--text-3);
  flex-shrink: 0;
  width: 0.8em;
}

.source-tile-name {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.source-tile-status {
  flex-shrink: 0;
  font-size: var(--fs-3xs);
  color: var(--text-3);
  margin-left: var(--sp-2);
}

.source-tile-speed {
  flex-shrink: 0;
  color: var(--text-3);
  font-size: var(--fs-xs);
  margin-left: var(--sp-2);
}

.source-passives {
  padding: 0 var(--sp-2) var(--sp-1) var(--sp-3);
  margin-top: calc(-1 * var(--sp-1));
}

/* ── Right column ──────────────────────────────────────────────────────────── */

.cost-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--sp-2) var(--sp-3);
  border-radius: var(--radius-md);
  background: var(--bg-card);
  border: 1px solid var(--border);
  flex-shrink: 0;
}

.cost-label {
  font-size: var(--fs-xs);
  color: var(--text-3);
}

.cost-value {
  font-size: var(--fs-md);
  font-weight: bold;
  color: var(--gold-bright);
  font-family: var(--font-display);
}

.current-passives {
  display: flex;
  flex-direction: column;
  gap: var(--sp-1);
  overflow-y: auto;
  flex: 1;
  min-height: 0;
}

.current-passive--innate {
  opacity: 0.6;
}

.attributed-source {
  font-size: var(--fs-3xs);
  color: var(--text-3);
  padding-left: 0.4rem;
  margin-top: -0.1rem;
}

.empty-slot {
  border: 1px dashed var(--border);
  padding: var(--sp-2) var(--sp-3);
  border-radius: var(--radius-md);
  color: var(--text-3);
  font-size: var(--fs-xs);
  text-align: center;
}

.action-error {
  font-size: var(--fs-xs);
  color: var(--crimson-hi);
  flex-shrink: 0;
}

/* ── Action buttons ────────────────────────────────────────────────────────── */

.attribute-btn {
  background: transparent;
  border: 1px solid var(--gold);
  color: var(--gold);
  padding: 0.1rem 0.5rem;
  border-radius: var(--radius-sm);
  font-size: var(--fs-3xs);
  font-family: var(--font-display);
  cursor: pointer;
  transition: background var(--duration-fast) var(--ease-out),
    color var(--duration-fast) var(--ease-out);
}

.attribute-btn:not(:disabled):hover {
  background: var(--gold);
  color: var(--gold-ink);
}

.attribute-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.unique-label {
  font-size: var(--fs-3xs);
  color: var(--text-3);
}

.remove-btn {
  background: transparent;
  border: 1px solid var(--crimson-hi);
  color: var(--crimson-hi);
  padding: 0.05rem 0.35rem;
  border-radius: var(--radius-sm);
  font-size: var(--fs-3xs);
  cursor: pointer;
  flex-shrink: 0;
  transition: background var(--duration-fast) var(--ease-out),
    color var(--duration-fast) var(--ease-out);
}

.remove-btn:not(:disabled):hover {
  background: var(--crimson-hi);
  color: var(--bg-surface);
}

.remove-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.unequip-btn {
  background: transparent;
  border: 1px solid var(--crimson-hi);
  color: var(--crimson-hi);
  padding: 0.1rem 0.5rem;
  border-radius: var(--radius-sm);
  font-size: var(--fs-3xs);
  font-family: var(--font-display);
  cursor: pointer;
  transition: background var(--duration-fast) var(--ease-out),
    color var(--duration-fast) var(--ease-out);
}

.unequip-btn:not(:disabled):hover {
  background: var(--crimson-hi);
  color: var(--bg-surface);
}

.unequip-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.equip-source-btn {
  background: transparent;
  border: 1px solid var(--gold);
  color: var(--gold);
  padding: 0.1rem 0.5rem;
  border-radius: var(--radius-sm);
  font-size: var(--fs-3xs);
  font-family: var(--font-display);
  cursor: pointer;
  flex-shrink: 0;
  margin-left: var(--sp-2);
  transition: background var(--duration-fast) var(--ease-out),
    color var(--duration-fast) var(--ease-out);
}

.equip-source-btn:not(:disabled):hover {
  background: var(--gold);
  color: var(--gold-ink);
}

.equip-source-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

/* ── Source summary ────────────────────────────────────────────────────────── */

.source-summary {
  margin-top: auto;
  padding-top: var(--sp-3);
  border-top: 1px solid var(--border);
  flex-shrink: 0;
}

.source-summary-header {
  font-size: var(--fs-3xs);
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  margin-bottom: var(--sp-2);
}

.source-summary-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--sp-1) 0;
}

.source-summary-info {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.source-summary-name {
  font-size: var(--fs-xs);
  color: var(--text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.source-summary-count {
  font-size: var(--fs-3xs);
  color: var(--text-3);
}
</style>
