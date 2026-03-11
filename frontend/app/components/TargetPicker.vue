<!--
  TargetPicker.vue

  Mobile-friendly bottom-sheet overlay for picking an enemy speed die as a
  card target. Appears after a non-Instance card slot has been chosen.

  Props:
    selecting – { unitId, cardIndex, diceSlot, cardName, cardRange }
    enemies   – enemy unit array from game state

  Emits:
    pick(unitId, diceSlot)  – player tapped a valid die
    cancel                  – player tapped backdrop or ✕ button
-->
<script setup lang="ts">
// isMassRange is auto-imported from useBattleDisplay.ts
import type { Unit } from "~/types/game";

const props = defineProps<{
  selecting: {
    unitId: number;
    cardIndex: number;
    diceSlot: number;
    cardName: string;
    cardRange: string;
  };
  enemies: Unit[];
}>();

const emit = defineEmits<{
  pick: [unitId: number, diceSlot: number];
  cancel: [];
}>();

function pick(unitId: number, diceSlot: number) {
  emit("pick", unitId, diceSlot);
}

/** Returns the current state of an enemy die slot based on its slotted card. */
function dieState(enemy: Unit, dieSlot: number): "clash" | "incoming" | "empty" {
  const sc = (enemy.slottedCards ?? []).find((sc) => sc.slot === dieSlot);
  if (!sc) return "empty";
  return sc.clash ? "clash" : "incoming";
}
</script>

<template>
  <!-- Backdrop -->
  <div class="backdrop" @click="emit('cancel')" />

  <!-- Sheet -->
  <div class="sheet">
    <!-- Header -->
    <div class="sheet-header">
      <span class="card-label">{{ selecting.cardName }}</span>
      <span v-if="isMassRange(selecting.cardRange)" class="mass-badge"
        >MASS</span
      >
      <span class="slot-label"
        >Slot {{ selecting.diceSlot }} &rarr; target</span
      >
      <button class="close-btn" @click="emit('cancel')">✕</button>
    </div>

    <!-- Enemy list -->
    <div class="enemy-list">
      <div
        v-for="unit in enemies"
        :key="unit.id"
        class="enemy-section"
        :class="{ untargetable: !unit.targetable }"
      >
        <div class="enemy-name">
          <span>{{ unit.name ?? unit.keyPage?.name ?? `#${unit.id}` }}</span>
          <span v-if="!unit.targetable" class="untargetable-label"
            >⚠ untargetable</span
          >
        </div>
        <div class="die-row">
          <button
            v-for="d in unit.speedDice"
            :key="d.slot"
            class="die-hex-outer"
            :class="{
              staggered: d.staggered,
              'die-clash': !d.staggered && dieState(unit, d.slot) === 'clash',
              'die-incoming':
                !d.staggered && dieState(unit, d.slot) === 'incoming',
            }"
            :disabled="!unit.targetable"
            :title="
              d.staggered
                ? `Target slot ${d.slot} (broken)`
                : `Target slot ${d.slot}`
            "
            @click="pick(unit.id, d.slot)"
          >
            <span class="die-hex-inner">{{ d.staggered ? "✕" : d.value }}</span>
          </button>
        </div>
      </div>

      <div v-if="!enemies.length" class="no-enemies">No enemies</div>
    </div>
  </div>
</template>

<style scoped>
.backdrop {
  position: fixed;
  inset: 0;
  background: rgba(2, 3, 12, 0.7);
  z-index: 100;
}

.sheet {
  position: fixed;
  left: 0;
  right: 0;
  bottom: 0;
  max-height: 72vh;
  overflow-y: auto;
  background: var(--bg-surface);
  border-top: 2px solid var(--gold-dim);
  border-radius: 10px 10px 0 0;
  z-index: 101;
  padding-bottom: env(safe-area-inset-bottom, 0);
}

/* ── Sheet header ────────────────────────────────────────────────────────── */
.sheet-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--border-mid);
  position: sticky;
  top: 0;
  background: var(--bg-surface);
}
.card-label {
  font-family: var(--font-display);
  font-weight: 600;
  color: var(--text-1);
  font-size: 0.88rem;
  letter-spacing: 0.04em;
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.mass-badge {
  font-family: var(--font-display);
  font-size: 0.58rem;
  padding: 0.1rem 0.4rem;
  background: #2a0e00;
  border: 1px solid #8b3500;
  color: #ff7043;
  white-space: nowrap;
  font-weight: 700;
  letter-spacing: 0.08em;
}
.slot-label {
  font-size: 0.7rem;
  color: var(--text-2);
  white-space: nowrap;
  font-family: var(--font-body);
}
.close-btn {
  background: transparent;
  border: none;
  color: var(--text-2);
  font-size: 1rem;
  cursor: pointer;
  padding: 0 0.2rem;
  line-height: 1;
  flex-shrink: 0;
}
.close-btn:hover {
  color: var(--crimson-hi);
}

/* ── Enemy list ──────────────────────────────────────────────────────────── */
.enemy-list {
  display: flex;
  flex-direction: column;
}

.enemy-section {
  padding: 0.85rem 1rem;
  border-bottom: 1px solid var(--border);
  transition: opacity 0.15s;
}
.enemy-section.untargetable {
  opacity: 0.4;
  pointer-events: none;
}

.enemy-name {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-family: var(--font-display);
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: 0.04em;
  margin-bottom: 0.65rem;
}
.untargetable-label {
  font-size: 0.62rem;
  color: var(--orange);
  font-weight: normal;
  font-family: var(--font-body);
}

.die-row {
  display: flex;
  gap: 0.6rem;
  flex-wrap: wrap;
}

/* ── Hexagonal die buttons ───────────────────────────────────────────────── */

/* Outer hex = border + hover target */
.die-hex-outer {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 4.6rem;
  height: 4rem;
  clip-path: var(--hex);
  background: var(--border-mid);
  border: none;
  cursor: pointer;
  transition: background 0.12s;
  padding: 0;
}
.die-hex-outer.die-incoming {
  background: #2a0a0a;
}
.die-hex-outer.die-incoming:hover:not(:disabled) {
  background: #7a1010;
}
.die-hex-outer.die-clash {
  background: #3d2e00;
}
.die-hex-outer.die-clash:hover:not(:disabled) {
  background: var(--gold-dim);
}
.die-hex-outer.staggered {
  background: var(--crimson-dim);
}
.die-hex-outer.staggered:hover:not(:disabled) {
  background: var(--crimson);
}
.die-hex-outer:disabled {
  opacity: 0.3;
  cursor: not-allowed;
}

/* Inner hex = fill */
.die-hex-inner {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 3.9rem;
  height: 3.4rem;
  clip-path: var(--hex);
  background: var(--bg-card-2);
  font-family: var(--font-body);
  font-size: 1.35rem;
  color: var(--text-1);
  pointer-events: none;
  transition:
    background 0.12s,
    color 0.12s;
}
.die-hex-outer.die-incoming:hover:not(:disabled) .die-hex-inner {
  background: #1a0505;
  color: #fff;
}
.die-hex-outer.die-clash:hover:not(:disabled) .die-hex-inner {
  background: #261c00;
  color: #fff;
}
.die-hex-outer.staggered .die-hex-inner {
  background: #220808;
  color: var(--crimson-hi);
}
.die-hex-outer.staggered:hover:not(:disabled) .die-hex-inner {
  background: var(--crimson-dim);
  color: #fff;
}

.no-enemies {
  padding: 2rem 1rem;
  text-align: center;
  color: var(--text-2);
  font-size: 0.85rem;
  font-family: var(--font-body);
  font-style: italic;
}
</style>
