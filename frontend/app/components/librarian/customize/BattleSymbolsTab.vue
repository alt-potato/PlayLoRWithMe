<!--
  BattleSymbolsTab.vue

  Battle Symbols (gifts) sub-tab inside CustomizePanel.
  Displays a 3x3 grid of gift position slots. Clicking a slot opens an inline
  selection list below the grid where the player can equip or unequip a gift.
  Each equipped slot also shows a visibility toggle (eye / dash icon).

  Gift changes are sent immediately via `onSetGifts` rather than being staged
  in the parent draft, because gifts are independent of the appearance/dialogue
  payload.
-->
<script setup lang="ts">
import type { LibrarianEntry, GiftSlot, GiftOption, GiftStat, ActionResult, SetGiftsPayload } from "~/types/game";

const props = defineProps<{
  lib: LibrarianEntry;
  busy: boolean;
  onSetGifts: (slots: SetGiftsPayload) => Promise<ActionResult>;
}>();

// ── Position definitions ──────────────────────────────────────────────────────

/**
 * Grid order (row-major, 3x3) matching the in-game gift UI layout.
 * The GiftPosition enum: Eye=0, Nose=1, Cheek=2, Mouth=3, Ear=4,
 * HairAccessory=5, Hood=6, Mask=7, Helmet=8
 */
/** Gift position index — one value per grid cell, 0–8. */
type GiftIdx = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;

const POSITIONS: { name: string; idx: GiftIdx }[] = [
  { name: "Eye",            idx: 0 },
  { name: "Nose",           idx: 1 },
  { name: "Cheek",          idx: 2 },
  { name: "Mouth",          idx: 3 },
  { name: "Ear",            idx: 4 },
  { name: "HairAccessory",  idx: 5 },
  { name: "Hood",           idx: 6 },
  { name: "Mask",           idx: 7 },
  { name: "Helmet",         idx: 8 },
];

/** Display-friendly labels matching in-game text (ui_gift_* keys). */
const POSITION_LABELS: Record<string, string> = {
  Eye: "Eye",
  Nose: "Nose",
  Cheek: "Cheek",
  Mouth: "Mouth",
  Ear: "Ear",
  HairAccessory: "Headwear 1",
  Hood: "Headwear 2",
  Mask: "Headwear 3",
  Helmet: "Headwear 4",
};

/** Labels for GiftStat fields shown in summary and option rows. */
const STAT_LABELS: { key: keyof GiftStat; label: string }[] = [
  { key: "hp",           label: "HP" },
  { key: "breakGauge",   label: "Stagger" },
  { key: "breakRecover", label: "Stagger Resist" },
  { key: "tune",         label: "Speed" },
  { key: "amp",          label: "Emotion" },
];

// ── State ─────────────────────────────────────────────────────────────────────

/** Which position slot is currently open for selection (by name), or null. */
const openPosition = ref<string | null>(null);

/** Tracks in-flight gift operations to prevent double-sends. */
const actionBusy = ref(false);

const isBusy = computed(() => props.busy || actionBusy.value);

// ── Helpers ───────────────────────────────────────────────────────────────────

/** Returns the equipped GiftSlot for a position index, or null. */
function equippedAt(idx: GiftIdx): GiftSlot | null {
  return props.lib.gifts?.equipped?.[idx] ?? null;
}

/** Returns gifts available for a given position name. */
function availableFor(positionName: string): GiftOption[] {
  return props.lib.gifts?.available?.filter(g => g.position === positionName) ?? [];
}

/** Formats a stat value as "+N" or "-N" (never "0"). */
function fmtStat(n: number): string {
  return n >= 0 ? `+${n}` : `${n}`;
}

/** Returns non-zero stat bonuses for a GiftStat as label/value pairs. */
function statBonuses(stat: GiftStat): { label: string; value: string }[] {
  return STAT_LABELS
    .filter(({ key }) => stat[key] !== 0)
    .map(({ key, label }) => ({ label, value: fmtStat(stat[key]) }));
}

/**
 * Cumulative stat totals across all equipped gifts.
 * Only non-zero fields are included in the summary.
 */
const cumulativeStats = computed<{ label: string; value: string }[]>(() => {
  const totals: GiftStat = { hp: 0, breakGauge: 0, breakRecover: 0, tune: 0, amp: 0 };
  for (const slot of props.lib.gifts?.equipped ?? []) {
    if (!slot) continue;
    for (const key of Object.keys(totals) as (keyof GiftStat)[]) {
      totals[key] += slot.stat[key];
    }
  }
  return statBonuses(totals);
});

// ── Actions ───────────────────────────────────────────────────────────────────

function toggleOpen(positionName: string): void {
  openPosition.value = openPosition.value === positionName ? null : positionName;
}

