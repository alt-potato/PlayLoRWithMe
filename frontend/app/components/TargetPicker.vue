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

const props = defineProps<{
  selecting: {
    unitId: number; cardIndex: number; diceSlot: number;
    cardName: string; cardRange: string;
  }
  enemies: any[]
}>()

const emit = defineEmits<{
  pick: [unitId: number, diceSlot: number]
  cancel: []
}>()

function pick(unitId: number, diceSlot: number) {
  emit('pick', unitId, diceSlot)
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
      <span v-if="isMassRange(selecting.cardRange)" class="mass-badge">MASS</span>
      <span class="slot-label">Slot {{ selecting.diceSlot }} → target</span>
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
          <span v-if="!unit.targetable" class="untargetable-label">⚠ untargetable</span>
        </div>
        <div class="die-row">
          <button
            v-for="d in unit.speedDice"
            :key="d.slot"
            class="die-btn"
            :class="{ staggered: d.staggered }"
            :disabled="!unit.targetable"
            :title="d.staggered ? `Target slot ${d.slot} (broken)` : `Target slot ${d.slot}`"
            @click="pick(unit.id, d.slot)"
          >{{ d.staggered ? '✕' : d.value }}</button>
        </div>
      </div>

      <div v-if="!enemies.length" class="no-enemies">No enemies</div>
    </div>
  </div>
</template>

<style scoped>
.backdrop {
  position: fixed; inset: 0;
  background: rgba(0, 0, 0, 0.55);
  z-index: 100;
}

.sheet {
  position: fixed; left: 0; right: 0; bottom: 0;
  max-height: 70vh; overflow-y: auto;
  background: #16162a;
  border-top: 2px solid #2a2a5a;
  border-radius: 12px 12px 0 0;
  z-index: 101;
  padding-bottom: env(safe-area-inset-bottom, 0);
}

.sheet-header {
  display: flex; align-items: center; gap: 0.5rem;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid #2a2a4a;
  position: sticky; top: 0;
  background: #16162a;
}
.card-label {
  font-weight: bold; color: #e0e0e0; font-size: 0.9rem;
  flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.mass-badge {
  font-size: 0.6rem; padding: 0.1rem 0.35rem;
  background: #4a1a00; border: 1px solid #bf360c; border-radius: 2px;
  color: #ff7043; white-space: nowrap; font-weight: bold; letter-spacing: 0.05em;
}
.slot-label {
  font-size: 0.72rem; color: #666; white-space: nowrap;
}
.close-btn {
  background: transparent; border: none; color: #666;
  font-size: 1rem; cursor: pointer; padding: 0 0.2rem; line-height: 1;
  flex-shrink: 0;
}
.close-btn:hover { color: #e53935; }

.enemy-list {
  display: flex; flex-direction: column; gap: 0;
}

.enemy-section {
  padding: 0.75rem 1rem;
  border-bottom: 1px solid #1e1e3a;
  transition: opacity 0.15s;
}
.enemy-section.untargetable { opacity: 0.45; pointer-events: none; }

.enemy-name {
  display: flex; align-items: center; gap: 0.5rem;
  font-size: 0.8rem; color: #ccc; margin-bottom: 0.5rem; font-weight: bold;
}
.untargetable-label {
  font-size: 0.65rem; color: #ff9800; font-weight: normal;
}

.die-row { display: flex; gap: 0.5rem; flex-wrap: wrap; }

.die-btn {
  display: inline-flex; align-items: center; justify-content: center;
  min-width: 3.5rem; height: 3.5rem;
  background: #1e1e3a; border: 1px solid #3a3a6a; border-radius: 6px;
  font-size: 1.1rem; color: #ccc; cursor: pointer; font-family: monospace;
  transition: background 0.1s, border-color 0.1s, color 0.1s;
}
.die-btn:hover:not(:disabled) {
  background: #1565c0; border-color: #42a5f5; color: #fff;
}
.die-btn.staggered {
  background: #2a0a0a; border-color: #6a1a1a; color: #e53935;
}
.die-btn.staggered:hover:not(:disabled) {
  background: #6a1a1a; border-color: #e53935; color: #fff;
}
.die-btn:disabled {
  opacity: 0.35; cursor: not-allowed;
}

.no-enemies {
  padding: 1.5rem 1rem; text-align: center; color: #555; font-size: 0.8rem;
}
</style>
