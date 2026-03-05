<!--
  AllyUnit.vue

  Displays a single ally unit card. During the SelectCard phase the hand section
  becomes interactive. Dice and slotted cards are shown in a unified slot list
  (one row per die). Ally accent color connects this unit to its incoming-attack
  chips on enemy cards.

  Props:
    unit – ally unit object from the game state snapshot

  Injects: BATTLE_CTX (provided by BattleView)
-->
<script setup lang="ts">
import type { BattleCtx } from '~/composables/useBattleContext'

const props = defineProps<{ unit: any }>()

// Display helpers are auto-imported from useBattleDisplay.ts
// BATTLE_CTX is auto-imported from useBattleContext.ts
const { isSelectPhase, selectingSlotFor, onCardClick, onSlotClick, onRemoveCard, allyColors, allUnits } = inject(BATTLE_CTX) as BattleCtx

const myColor = computed(() => allyColors.value[props.unit.id] ?? '#888')

/** One entry per speed die, with its matching slotted card (if any). */
const slots = computed(() =>
  (props.unit.speedDice ?? []).map((d: any) => ({
    die: d,
    card: (props.unit.slottedCards ?? []).find((sc: any) => sc.slot === d.slot) ?? null,
  }))
)

function targetLabel(sc: any): string {
  if (sc?.targetUnitId == null) return ''
  const u = allUnits.value.find((u: any) => u.id === sc.targetUnitId)
  const prefix = sc.clash ? '⚔' : '↗'
  return `${prefix} ${u?.name ?? `#${sc.targetUnitId}`} ·${sc.targetSlot}`
}
</script>