async function equip(idx: GiftIdx, giftId: number): Promise<void> {
  if (isBusy.value) return;
  actionBusy.value = true;
  try {
    await props.onSetGifts({ [`gift${idx}`]: giftId });
    openPosition.value = null;
  } finally {
    actionBusy.value = false;
  }
}

async function unequip(idx: GiftIdx): Promise<void> {
  if (isBusy.value) return;
  actionBusy.value = true;
  try {
    await props.onSetGifts({ [`gift${idx}`]: -1 });
    openPosition.value = null;
  } finally {
    actionBusy.value = false;
  }
}

async function toggleVisibility(idx: GiftIdx, currentlyVisible: boolean): Promise<void> {
  if (isBusy.value) return;
  actionBusy.value = true;
  try {
    await props.onSetGifts({ [`vis${idx}`]: currentlyVisible ? 0 : 1 });
  } finally {
    actionBusy.value = false;
  }
}
</script>

<template>
  <div class="tab-inner">
    <!-- 3x3 gift grid -->
    <div class="gift-grid">
      <div
        v-for="pos in POSITIONS"
        :key="pos.name"
        class="gift-cell"
        :class="{
          equipped: !!equippedAt(pos.idx),
          open: openPosition === pos.name,
        }"
        @click="toggleOpen(pos.name)"
      >
        <div class="cell-label">{{ POSITION_LABELS[pos.name] }}</div>
        <template v-if="equippedAt(pos.idx)">
          <div class="cell-name">{{ equippedAt(pos.idx)!.name }}</div>
          <!-- Visibility toggle; stops propagation to avoid reopening the slot picker -->
          <button
            class="vis-btn"
            :title="equippedAt(pos.idx)!.visible ? 'Hide' : 'Show'"
            :disabled="isBusy"
            @click.stop="toggleVisibility(pos.idx, equippedAt(pos.idx)!.visible)"
          >
            {{ equippedAt(pos.idx)!.visible ? "👁" : "–" }}
          </button>
        </template>
        <div v-else class="cell-empty">—</div>
      </div>
    </div>

    <!-- Inline selection panel (shown below grid when a slot is open) -->
    <div v-if="openPosition" class="selection-panel">
      <div class="selection-header">
        {{ POSITION_LABELS[openPosition] }} Slot
      </div>

      <!-- Currently equipped gift details -->
      <template v-if="equippedAt(POSITIONS.find(p => p.name === openPosition)!.idx)">
        <div class="equipped-detail">
          <div class="gift-row-name">
            {{ equippedAt(POSITIONS.find(p => p.name === openPosition)!.idx)!.name }}
            <span class="equipped-badge">Equipped</span>
          </div>
          <div
            v-if="equippedAt(POSITIONS.find(p => p.name === openPosition)!.idx)!.desc"
            class="gift-desc"
          >
            {{ equippedAt(POSITIONS.find(p => p.name === openPosition)!.idx)!.desc }}
          </div>
          <div class="stat-row">
            <span
              v-for="bonus in statBonuses(equippedAt(POSITIONS.find(p => p.name === openPosition)!.idx)!.stat)"
              :key="bonus.label"
              class="stat-chip"
              :class="{ positive: bonus.value.startsWith('+'), negative: bonus.value.startsWith('-') }"
            >
              {{ bonus.label }} {{ bonus.value }}
            </span>
          </div>
          <button
            class="unequip-btn"
            :disabled="isBusy"
            @click="unequip(POSITIONS.find(p => p.name === openPosition)!.idx)"
          >
            Unequip
          </button>
        </div>
      </template>

      <!-- Available gifts for this position -->
      <div class="available-list">
        <div
          v-if="!availableFor(openPosition).length"
          class="no-available"
        >
          No available gifts for this slot.
        </div>
        <button
          v-for="gift in availableFor(openPosition)"
          :key="gift.id"
          class="gift-option"
          :disabled="isBusy"
          @click="equip(POSITIONS.find(p => p.name === openPosition)!.idx, gift.id)"
        >
          <img
            :src="`/assets/gifts/gift_${gift.id}.png`"
            class="gift-thumb"
            :alt="gift.name"
          />
          <div class="gift-option-info">
            <div class="gift-option-name">{{ gift.name }}</div>
            <div v-if="gift.desc" class="gift-desc">{{ gift.desc }}</div>
            <div class="stat-row">
              <span
                v-for="bonus in statBonuses(gift.stat)"
                :key="bonus.label"
                class="stat-chip"
                :class="{ positive: bonus.value.startsWith('+'), negative: bonus.value.startsWith('-') }"
              >
                {{ bonus.label }} {{ bonus.value }}
              </span>
            </div>
          </div>
        </button>
      </div>
    </div>

    <!-- Cumulative stat summary -->
    <div v-if="cumulativeStats.length" class="stat-summary">
      <div class="summary-title">Total Bonuses</div>
      <div class="summary-row">
        <span
          v-for="bonus in cumulativeStats"
          :key="bonus.label"
          class="stat-chip"
          :class="{ positive: bonus.value.startsWith('+'), negative: bonus.value.startsWith('-') }"
        >
          {{ bonus.label }} {{ bonus.value }}
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.tab-inner {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

/* ── Gift grid ─────────────────────────────────────────────────────────────── */

.gift-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 0.35rem;
}

