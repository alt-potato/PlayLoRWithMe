<!--
  PassivesTab.vue

  Passive attribution tab inside the librarian EditPanel. Uses a Save/Cancel
  model: all source-equip/unequip and passive attribute/remove actions are
  staged locally. Nothing hits the server until the user clicks Save Changes.
  Cancel discards staged edits. Because passives are per-key-page, a primary
  key-page change (server-side reset) re-initializes the staged view.

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
import { toggleSet } from "~/utils/setReactive";

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

// ── Staged state (Save/Cancel model) ────────────────────────────────────────

const stagedSourceIds = ref<Set<number>>(new Set());
const stagedAttributions = ref<AttributedPassive[]>([]);

function initStaged() {
  stagedSourceIds.value = new Set(props.lib.sourceKeyPageIds ?? []);
  stagedAttributions.value = [...(props.lib.attributedPassives ?? [])];
}

initStaged();

// Server resets passives when the primary key-page changes, so staged state
// must follow.
watch(() => props.lib.keyPage?.instanceId, () => { initStaged(); });

// UI reads staged values, not server values.
const sourceKeyPageIds = computed(() => stagedSourceIds.value);
const attributedPassives = computed(() => stagedAttributions.value);

const innatePassives = computed(() => {
  const all = props.lib.passives ?? [];
  const attr = props.lib.attributedPassives ?? [];
  if (!attr.length) return all;
  // Remove one matching passive per actually-attributed entry (handles duplicates).
  // Innate derivation uses the *actual* server-side attribution list, since the
  // server authoritatively decides which of the key-page's passive slots are
  // filled by attribution vs. innate.
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

const emptySlotCount = computed(
  () => passiveSlotCount.value - innatePassives.value.length - attributedPassives.value.length,
);

// ── Pending diff (staged vs server) ─────────────────────────────────────────

const actualSourceIds = computed(() => new Set(props.lib.sourceKeyPageIds ?? []));

function attrKey(sourceId: number, p: Passive): string {
  return `${sourceId}:${p.id.id}:${p.id.packageId}`;
}

const actualAttrKeys = computed(() => new Set(
  (props.lib.attributedPassives ?? []).map((ap) => attrKey(ap.sourceInstanceId, ap.passive)),
));

const pendingSourceAdds = computed(() => {
  const out = new Set<number>();
  for (const id of stagedSourceIds.value) if (!actualSourceIds.value.has(id)) out.add(id);
  return out;
});
const pendingSourceRemoves = computed(() => {
  const out = new Set<number>();
  for (const id of actualSourceIds.value) if (!stagedSourceIds.value.has(id)) out.add(id);
  return out;
});
const pendingAttrAdds = computed(() =>
  stagedAttributions.value.filter(
    (ap) => !actualAttrKeys.value.has(attrKey(ap.sourceInstanceId, ap.passive)),
  ),
);
const pendingAttrRemoves = computed(() => {
  const stagedKeys = new Set(
    stagedAttributions.value.map((ap) => attrKey(ap.sourceInstanceId, ap.passive)),
  );
  return (props.lib.attributedPassives ?? []).filter(
    (ap) => !stagedKeys.has(attrKey(ap.sourceInstanceId, ap.passive)),
  );
});

const isDirty = computed(
  () =>
    pendingSourceAdds.value.size > 0 ||
    pendingSourceRemoves.value.size > 0 ||
    pendingAttrAdds.value.length > 0 ||
    pendingAttrRemoves.value.length > 0,
);

function isPendingSourceAdd(kp: AvailableKeyPage): boolean {
  return pendingSourceAdds.value.has(kp.instanceId);
}
function isPendingAttrAdd(ap: AttributedPassive): boolean {
  return !actualAttrKeys.value.has(attrKey(ap.sourceInstanceId, ap.passive));
}

// Staged cost = server current minus the costs of pending removals plus the
// costs of pending additions. Server `currentPassiveCost` already reflects the
// actual attribution set; we adjust by the diff.
const stagedPassiveCost = computed(() => {
  let cost = props.lib.currentPassiveCost ?? 0;
  for (const ap of pendingAttrRemoves.value) cost -= ap.passive.cost ?? 0;
  for (const ap of pendingAttrAdds.value) cost += ap.passive.cost ?? 0;
  return cost;
});

// Duplicate prevention: a passive (by id+packageId) can appear at most once
// across innate + attributed. Check the staged view so users can re-attribute
// a passive they just pending-removed from a different source.
const stagedPassiveIds = computed(() => {
  const set = new Set<string>();
  for (const p of innatePassives.value) set.add(`${p.id.id}:${p.id.packageId}`);
  for (const ap of stagedAttributions.value)
    set.add(`${ap.passive.id.id}:${ap.passive.id.packageId}`);
  return set;
});

function hasDuplicate(p: Passive): boolean {
  return stagedPassiveIds.value.has(`${p.id.id}:${p.id.packageId}`);
}

// ── Chapter filter (reused from KeyPageTab) ─────────────────────────────────

const chapters = computed(() => {
  const set = new Set(availableKeyPages.value.map((kp) => kp.chapter));
  return ["All", ...Array.from(set).sort((a, b) => a - b).map(String)];
});

const chapterFilter = ref("All");

// ── Advanced search ─────────────────────────────────────────────────────────

const kpNameSearch = ref("");
const passiveSearch = ref("");
const costFilter = ref<Set<string>>(new Set());
const keywordFilter = ref<Set<string>>(new Set());
const showAdvanced = ref(false);

const COST_LABELS = ["1", "2", "3", "4", "5", "6+"];
const KEYWORD_LABELS = [
  "Power",
  "Strength",
  "Endurance",
  "Burn",
  "Paralysis",
  "Bleed",
  "Recovery",
  "Damage",
];

const toggleCost = (v: string) => toggleSet(costFilter, v);
const toggleKeyword = (v: string) => toggleSet(keywordFilter, v);

function matchesPassiveCost(p: Passive, selection: Set<string>): boolean {
  if (selection.size === 0) return true;
  const c = p.cost ?? 0;
  const label = c >= 6 ? "6+" : String(c);
  return selection.has(label);
}

function matchesKeyword(p: Passive, selection: Set<string>): boolean {
  if (selection.size === 0) return true;
  const text = `${p.name ?? ""} ${p.desc ?? ""}`.toLowerCase();
  for (const k of selection) if (text.includes(k.toLowerCase())) return true;
  return false;
}

const filteredPages = computed(() => {
  const ch = chapterFilter.value === "All" ? null : Number(chapterFilter.value);
  const nameSearch = kpNameSearch.value.trim().toLowerCase();
  const passiveSearchText = passiveSearch.value.trim().toLowerCase();
  const hasPassiveFilter =
    passiveSearchText !== "" ||
    costFilter.value.size > 0 ||
    keywordFilter.value.size > 0;

  return availableKeyPages.value.filter((kp) => {
    if (ch != null && kp.chapter !== ch) return false;
    if (nameSearch && !kp.name.toLowerCase().includes(nameSearch)) return false;
    if (hasPassiveFilter) {
      // Page matches iff at least one of its passives satisfies all active
      // passive-level filters (AND across filter types, within a single passive).
      return (kp.passives ?? []).some((p) => {
        if (passiveSearchText) {
          const text = `${p.name ?? ""} ${p.desc ?? ""}`.toLowerCase();
          if (!text.includes(passiveSearchText)) return false;
        }
        if (!matchesPassiveCost(p, costFilter.value)) return false;
        if (!matchesKeyword(p, keywordFilter.value)) return false;
        return true;
      });
    }
    return true;
  });
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

const toggleGroup = (bookIcon: string) => toggleSet(collapsedGroups, bookIcon);

// ── Expanded source key pages ───────────────────────────────────────────────

/** Set of instanceIds currently expanded in the left column. */
const expandedSources = ref(new Set<number>());

const toggleSourceExpansion = (kp: AvailableKeyPage) => toggleSet(expandedSources, kp.instanceId);

function isSource(kp: AvailableKeyPage): boolean {
  return sourceKeyPageIds.value.has(kp.instanceId);
}

function isExpanded(kp: AvailableKeyPage): boolean {
  return expandedSources.value.has(kp.instanceId);
}

/** Whether this key page is the current librarian's own primary key page. */
function isOwnPrimary(kp: AvailableKeyPage): boolean {
  return kp.instanceId === props.lib.keyPage?.instanceId;
}

/**
 * Whether a key page is ineligible (attributed to another librarian or
 * equipped as someone else's primary). Own pages — this librarian's primary
 * or one of its existing sources — are never ineligible from this perspective.
 */
function isIneligible(kp: AvailableKeyPage): boolean {
  if (isOwnPrimary(kp) || isSource(kp)) return false;
  return kp.canGivePassive === false;
}

// ── Source key page names for attribution display ───────────────────────────

const sourceNameMap = computed(() => {
  const map = new Map<number, string>();
  for (const kp of availableKeyPages.value) {
    map.set(kp.instanceId, kp.name);
  }
  // Ensure a name exists for pending-remove sources that may have dropped
  // out of availableKeyPages after being queued for unequip.
  for (const ap of props.lib.attributedPassives ?? []) {
    if (ap.sourceName && !map.has(ap.sourceInstanceId))
      map.set(ap.sourceInstanceId, ap.sourceName);
  }
  return map;
});

/** Count of passives attributed from each source (staged view). */
const sourcePassiveCounts = computed(() => {
  const map = new Map<number, number>();
  for (const ap of attributedPassives.value) {
    map.set(ap.sourceInstanceId, (map.get(ap.sourceInstanceId) ?? 0) + 1);
  }
  return map;
});

/** Source summary rows: staged sources first, then pending-remove sources. */
interface SourceSummaryRow {
  id: number;
  pendingRemove: boolean;
}
const sourceSummaryRows = computed((): SourceSummaryRow[] => {
  const rows: SourceSummaryRow[] = [];
  for (const id of sourceKeyPageIds.value) rows.push({ id, pendingRemove: false });
  for (const id of pendingSourceRemoves.value) rows.push({ id, pendingRemove: true });
  return rows;
});

/** Undo a pending source removal by re-adding it to the staged set. */
function undoUnequipSource(instanceId: number) {
  const next = new Set(stagedSourceIds.value);
  next.add(instanceId);
  stagedSourceIds.value = next;
}

// ── Local stagers ───────────────────────────────────────────────────────────

const actionError = ref<string | null>(null);
const saveBusy = ref(false);
const localBusy = computed(() => props.editBusy || saveBusy.value);

function equipSource(kp: AvailableKeyPage) {
  const next = new Set(stagedSourceIds.value);
  next.add(kp.instanceId);
  stagedSourceIds.value = next;
  const exp = new Set(expandedSources.value);
  exp.add(kp.instanceId);
  expandedSources.value = exp;
}

function unequipSource(instanceId: number) {
  const next = new Set(stagedSourceIds.value);
  next.delete(instanceId);
  stagedSourceIds.value = next;
  // Cascade: drop any staged attributions from this source.
  stagedAttributions.value = stagedAttributions.value.filter(
    (ap) => ap.sourceInstanceId !== instanceId,
  );
  const exp = new Set(expandedSources.value);
  exp.delete(instanceId);
  expandedSources.value = exp;
}

function attributePassive(sourceInstanceId: number, p: Passive) {
  const sourceName = availableKeyPages.value.find(
    (kp) => kp.instanceId === sourceInstanceId,
  )?.name;
  stagedAttributions.value = [
    ...stagedAttributions.value,
    { sourceInstanceId, passive: p, sourceName },
  ];
}

function removeAttributed(ap: AttributedPassive) {
  const key = attrKey(ap.sourceInstanceId, ap.passive);
  const idx = stagedAttributions.value.findIndex(
    (x) => attrKey(x.sourceInstanceId, x.passive) === key,
  );
  if (idx >= 0) {
    const next = [...stagedAttributions.value];
    next.splice(idx, 1);
    stagedAttributions.value = next;
  }
}

/** Restore a pending-remove attribution to the staged set. */
function undoRemoveAttributed(ap: AttributedPassive) {
  stagedAttributions.value = [...stagedAttributions.value, ap];
}

// ── Save / Cancel ───────────────────────────────────────────────────────────

async function saveChanges() {
  if (!isDirty.value) return;
  saveBusy.value = true;
  actionError.value = null;
  try {
    // Order matters: drop attributions before unequipping their source, and
    // equip new sources before attributing from them.
    for (const ap of pendingAttrRemoves.value) {
      await props.onRemoveAttributedPassive(
        ap.sourceInstanceId,
        ap.passive.id.id,
        // EntryId.packageId is numeric on the wire, but the C# handler reads
        // passivePackageId as a string (JsonReader stringifies all scalars
        // anyway, so this just makes the contract explicit).
        String(ap.passive.id.packageId),
      );
    }
    for (const id of pendingSourceRemoves.value) {
      await props.onUnequipSourceBook(id);
    }
    for (const id of pendingSourceAdds.value) {
      await props.onEquipSourceBook(id);
    }
    for (const ap of pendingAttrAdds.value) {
      await props.onAttributePassive(
        ap.sourceInstanceId,
        ap.passive.id.id,
        String(ap.passive.id.packageId),
      );
    }
  } catch (e) {
    actionError.value = String(e);
  } finally {
    saveBusy.value = false;
  }
  // Re-sync staged state to whatever the server actually accepted.
  await nextTick();
  initStaged();
}

function cancelChanges() {
  initStaged();
  actionError.value = null;
}

/** Whether the cost cap would be exceeded by attributing a passive with this cost. */
function wouldExceedCost(passiveCost: number): boolean {
  return stagedPassiveCost.value + passiveCost > maxPassiveCost.value;
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

      <button class="pt-advanced-toggle" @click="showAdvanced = !showAdvanced">
        {{ showAdvanced ? "▾" : "▸" }} Advanced
      </button>

      <template v-if="showAdvanced">
        <input
          v-model="kpNameSearch"
          class="pt-search-input"
          placeholder="Search key pages..."
          type="search"
        />
        <input
          v-model="passiveSearch"
          class="pt-search-input"
          placeholder="Search passive abilities..."
          type="search"
        />

        <div class="pt-filter-section-label">Cost</div>
        <div class="pt-filter-pills">
          <button
            v-for="label in COST_LABELS"
            :key="label"
            class="pt-filter-pill"
            :class="{ active: costFilter.has(label) }"
            @click="toggleCost(label)"
          >
            {{ label }}
          </button>
        </div>

        <div class="pt-filter-section-label">Keyword</div>
        <div class="pt-filter-pills">
          <button
            v-for="kw in KEYWORD_LABELS"
            :key="kw"
            class="pt-filter-pill"
            :class="{ active: keywordFilter.has(kw) }"
            @click="toggleKeyword(kw)"
          >
            {{ kw }}
          </button>
        </div>
      </template>

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
                  'source-tile--own': isOwnPrimary(kp),
                  'source-tile--ineligible': isIneligible(kp),
                  'source-tile--expanded': isExpanded(kp),
                  'source-tile--pending-add': isPendingSourceAdd(kp),
                }"
                :style="rarityBorderStyle(kp.rarity)"
                @click="toggleSourceExpansion(kp)"
              >
                <span class="source-tile-chevron">{{ isExpanded(kp) ? "▾" : "▸" }}</span>
                <span class="source-tile-name">{{ kp.name }}</span>
                <span v-if="isOwnPrimary(kp)" class="source-tile-status">
                  Equipped
                </span>
                <span v-else-if="isIneligible(kp)" class="source-tile-status">
                  In use by {{ kp.passiveGivenTo }}
                </span>
                <button
                  v-if="isSource(kp)"
                  class="unequip-btn unequip-btn--inline"
                  :disabled="localBusy"
                  @click.stop="unequipSource(kp.instanceId)"
                >
                  Unequip
                </button>
                <button
                  v-else-if="!isIneligible(kp) && !isOwnPrimary(kp)"
                  class="equip-source-btn"
                  :disabled="localBusy"
                  @click.stop="equipSource(kp)"
                >
                  Equip
                </button>
              </div>
              <div v-if="isExpanded(kp)" class="source-passives">
                <UnitPassiveList :passives="kp.passives">
                  <template v-if="isSource(kp)" #action="{ passive }">
                    <button
                      v-if="passive.canTransfer !== false && !hasDuplicate(passive)"
                      class="attribute-btn"
                      :disabled="localBusy || wouldExceedCost(passive.cost ?? 0) || !hasEmptySlots()"
                      @click.stop="attributePassive(kp.instanceId, passive)"
                    >
                      Attribute
                    </button>
                    <span v-else-if="hasDuplicate(passive)" class="unique-label">Duplicate</span>
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

      <!-- Primary key page (passives are per-key-page; compact omits stats/resistances) -->
      <LibrarianKeyPageDetail
        v-if="lib.keyPage"
        class="primary-keypage"
        :key-page="lib.keyPage"
        :compact="true"
      />

      <!-- Cost bar (reflects staged state) -->
      <div class="cost-bar">
        <span class="cost-label">Passive Cost</span>
        <span class="cost-value">{{ stagedPassiveCost }} / {{ maxPassiveCost }}</span>
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

        <!-- Staged attributed passives (includes pending-add) -->
        <div
          v-for="ap in attributedPassives"
          :key="'attr-' + ap.passive.id.id + ap.sourceInstanceId"
          class="current-passive current-passive--attributed"
          :class="{ 'current-passive--pending-add': isPendingAttrAdd(ap) }"
        >
          <UnitPassiveList :passives="[ap.passive]">
            <template #action="{ passive: _ }">
              <button
                class="remove-btn"
                :disabled="localBusy"
                @click="removeAttributed(ap)"
              >
                ✕
              </button>
            </template>
          </UnitPassiveList>
          <div class="attributed-source">from: {{ ap.sourceName ?? "Unknown" }}</div>
        </div>

        <!-- Pending-remove attributed passives (struck; click ↶ to undo) -->
        <div
          v-for="ap in pendingAttrRemoves"
          :key="'rm-' + ap.passive.id.id + ap.sourceInstanceId"
          class="current-passive current-passive--attributed current-passive--pending-remove"
        >
          <UnitPassiveList :passives="[ap.passive]">
            <template #action="{ passive: _ }">
              <button
                class="remove-btn"
                :disabled="localBusy"
                title="Undo remove"
                @click="undoRemoveAttributed(ap)"
              >
                ↶
              </button>
            </template>
          </UnitPassiveList>
          <div class="attributed-source">from: {{ ap.sourceName ?? "Unknown" }}</div>
        </div>

        <!-- Empty slots — single `-` placeholder per slot -->
        <div
          v-for="i in Math.max(0, emptySlotCount)"
          :key="'empty-' + i"
          class="empty-slot"
          aria-label="Empty slot"
        >
          -
        </div>
      </div>

      <div v-if="actionError" class="action-error">{{ actionError }}</div>

      <!-- Source key pages summary — includes pending-remove rows (struck) -->
      <div v-if="sourceSummaryRows.length > 0" class="source-summary">
        <div class="source-summary-header">
          Source Key Pages ({{ sourceKeyPageIds.size }}/4)
        </div>
        <div
          v-for="row in sourceSummaryRows"
          :key="(row.pendingRemove ? 'rm-' : 'kp-') + row.id"
          class="source-summary-row"
          :class="{ 'source-summary-row--pending-remove': row.pendingRemove }"
        >
          <div class="source-summary-info">
            <span class="source-summary-name">{{ sourceNameMap.get(row.id) ?? "Unknown" }}</span>
            <span class="source-summary-count">
              {{ sourcePassiveCounts.get(row.id) ?? 0 }} passives
            </span>
          </div>
          <button
            v-if="row.pendingRemove"
            class="remove-btn"
            :disabled="localBusy"
            title="Undo unequip"
            @click="undoUnequipSource(row.id)"
          >
            ↶
          </button>
          <button
            v-else
            class="remove-btn"
            :disabled="localBusy"
            @click="unequipSource(row.id)"
          >
            ✕
          </button>
        </div>
      </div>

      <!-- Save / Cancel bar — visible when staged state differs from server -->
      <div v-if="isDirty" class="save-cancel-bar">
        <button class="cancel-btn" :disabled="saveBusy" @click="cancelChanges">Cancel</button>
        <button class="save-btn" :disabled="saveBusy || editBusy" @click="saveChanges">
          {{ saveBusy ? "Saving…" : "Save Changes" }}
        </button>
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

/* ── Advanced search ──────────────────────────────────────────────────────── */

.pt-search-input {
  padding: var(--sp-2) var(--sp-3);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-mid);
  background: var(--bg-card-2);
  color: var(--text-1);
  font-size: var(--fs-sm);
  width: 100%;
  box-sizing: border-box;
  flex-shrink: 0;
  transition: border-color var(--duration-fast) var(--ease-out),
    box-shadow var(--duration-fast) var(--ease-out);
}

.pt-search-input::placeholder {
  color: var(--text-3);
}

.pt-search-input:focus {
  outline: none;
  border-color: var(--gold-dim);
  box-shadow: var(--shadow-gold);
}

.pt-advanced-toggle {
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
  flex-shrink: 0;
}

.pt-advanced-toggle:hover {
  color: var(--text-2);
}

.pt-filter-section-label {
  font-size: var(--fs-xs);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-2);
  font-family: var(--font-display);
  margin-top: var(--sp-1);
  flex-shrink: 0;
}

.pt-filter-pills {
  display: flex;
  flex-wrap: wrap;
  gap: var(--sp-1);
  flex-shrink: 0;
}

.pt-filter-pill {
  font-size: var(--fs-xs);
  font-family: var(--font-display);
  padding: var(--sp-1) var(--sp-3);
  border-radius: var(--radius-pill);
  border: 1px solid var(--border-mid);
  background: transparent;
  color: var(--text-2);
  cursor: pointer;
  white-space: nowrap;
  transition: background var(--duration-fast) var(--ease-out),
    color var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out);
}

.pt-filter-pill:hover {
  color: var(--text-1);
  border-color: var(--border-hi);
}

.pt-filter-pill.active {
  background: var(--gold-ink);
  color: var(--gold-bright);
  border-color: var(--gold-dim);
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
  scrollbar-gutter: stable;
  padding-right: var(--sp-1);
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
  /* --rarity-border is set via :style when the key page has a rarity field; */
  /* falls back to the default border colour for combat-context payloads. */
  border: 1px solid var(--rarity-border, var(--border));
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

.source-tile--own {
  border-color: var(--border-mid);
  background: var(--bg-card-2);
}

.source-tile--ineligible {
  opacity: 0.4;
}

.source-tile--pending-add {
  border-style: dashed;
  border-color: var(--gold);
  background: var(--gold-glow);
}

.source-tile-chevron {
  font-size: var(--fs-2xs);
  color: var(--text-2);
  flex-shrink: 0;
  width: 0.8em;
  margin-right: var(--sp-2);
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
  color: var(--text-2);
  margin-left: var(--sp-2);
}

.source-passives {
  padding: 0 var(--sp-2) var(--sp-1) var(--sp-3);
  margin-top: calc(-1 * var(--sp-1));
}

/* ── Right column ──────────────────────────────────────────────────────────── */

.primary-keypage {
  flex-shrink: 0;
  padding-top: 0;
  padding-bottom: var(--sp-1);
}

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
  scrollbar-gutter: stable;
  padding-right: var(--sp-1);
  flex: 1;
  min-height: 0;
}

.current-passive--innate {
  opacity: 0.6;
}

.current-passive--pending-add :deep(.passive-entry) {
  border-left-style: dashed;
  border-left-color: var(--gold);
}

.current-passive--pending-remove {
  opacity: 0.55;
}

.current-passive--pending-remove :deep(.passive-name),
.current-passive--pending-remove .attributed-source {
  text-decoration: line-through;
}

.attributed-source {
  font-size: var(--fs-3xs);
  color: var(--text-3);
  padding-left: 0.4rem;
  margin-top: -0.1rem;
}

.empty-slot {
  color: var(--text-3);
  font-size: var(--fs-xs);
  text-align: center;
  padding: 0 var(--sp-2);
  line-height: 1;
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

.source-summary-row--pending-remove {
  opacity: 0.55;
}

.source-summary-row--pending-remove .source-summary-name,
.source-summary-row--pending-remove .source-summary-count {
  text-decoration: line-through;
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

/* ── Save / Cancel bar ─────────────────────────────────────────────────────── */

.save-cancel-bar {
  display: flex;
  gap: var(--sp-2);
  justify-content: flex-end;
  padding-top: var(--sp-3);
  margin-top: var(--sp-2);
  border-top: 1px solid var(--border);
  flex-shrink: 0;
}

.save-btn {
  padding: var(--sp-2) var(--sp-4);
  border-radius: var(--radius-md);
  border: 1px solid var(--gold);
  background: var(--gold);
  color: var(--gold-ink);
  cursor: pointer;
  font-family: var(--font-display);
  font-size: var(--fs-sm);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  transition: background var(--duration-fast) var(--ease-out);
}

.save-btn:not(:disabled):hover {
  background: var(--gold-bright);
}

.save-btn:disabled {
  opacity: 0.5;
  cursor: default;
}

.cancel-btn {
  padding: var(--sp-2) var(--sp-4);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-mid);
  background: transparent;
  color: var(--text-2);
  cursor: pointer;
  font-family: var(--font-display);
  font-size: var(--fs-sm);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  transition: color var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out);
}

.cancel-btn:not(:disabled):hover {
  color: var(--text-1);
  border-color: var(--text-2);
}

.cancel-btn:disabled {
  opacity: 0.5;
  cursor: default;
}
</style>
