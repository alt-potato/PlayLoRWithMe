<!--
  ProjectionTab.vue

  Projection (body appearance) sub-tab inside CustomizePanel.
  Controls body type (appearanceType), body height, and appearance projection
  from another key page.

  Body type is a gender variant toggle (F = female, M = male) that selects
  different body prefabs in-game.  Only enabled when the active skin (fashion
  book or equipped key page) has SkinGender != "N".

  Fashion skins from other key pages are filtered by range-type compatibility:
    Melee librarians  -> Melee or Hybrid fashion books
    Range librarians  -> Range or Hybrid fashion books
    Hybrid librarians -> Hybrid fashion books only
  This mirrors the EquipCustomCoreBook validation in UnitDataModel.
-->
<script setup lang="ts">
import type { FashionBook, WorkshopSkin } from "~/types/game";

const HEIGHT_MIN = 140;
const HEIGHT_MAX = 220;

const props = defineProps<{
  appearanceType: string;
  height: number;
  customBookId: number;
  /** Non-empty when the active projection is a workshop book. */
  customBookPackageId: string;
  /** contentFolderIdx of the active workshop skin; empty string when none. */
  workshopSkin: string;
  fashionBooks: FashionBook[];
  /** Workshop cloth-overlay skins from CustomizingResourceLoader. */
  workshopSkins: WorkshopSkin[];
  libRangeType: string;
  /** SkinGender of the active skin source (from librarian entry). */
  skinGender: string | undefined;
  busy: boolean;
}>();

const emit = defineEmits<{
  "update:appearanceType": [value: string];
  "update:height": [value: number];
  "update:customBookId": [value: number];
  "update:customBookPackageId": [value: string];
  "update:workshopSkin": [value: string];
}>();

function onHeightInput(e: Event): void {
  const raw = Number((e.target as HTMLInputElement).value);
  if (!isNaN(raw)) {
    emit("update:height", Math.min(HEIGHT_MAX, Math.max(HEIGHT_MIN, raw)));
  }
}

/**
 * Whether a fashion book is compatible with this librarian's range type.
 * Mirrors UnitDataModel.EquipCustomCoreBook range-type validation.
 */
function isCompatible(book: FashionBook): boolean {
  const lib = props.libRangeType;
  const bk = book.rangeType;
  if (lib === "Melee") return bk === "Melee" || bk === "Hybrid";
  if (lib === "Range") return bk === "Range" || bk === "Hybrid";
  if (lib === "Hybrid") return bk === "Hybrid";
  return true; // unknown range type — allow all
}

const compatibleCorebooks = computed(() =>
  props.fashionBooks.filter(isCompatible),
);

/** Controls which subset is shown in the X Page Fashion list. */
const fashionTab = ref<"core" | "workshop">("core");

/** Whether the body type toggle is available for the active skin. */
const hasBodyTypeToggle = computed(
  () => props.skinGender === "F" || props.skinGender === "M",
);
</script>

<template>
  <div class="tab-inner">
    <!-- Body type -->
    <div class="section-label">Body Type</div>
    <div class="type-row">
      <button
        class="type-btn"
        :class="{ active: appearanceType === 'F' }"
        :disabled="busy || !hasBodyTypeToggle"
        @click="emit('update:appearanceType', 'F')"
      >
        Type A
      </button>
      <button
        class="type-btn"
        :class="{ active: appearanceType === 'M' }"
        :disabled="busy || !hasBodyTypeToggle"
        @click="emit('update:appearanceType', 'M')"
      >
        Type B
      </button>
    </div>

    <!-- Body size -->
    <div class="section-label" style="margin-top: 0.75rem">
      Body Size
      <span class="range-hint">({{ HEIGHT_MIN }}–{{ HEIGHT_MAX }})</span>
    </div>
    <div class="height-row">
      <input
        :value="height"
        type="number"
        class="height-input"
        :min="HEIGHT_MIN"
        :max="HEIGHT_MAX"
        :disabled="busy"
        @input="onHeightInput"
      />
      <span class="height-unit">cm</span>
    </div>

    <!-- X Page Fashion -->
    <div class="section-label" style="margin-top: 0.75rem">
      X Page Fashion
      <button
        class="unequip-btn"
        :class="{ active: customBookId < 0 && workshopSkin === '' }"
        :disabled="busy"
        @click="emit('update:customBookId', -1); emit('update:customBookPackageId', ''); emit('update:workshopSkin', '')"
      >Unequip</button>
    </div>

    <!-- Toggle between core fashion books and workshop skins -->
    <div class="fashion-toggle">
      <button
        class="ftoggle-btn"
        :class="{ active: fashionTab === 'core' }"
        @click="fashionTab = 'core'"
      >
        Fashion
      </button>
      <button
        class="ftoggle-btn"
        :class="{ active: fashionTab === 'workshop' }"
        @click="fashionTab = 'workshop'"
      >
        Workshop
      </button>
    </div>

    <!-- Core fashion tab -->
    <template v-if="fashionTab === 'core'">
      <div v-if="compatibleCorebooks.length === 0" class="empty-hint">
        No compatible fashion skins unlocked.
      </div>
      <div v-else class="fashion-list projection-list">
        <button
          v-for="book in compatibleCorebooks"
          :key="book.id"
          class="fashion-item"
          :class="{ active: customBookId === book.id && customBookPackageId === '' }"
          :disabled="busy"
          @click="emit('update:customBookId', book.id); emit('update:customBookPackageId', ''); emit('update:workshopSkin', '')"
        >
          <span class="fashion-name">{{ book.name }}</span>
          <span
            v-if="book.replacesHead"
            class="replaces-head-badge"
            title="This skin replaces the face and hair"
          >
            full
          </span>
        </button>
      </div>
    </template>

    <!-- Workshop skin tab -->
    <template v-else>
      <div v-if="workshopSkins.length === 0" class="empty-hint">
        No workshop skins installed.
      </div>
      <div v-else class="fashion-list projection-list">
        <button
          v-for="skin in workshopSkins"
          :key="skin.contentFolderIdx"
          class="fashion-item"
          :class="{ active: workshopSkin === skin.contentFolderIdx }"
          :disabled="busy"
          @click="emit('update:workshopSkin', skin.contentFolderIdx); emit('update:customBookId', -1); emit('update:customBookPackageId', '')"
        >
          <span class="fashion-name">{{ skin.name }}</span>
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.tab-inner {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.section-label {
  font-size: 0.6rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-3);
}