.gift-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.15rem;
  padding: 0.4rem 0.3rem;
  border: 1px solid var(--border);
  border-radius: 5px;
  background: var(--bg-raised);
  cursor: pointer;
  min-height: 4rem;
  text-align: center;
  transition:
    border-color 0.12s,
    background 0.12s;
  position: relative;
}

.gift-cell:hover {
  border-color: var(--border-mid);
}

.gift-cell.equipped {
  border-color: var(--border-mid);
}

.gift-cell.open {
  border-color: var(--gold);
}

.cell-label {
  font-size: var(--fs-4xs);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-3);
}

.cell-name {
  font-size: var(--fs-4xs);
  color: var(--text-1);
  line-height: 1.3;
  word-break: break-word;
}

.cell-empty {
  font-size: var(--fs-3xs);
  color: var(--text-3);
}

.vis-btn {
  position: absolute;
  bottom: 0.2rem;
  right: 0.2rem;
  background: transparent;
  border: none;
  font-size: var(--fs-3xs);
  cursor: pointer;
  color: var(--text-3);
  padding: 0;
  line-height: 1;
  transition: color 0.1s;
}

.vis-btn:hover:not(:disabled) {
  color: var(--text-1);
}

.vis-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

/* ── Selection panel ───────────────────────────────────────────────────────── */

.selection-panel {
  border: 1px solid var(--border-mid);
  border-radius: 5px;
  background: var(--bg-raised);
  overflow: hidden;
}

.selection-header {
  padding: 0.3rem 0.6rem;
  font-size: var(--fs-4xs);
  font-family: var(--font-display);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--gold);
  border-bottom: 1px solid var(--border);
}

.equipped-detail {
  padding: 0.5rem 0.6rem;
  border-bottom: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.gift-row-name {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: var(--fs-3xs);
  color: var(--text-1);
  font-weight: 600;
}

.equipped-badge {
  font-size: var(--fs-4xs);
  color: var(--gold);
  border: 1px solid var(--gold);
  border-radius: 3px;
  padding: 0.05rem 0.25rem;
  font-weight: normal;
}

.gift-desc {
  font-size: var(--fs-4xs);
  color: var(--text-3);
  line-height: 1.4;
}

.stat-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.2rem;
}

.stat-chip {
  font-size: var(--fs-4xs);
  padding: 0.07rem 0.3rem;
  border-radius: 3px;
  border: 1px solid var(--border);
  color: var(--text-2);
}

.stat-chip.positive {
  color: var(--text-green);
  border-color: var(--text-green);
}

.stat-chip.negative {
  color: var(--crimson-hi);
  border-color: var(--crimson-hi);
}

.unequip-btn {
  align-self: flex-start;
  padding: 0.2rem 0.5rem;
  font-size: var(--fs-4xs);
  background: transparent;
  border: 1px solid var(--border-mid);
  border-radius: 3px;
  color: var(--text-2);
  cursor: pointer;
  transition:
    color 0.1s,
    border-color 0.1s;
}

.unequip-btn:hover:not(:disabled) {
  color: var(--crimson-hi);
  border-color: var(--crimson-hi);
}

.unequip-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.available-list {
  display: flex;
  flex-direction: column;
  max-height: 14rem;
  overflow-y: auto;
}

.no-available {
  padding: 0.5rem 0.6rem;
  font-size: var(--fs-3xs);
  color: var(--text-3);
  font-style: italic;
}

.gift-option {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
  padding: 0.4rem 0.6rem;
  background: transparent;
  border: none;
  border-bottom: 1px solid var(--border);
  cursor: pointer;
  text-align: left;
  transition: background 0.1s;
}

.gift-option:last-child {
  border-bottom: none;
}

.gift-option:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.04);
}

.gift-option:disabled {
  opacity: 0.4;
  cursor: default;
}

.gift-thumb {
  width: 2rem;
  height: 2rem;
  object-fit: contain;
  border-radius: 3px;
  flex-shrink: 0;
}

.gift-option-info {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 0;
}

.gift-option-name {
  font-size: var(--fs-3xs);
  color: var(--text-1);
  font-weight: 600;
  line-height: 1.2;
}

/* ── Cumulative stat summary ───────────────────────────────────────────────── */

.stat-summary {
  border: 1px solid var(--border);
  border-radius: 5px;
  background: var(--bg-raised);
  padding: 0.4rem 0.6rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.summary-title {
  font-size: var(--fs-4xs);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-3);
}

.summary-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.2rem;
}
</style>
