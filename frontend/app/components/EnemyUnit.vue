<!--
  EnemyUnit.vue

  Displays a single enemy unit card. Shows incoming-attack chips beneath each
  speed die row (from any attacker — ally or enemy — via attackMap).

  Props:
    unit – enemy unit object from the game state snapshot

  Injects: BATTLE_CTX (provided by BattleView)
-->
<script setup lang="ts">
import type { BattleCtx } from '~/composables/useBattleContext'

defineProps<{ unit: any }>()

// Display helpers are auto-imported from useBattleDisplay.ts
// BATTLE_CTX is auto-imported from useBattleContext.ts
const { attackMap } = inject(BATTLE_CTX) as BattleCtx
</script>

<template>
  <div class="unit-card enemy">

    <div class="unit-header">
      <span class="unit-name">{{ unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}` }}</span>
      <span class="turn-badge" :style="{ background: turnColor(unit.turnState) }">{{ unit.turnState }}</span>
    </div>

    <div class="bar-row">
      <span class="bar-label">HP</span>
      <div class="bar-track"><div class="bar-fill hp" :style="{ width: hpPct(unit) + '%' }"/></div>
      <span class="bar-num">{{ unit.hp }}/{{ unit.maxHp }}</span>
    </div>
    <div class="bar-row">
      <span class="bar-label">SG</span>
      <div class="bar-track"><div class="bar-fill" :style="{ width: sgPct(unit) + '%', background: sgColor(unit) }"/></div>
      <span class="bar-num">{{ unit.staggerGauge }}/{{ unit.maxStaggerGauge }}</span>
    </div>

    <!-- Speed dice + incoming-attack chips -->
    <div v-if="unit.speedDice?.length" class="dice-section">
      <div class="dice-row">
        <span
          v-for="d in unit.speedDice" :key="d.slot"
          class="die"
          :class="{ staggered: d.staggered }"
          :title="`Slot ${d.slot}`"
        >{{ d.staggered ? '✕' : d.value }}</span>
      </div>
      <template v-for="d in unit.speedDice" :key="'atk-' + d.slot">
        <div v-if="attackMap[unit.id]?.[d.slot]?.length" class="incoming-row">
          <span
            v-for="atk in attackMap[unit.id][d.slot]" :key="atk.name"
            class="incoming-chip"
            :class="{ 'chip-mass': isMassRange(atk.range) }"
            :style="{ borderColor: atk.color, color: atk.color }"
          >↑ {{ atk.name }}{{ isMassRange(atk.range) ? ' ✦' : '' }}</span>
        </div>
      </template>
    </div>

    <div v-if="unit.slottedCards?.length" class="slotted-cards">
      <div v-for="sc in unit.slottedCards" :key="sc.slot" class="slotted-card">
        <span class="slot-num">[{{ sc.slot }}]</span>
        <span class="card-name">{{ sc.name }}</span>
        <span v-if="sc.targetUnitId != null" class="card-target" :class="{ 'card-clash': sc.clash }">
          {{ sc.clash ? '⚔' : '↗' }} #{{ sc.targetUnitId }}·{{ sc.targetSlot }}
        </span>
      </div>
    </div>

    <div v-if="unit.buffs?.length" class="buffs">
      <span v-for="b in unit.buffs" :key="b.type" class="buff-tag">{{ b.type }}×{{ b.stacks }}</span>
    </div>

    <details class="collapse">
      <summary>Resistances</summary>
      <ResistanceTable :resistances="unit.keyPage?.resistances" />
    </details>

    <details v-if="unit.passives?.length" class="collapse">
      <summary>Passives ({{ unit.passives.length }})</summary>
      <div class="card-list">
        <div v-for="p in unit.passives" :key="p.id.id + p.id.packageId" class="card-entry" :class="{ unavailable: p.disabled }">
          <span>{{ p.name }}</span>
        </div>
      </div>
    </details>

    <details v-if="unit.abnormalities?.length" class="collapse">
      <summary>Abnormalities ({{ unit.abnormalities.length }})</summary>
      <div class="card-list">
        <div v-for="ab in unit.abnormalities" :key="ab.id" class="card-entry">
          <span>{{ ab.name }}</span>
          <span class="card-range">Lv{{ ab.emotionLevel }}</span>
        </div>
      </div>
    </details>

  </div>
</template>

<style scoped>
.unit-card {
  background: #13132a;
  border-radius: 4px;
  padding: 0.6rem;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  font-size: 0.78rem;
}
.enemy { border-left: 3px solid #c62828; }

.unit-header { display: flex; justify-content: space-between; align-items: center; gap: 0.5rem; }
.unit-name { color: #e0e0e0; font-weight: bold; flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.turn-badge { font-size: 0.6rem; padding: 0.1rem 0.3rem; border-radius: 2px; color: #000; white-space: nowrap; font-weight: bold; }

.bar-row { display: flex; align-items: center; gap: 0.4rem; }
.bar-label { color: #666; width: 1.5rem; flex-shrink: 0; }
.bar-track { flex: 1; height: 6px; background: #222; border-radius: 3px; overflow: hidden; }
.bar-fill { height: 100%; border-radius: 3px; transition: width 0.3s; }
.bar-fill.hp { background: #2e7d32; }
.bar-num { color: #888; width: 5rem; text-align: right; flex-shrink: 0; }

.dice-section { display: flex; flex-direction: column; gap: 0.2rem; }
.dice-row { display: flex; gap: 0.25rem; flex-wrap: wrap; }
.die {
  display: inline-flex; align-items: center; justify-content: center;
  width: 1.4rem; height: 1.4rem;
  background: #1e1e3a; border: 1px solid #3a3a6a; border-radius: 3px;
  font-size: 0.75rem; color: #ccc;
}
.die.staggered { background: #2a0a0a; border-color: #6a1a1a; color: #e53935; }

.incoming-row { display: flex; flex-wrap: wrap; gap: 0.2rem; }
.incoming-chip {
  font-size: 0.65rem; padding: 0.1rem 0.3rem;
  background: transparent; border: 1px solid; border-radius: 2px;
  white-space: nowrap;
}
.incoming-chip.chip-mass { font-weight: bold; }

.slotted-cards { display: flex; flex-direction: column; gap: 0.15rem; }
.slotted-card { display: flex; gap: 0.3rem; align-items: baseline; font-size: 0.72rem; }
.slot-num { color: #555; width: 1.5rem; flex-shrink: 0; }
.card-name { color: #bbb; flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.card-target { color: #555; white-space: nowrap; }
.card-target.card-clash { color: #e53935; font-weight: bold; }

.buffs { display: flex; flex-wrap: wrap; gap: 0.2rem; }
.buff-tag {
  font-size: 0.65rem; padding: 0.1rem 0.3rem;
  background: #2a1a00; border: 1px solid #5a3a00; border-radius: 2px; color: #ff9800;
}

.collapse { margin-top: 0.1rem; }
.collapse summary { cursor: pointer; font-size: 0.7rem; color: #555; user-select: none; padding: 0.1rem 0; }
.collapse summary:hover { color: #888; }

.card-list { display: flex; flex-direction: column; gap: 0.1rem; margin-top: 0.25rem; }
.card-entry { display: flex; gap: 0.3rem; align-items: baseline; font-size: 0.7rem; color: #aaa; }
.card-entry.unavailable { color: #555; }
.card-range { color: #555; margin-left: auto; }
</style>