.range-hint {
  font-size: 0.55rem;
  color: var(--text-3);
  text-transform: none;
  letter-spacing: 0;
}

.type-row {
  display: flex;
  gap: 0.4rem;
}

.type-btn {
  padding: 0.35rem 0.8rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: var(--bg-card-2);
  color: var(--text-2);
  font-size: 0.75rem;
  cursor: pointer;
  transition:
    color 0.1s,
    border-color 0.1s,
    background 0.1s;
}

.type-btn:hover:not(:disabled) {
  border-color: var(--gold-dim);
  color: var(--gold);
}

.type-btn.active {
  border-color: var(--gold);
  color: var(--gold);
  background: rgba(201, 162, 39, 0.08);
}

.type-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.height-row {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.height-input {
  width: 5rem;
  padding: 0.35rem 0.5rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: var(--bg);
  color: var(--text-1);
  font-size: 0.8rem;
  text-align: right;
}

.height-unit {
  font-size: 0.7rem;
  color: var(--text-2);
}

.empty-hint {
  font-size: 0.7rem;
  color: var(--text-3);
  font-style: italic;
  padding: 0.25rem 0;
}

.unequip-btn {
  margin-left: 0.5rem;
  padding: 0.1rem 0.4rem;
  border-radius: 3px;
  border: 1px solid var(--border-mid);
  background: transparent;
  color: var(--text-3);
  font-size: 0.55rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  cursor: pointer;
  transition: color 0.1s, border-color 0.1s;
  vertical-align: middle;
}

.unequip-btn:hover:not(:disabled) {
  border-color: var(--gold-dim);
  color: var(--text-2);
}

.unequip-btn.active {
  border-color: var(--gold);
  color: var(--gold);
}

.unequip-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.fashion-toggle {
  display: flex;
  gap: 0.25rem;
}

.ftoggle-btn {
  flex: 1;
  padding: 0.25rem 0;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: var(--bg-card-2);
  color: var(--text-3);
  font-size: 0.65rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  cursor: pointer;
  transition: color 0.1s, border-color 0.1s, background 0.1s;
}

.ftoggle-btn:hover {
  border-color: var(--gold-dim);
  color: var(--text-2);
}

.ftoggle-btn.active {
  border-color: var(--gold);
  color: var(--gold);
  background: rgba(201, 162, 39, 0.08);
}

.fashion-list {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  max-height: 180px;
  overflow-y: auto;
}

/* Extra top spacing separates the list from the Fashion/Workshop toggle. */
.projection-list {
  margin-top: 0.2rem;
  padding-top: 0.35rem;
  border-top: 1px solid var(--border);
}

.fashion-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.3rem 0.6rem;
  border-radius: 4px;
  border: 1px solid var(--border);
  background: var(--bg-card-2);
  color: var(--text-2);
  font-size: 0.72rem;
  cursor: pointer;
  text-align: left;
  transition:
    color 0.1s,
    border-color 0.1s,
    background 0.1s;
}

.fashion-item:hover:not(:disabled) {
  border-color: var(--gold-dim);
  color: var(--gold);
}

.fashion-item.active {
  border-color: var(--gold);
  color: var(--gold);
  background: rgba(201, 162, 39, 0.08);
}

.fashion-item:disabled {
  opacity: 0.4;
  cursor: default;
}

.fashion-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* Badge indicating the skin replaces the head model (skinType != "Lor"). */
.replaces-head-badge {
  font-size: 0.55rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-3);
  border: 1px solid var(--border);
  border-radius: 2px;
  padding: 0.05rem 0.25rem;
  margin-left: 0.4rem;
  flex-shrink: 0;
}
</style>