<template>
  <div class="unit-card ally" :style="{ borderLeftColor: myColor }">

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

    <div class="stats-row">
      <span>AP <strong>{{ unit.playPoint }}/{{ unit.maxPlayPoint }}</strong></span>
      <span>Em <strong>{{ unit.emotionLevel }}/{{ unit.maxEmotionLevel }}</strong></span>
      <span class="coins" title="Emotion coins (●=positive ○=negative ·=empty)">{{ coinDots(unit) }}</span>
    </div>

    <!-- Unified slot list: one row per speed die -->
    <div v-if="slots.length" class="slot-list">
      <div
        v-for="{ die: d, card: sc } in slots"
        :key="d.slot"
        class="slot-row"
        :class="{ 'slot-filled': sc !== null }"
        :style="sc !== null ? { borderLeftColor: myColor } : {}"
      >
        <!-- Die value -->
        <span
          class="die"
          :class="{ staggered: d.staggered }"
        >{{ d.staggered ? '✕' : d.value }}</span>

        <!-- Card info or empty placeholder -->
        <template v-if="sc !== null">
          <span class="slot-card-name">{{ sc.name }}</span>
          <span v-if="sc.range" class="slot-card-range">{{ sc.range }}</span>
          <span
            v-if="targetLabel(sc)"
            class="slot-target"
            :class="{ 'slot-clash': sc.clash }"
            :style="{ color: sc.clash ? '#e53935' : myColor }"
          >{{ targetLabel(sc) }}</span>
          <button
            v-if="isSelectPhase"
            class="remove-btn"
            title="Return to hand"
            @click="onRemoveCard(unit.id, sc.slot)"
          >×</button>
        </template>
        <template v-else>
          <span class="slot-empty">—</span>
        </template>
      </div>
    </div>

    <div v-if="unit.buffs?.length" class="buffs">
      <span v-for="b in unit.buffs" :key="b.type" class="buff-tag">{{ b.type }}×{{ b.stacks }}</span>
    </div>

    <!-- Hand — interactive during SelectCard, collapsible read-only otherwise -->
    <template v-if="unit.hand?.length">
      <div v-if="isSelectPhase" class="hand-section">
        <div class="hand-label">Hand</div>
        <div class="card-list">
          <div
            v-for="(c, i) in unit.hand"
            :key="c.id.id + c.id.packageId"
            class="card-entry card-playable"
            :class="{
              'card-active': selectingSlotFor?.unitId === unit.id && selectingSlotFor?.cardIndex === i,
              'card-dim':    selectingSlotFor !== null && !(selectingSlotFor.unitId === unit.id && selectingSlotFor.cardIndex === i),
            }"
            @click="onCardClick(unit.id, i)"
          >
            <span class="card-cost">{{ c.cost }}</span>
            <span class="card-entry-name">{{ c.name }}</span>
            <span class="card-range">{{ c.range }}</span>
          </div>

          <!-- Slot picker appears below the selected card row -->
          <div v-if="selectingSlotFor?.unitId === unit.id" class="slot-picker">
            <span class="slot-picker-label">→ slot:</span>
            <button
              v-for="d in unit.speedDice"
              :key="d.slot"
              class="slot-btn"
              :disabled="isSlotFilled(unit, d.slot) || d.staggered"
              :title="d.staggered ? 'Broken' : isSlotFilled(unit, d.slot) ? 'Occupied' : `Play to slot ${d.slot}`"
              @click.stop="onSlotClick(unit, selectingSlotFor!.cardIndex, d.slot)"
            >{{ d.slot }}</button>
            <button class="slot-btn slot-cancel" title="Cancel" @click.stop="selectingSlotFor = null">✕</button>
          </div>
        </div>
      </div>

      <details v-else class="collapse">
        <summary>Hand ({{ unit.hand.length }})</summary>
        <div class="card-list">
          <div v-for="c in unit.hand" :key="c.id.id + c.id.packageId" class="card-entry">
            <span class="card-cost">{{ c.cost }}</span>
            <span>{{ c.name }}</span>
            <span class="card-range">{{ c.range }}</span>
          </div>
        </div>
      </details>
    </template>

    <details v-if="unit.ego?.length" class="collapse">
      <summary>EGO ({{ unit.ego.length }})</summary>
      <div class="card-list">
        <div v-for="c in unit.ego" :key="c.id.id + c.id.packageId" class="card-entry" :class="{ unavailable: !c.available }">
          <span class="card-cost">{{ c.cost }}</span>
          <span>{{ c.name }}</span>
          <span class="card-range">{{ c.available ? '✓' : '…' }}</span>
        </div>
      </div>
    </details>

    <details v-if="unit.teamHand?.length" class="collapse">
      <summary>Team Pages ({{ unit.teamHand.length }})</summary>
      <div class="card-list">
        <div v-for="c in unit.teamHand" :key="c.id.id + c.id.packageId" class="card-entry">
          <span class="card-cost">{{ c.cost }}</span>
          <span>{{ c.name }}</span>
        </div>
      </div>
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

    <details class="collapse">
      <summary>Resistances</summary>
      <ResistanceTable :resistances="unit.keyPage?.resistances" />
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
.ally { border-left: 3px solid #c9a227; }

.unit-header { display: flex; justify-content: space-between; align-items: center; gap: 0.5rem; }
.unit-name { color: #e0e0e0; font-weight: bold; flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.turn-badge { font-size: 0.6rem; padding: 0.1rem 0.3rem; border-radius: 2px; color: #000; white-space: nowrap; font-weight: bold; }

.bar-row { display: flex; align-items: center; gap: 0.4rem; }
.bar-label { color: #666; width: 1.5rem; flex-shrink: 0; }
.bar-track { flex: 1; height: 6px; background: #222; border-radius: 3px; overflow: hidden; }
.bar-fill { height: 100%; border-radius: 3px; transition: width 0.3s; }
.bar-fill.hp { background: #2e7d32; }
.bar-num { color: #888; width: 5rem; text-align: right; flex-shrink: 0; }

.stats-row { display: flex; gap: 0.75rem; color: #888; font-size: 0.72rem; }
.stats-row strong { color: #ccc; }
.coins { letter-spacing: 0.05em; color: #c9a227; }

/* Unified slot list */
.slot-list { display: flex; flex-direction: column; gap: 0.15rem; }
.slot-row {
  display: flex; align-items: center; gap: 0.35rem;
  padding: 0.15rem 0.25rem; border-radius: 3px;
  border-left: 2px solid transparent;
  font-size: 0.72rem;
}
.slot-row.slot-filled { background: #1a1a30; }

.die {
  display: inline-flex; align-items: center; justify-content: center;
  width: 1.4rem; height: 1.4rem; flex-shrink: 0;
  background: #1e1e3a; border: 1px solid #3a3a6a; border-radius: 3px;
  font-size: 0.75rem; color: #ccc;
}
.die.staggered { background: #2a0a0a; border-color: #6a1a1a; color: #e53935; }

.slot-card-name { color: #bbb; flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.slot-card-range { color: #555; white-space: nowrap; font-size: 0.65rem; }
.slot-target { white-space: nowrap; font-size: 0.65rem; flex-shrink: 0; }
.slot-clash { font-weight: bold; }
.slot-empty { color: #333; font-style: italic; }

.remove-btn {
  margin-left: auto; flex-shrink: 0;
  background: transparent; border: none; color: #555;
  cursor: pointer; font-size: 0.75rem; padding: 0 0.2rem; line-height: 1; font-family: monospace;
}
.remove-btn:hover { color: #e53935; }

.buffs { display: flex; flex-wrap: wrap; gap: 0.2rem; }
.buff-tag {
  font-size: 0.65rem; padding: 0.1rem 0.3rem;
  background: #2a1a00; border: 1px solid #5a3a00; border-radius: 2px; color: #ff9800;
}

/* Hand (interactive) */
.hand-section { display: flex; flex-direction: column; gap: 0.15rem; }
.hand-label { font-size: 0.7rem; color: #555; margin-bottom: 0.05rem; }

.card-playable {
  cursor: pointer; border-radius: 3px; padding: 0.15rem 0.25rem;
  transition: background 0.1s, opacity 0.15s;
}
.card-playable:hover       { background: #1e1e3a; }
.card-playable.card-active { background: #1a2a1a; outline: 1px solid #2e7d32; }
.card-playable.card-dim    { opacity: 0.35; }
.card-entry-name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

/* Slot picker */
.slot-picker {
  display: flex; align-items: center; gap: 0.25rem;
  padding: 0.2rem 0.25rem;
  background: #0e1e0e; border: 1px solid #2e7d32; border-radius: 3px;
}
.slot-picker-label { font-size: 0.65rem; color: #4caf50; }
.slot-btn {
  display: inline-flex; align-items: center; justify-content: center;
  width: 1.4rem; height: 1.4rem;
  background: #1e1e3a; border: 1px solid #3a3a6a; border-radius: 3px;
  font-size: 0.7rem; color: #ccc; cursor: pointer; font-family: monospace;
}
.slot-btn:hover:not(:disabled) { background: #2e7d32; border-color: #4caf50; color: #fff; }
.slot-btn:disabled { opacity: 0.3; cursor: not-allowed; }
.slot-btn.slot-cancel { border-color: #6a1a1a; color: #e57373; }
.slot-btn.slot-cancel:hover { background: #6a1a1a; color: #fff; }

/* Collapsibles */
.collapse { margin-top: 0.1rem; }
.collapse summary { cursor: pointer; font-size: 0.7rem; color: #555; user-select: none; padding: 0.1rem 0; }
.collapse summary:hover { color: #888; }

/* Card lists (hand read-only, EGO, team pages, passives, abnormalities) */
.card-list { display: flex; flex-direction: column; gap: 0.1rem; margin-top: 0.25rem; }
.card-entry { display: flex; gap: 0.3rem; align-items: baseline; font-size: 0.7rem; color: #aaa; }
.card-entry.unavailable { color: #555; }
.card-cost {
  display: inline-flex; align-items: center; justify-content: center;
  width: 1rem; height: 1rem;
  background: #1a1a2e; border: 1px solid #3a3a5a; border-radius: 2px;
  font-size: 0.65rem; color: #c9a227; flex-shrink: 0;
}
.card-range { color: #555; margin-left: auto; }
</style>
