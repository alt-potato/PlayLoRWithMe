<script setup lang="ts">
const props = defineProps<{ state: any }>()

const RESIST_COLORS: Record<string, string> = {
  Weak:        '#e53935',
  Vulnerable:  '#bf360c',
  Normal:      '#555',
  Endure:      '#2e7d32',
  Resist:      '#1565c0',
  Immune:      '#6a1b9a',
}
const TURNSTATE_COLORS: Record<string, string> = {
  WAIT_CARD:       '#c9a227',
  ACTION_WAITING:  '#c9a227',
  BREAK:           '#e53935',
  ACTION_BREAK:    '#e53935',
  DEAD:            '#444',
  MOVE:            '#888',
  STAND_BY:        '#888',
}

function resistColor(val: string) {
  return RESIST_COLORS[val] ?? '#555'
}
function turnColor(val: string) {
  return TURNSTATE_COLORS[val] ?? '#888'
}
function hpPct(unit: any) {
  return unit.maxHp > 0 ? Math.min(100, unit.hp / unit.maxHp * 100) : 0
}
function sgPct(unit: any) {
  return unit.maxStaggerGauge > 0
    ? Math.min(100, unit.staggerGauge / unit.maxStaggerGauge * 100)
    : 0
}
function sgColor(unit: any) {
  const pct = sgPct(unit)
  if (pct <= 0)  return '#e53935'
  if (pct < 30)  return '#ff9800'
  return '#1976d2'
}
function coinDots(unit: any) {
  const coins = unit.emotionCoins
  if (!coins) return ''
  const pos = '●'.repeat(coins.positive)
  const neg = '○'.repeat(coins.negative)
  const empty = '·'.repeat(Math.max(0, coins.max - coins.positive - coins.negative))
  return pos + neg + empty
}
</script>

<template>
  <!-- Stage bar -->
  <div class="stage-bar">
    <span class="stage-item">Floor <strong>{{ state.stage?.floor }}</strong></span>
    <span class="stage-item">Ch <strong>{{ state.stage?.chapter }}</strong></span>
    <span class="stage-item">Wave <strong>{{ state.stage?.wave }}</strong></span>
    <span class="stage-item">Round <strong>{{ state.stage?.round }}</strong></span>
    <span class="stage-sep"/>
    <span class="phase">{{ state.phase }}</span>
  </div>

  <!-- Unit columns: enemies left, allies right (matches standard invitation layout) -->
  <div class="battlefield">
    <!-- Enemies -->
    <section class="side enemies">
      <h2>Enemies</h2>
      <div v-for="unit in state.enemies" :key="unit.id" class="unit-card enemy">

        <div class="unit-header">
          <span class="unit-name">{{ unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}` }}</span>
          <span class="turn-badge" :style="{ background: turnColor(unit.turnState) }">
            {{ unit.turnState }}
          </span>
        </div>

        <div class="bar-row">
          <span class="bar-label">HP</span>
          <div class="bar-track">
            <div class="bar-fill hp" :style="{ width: hpPct(unit) + '%' }"/>
          </div>
          <span class="bar-num">{{ unit.hp }}/{{ unit.maxHp }}</span>
        </div>

        <div class="bar-row">
          <span class="bar-label">SG</span>
          <div class="bar-track">
            <div class="bar-fill" :style="{ width: sgPct(unit) + '%', background: sgColor(unit) }"/>
          </div>
          <span class="bar-num">{{ unit.staggerGauge }}/{{ unit.maxStaggerGauge }}</span>
        </div>

        <div v-if="unit.speedDice?.length" class="dice-row">
          <span
            v-for="d in unit.speedDice" :key="d.slot"
            class="die"
            :class="{ staggered: d.staggered }"
          >{{ d.staggered ? '✕' : d.value }}</span>
        </div>

        <div v-if="unit.slottedCards?.length" class="slotted-cards">
          <div v-for="sc in unit.slottedCards" :key="sc.slot" class="slotted-card">
            <span class="slot-num">[{{ sc.slot }}]</span>
            <span class="card-name">{{ sc.name }}</span>
            <span v-if="sc.targetUnitId != null" class="card-target">→ #{{ sc.targetUnitId }}·{{ sc.targetSlot }}</span>
          </div>
        </div>

        <div v-if="unit.buffs?.length" class="buffs">
          <span v-for="b in unit.buffs" :key="b.type" class="buff-tag">{{ b.type }}×{{ b.stacks }}</span>
        </div>

        <details class="collapse">
          <summary>Resistances</summary>
          <table class="resist-table">
            <thead>
              <tr><th></th><th>Slash</th><th>Pierce</th><th>Blunt</th></tr>
            </thead>
            <tbody>
              <tr>
                <th>HP</th>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.slashHp) }">{{ unit.keyPage?.resistances?.slashHp }}</td>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.pierceHp) }">{{ unit.keyPage?.resistances?.pierceHp }}</td>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.bluntHp) }">{{ unit.keyPage?.resistances?.bluntHp }}</td>
              </tr>
              <tr>
                <th>SG</th>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.slashBp) }">{{ unit.keyPage?.resistances?.slashBp }}</td>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.pierceBp) }">{{ unit.keyPage?.resistances?.pierceBp }}</td>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.bluntBp) }">{{ unit.keyPage?.resistances?.bluntBp }}</td>
              </tr>
            </tbody>
          </table>
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
    </section>

    <!-- Allies -->
    <section class="side allies">
      <h2>Allies</h2>
      <div v-for="unit in state.allies" :key="unit.id" class="unit-card ally">

        <!-- Header -->
        <div class="unit-header">
          <span class="unit-name">{{ unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}` }}</span>
          <span class="turn-badge" :style="{ background: turnColor(unit.turnState) }">
            {{ unit.turnState }}
          </span>
        </div>

        <!-- HP -->
        <div class="bar-row">
          <span class="bar-label">HP</span>
          <div class="bar-track">
            <div class="bar-fill hp" :style="{ width: hpPct(unit) + '%' }"/>
          </div>
          <span class="bar-num">{{ unit.hp }}/{{ unit.maxHp }}</span>
        </div>

        <!-- Stagger -->
        <div class="bar-row">
          <span class="bar-label">SG</span>
          <div class="bar-track">
            <div class="bar-fill" :style="{ width: sgPct(unit) + '%', background: sgColor(unit) }"/>
          </div>
          <span class="bar-num">{{ unit.staggerGauge }}/{{ unit.maxStaggerGauge }}</span>
        </div>

        <!-- Ally stats row -->
        <div class="stats-row">
          <span>AP <strong>{{ unit.playPoint }}/{{ unit.maxPlayPoint }}</strong></span>
          <span>Em <strong>{{ unit.emotionLevel }}/{{ unit.maxEmotionLevel }}</strong></span>
          <span class="coins" title="Emotion coins (●=positive ○=negative ·=empty)">{{ coinDots(unit) }}</span>
        </div>

        <!-- Speed dice -->
        <div v-if="unit.speedDice?.length" class="dice-row">
          <span
            v-for="d in unit.speedDice" :key="d.slot"
            class="die"
            :class="{ staggered: d.staggered }"
            :title="`Slot ${d.slot}`"
          >{{ d.staggered ? '✕' : d.value }}</span>
        </div>

        <!-- Slotted cards -->
        <div v-if="unit.slottedCards?.length" class="slotted-cards">
          <div v-for="sc in unit.slottedCards" :key="sc.slot" class="slotted-card">
            <span class="slot-num">[{{ sc.slot }}]</span>
            <span class="card-name">{{ sc.name }}</span>
            <span v-if="sc.targetUnitId != null" class="card-target">→ #{{ sc.targetUnitId }}·{{ sc.targetSlot }}</span>
          </div>
        </div>

        <!-- Buffs -->
        <div v-if="unit.buffs?.length" class="buffs">
          <span v-for="b in unit.buffs" :key="b.type" class="buff-tag">{{ b.type }}×{{ b.stacks }}</span>
        </div>

        <!-- Resistances (collapsible) -->
        <details class="collapse">
          <summary>Resistances</summary>
          <table class="resist-table">
            <thead>
              <tr><th></th><th>Slash</th><th>Pierce</th><th>Blunt</th></tr>
            </thead>
            <tbody>
              <tr>
                <th>HP</th>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.slashHp) }">{{ unit.keyPage?.resistances?.slashHp }}</td>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.pierceHp) }">{{ unit.keyPage?.resistances?.pierceHp }}</td>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.bluntHp) }">{{ unit.keyPage?.resistances?.bluntHp }}</td>
              </tr>
              <tr>
                <th>SG</th>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.slashBp) }">{{ unit.keyPage?.resistances?.slashBp }}</td>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.pierceBp) }">{{ unit.keyPage?.resistances?.pierceBp }}</td>
                <td :style="{ color: resistColor(unit.keyPage?.resistances?.bluntBp) }">{{ unit.keyPage?.resistances?.bluntBp }}</td>
              </tr>
            </tbody>
          </table>
        </details>

        <!-- Hand (collapsible) -->
        <details v-if="unit.hand?.length" class="collapse">
          <summary>Hand ({{ unit.hand.length }})</summary>
          <div class="card-list">
            <div v-for="c in unit.hand" :key="c.id.id + c.id.packageId" class="card-entry">
              <span class="card-cost">{{ c.cost }}</span>
              <span>{{ c.name }}</span>
              <span class="card-range">{{ c.range }}</span>
            </div>
          </div>
        </details>

        <!-- EGO (collapsible) -->
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

        <!-- Team abnormality hand (collapsible) -->
        <details v-if="unit.teamHand?.length" class="collapse">
          <summary>Team Pages ({{ unit.teamHand.length }})</summary>
          <div class="card-list">
            <div v-for="c in unit.teamHand" :key="c.id.id + c.id.packageId" class="card-entry">
              <span class="card-cost">{{ c.cost }}</span>
              <span>{{ c.name }}</span>
            </div>
          </div>
        </details>

        <!-- Passives (collapsible) -->
        <details v-if="unit.passives?.length" class="collapse">
          <summary>Passives ({{ unit.passives.length }})</summary>
          <div class="card-list">
            <div v-for="p in unit.passives" :key="p.id.id + p.id.packageId" class="card-entry" :class="{ unavailable: p.disabled }">
              <span>{{ p.name }}</span>
            </div>
          </div>
        </details>

        <!-- Abnormalities (collapsible) -->
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
    </section>

  </div>
</template>

<style scoped>
/* Stage bar */
.stage-bar {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0.4rem 0.75rem;
  background: #16162a;
  border: 1px solid #2a2a4a;
  border-radius: 4px;
  margin-bottom: 0.75rem;
  font-size: 0.8rem;
}
.stage-item { color: #888; }
.stage-item strong { color: #c9a227; }
.stage-sep { flex: 1; }
.phase { color: #aaa; font-size: 0.75rem; font-style: italic; }

/* Battlefield layout */
.battlefield {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.75rem;
  align-items: start;
}

.side h2 {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  margin-bottom: 0.5rem;
}
.allies h2 { color: #c9a227; }
.enemies h2 { color: #c62828; }

.side { display: flex; flex-direction: column; gap: 0.5rem; }

/* Unit card */
.unit-card {
  background: #13132a;
  border-radius: 4px;
  padding: 0.6rem;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  font-size: 0.78rem;
}
.ally  { border-left: 3px solid #c9a227; }
.enemy { border-left: 3px solid #c62828; }

/* Header */
.unit-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.5rem;
}
.unit-name { color: #e0e0e0; font-weight: bold; flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.turn-badge { font-size: 0.6rem; padding: 0.1rem 0.3rem; border-radius: 2px; color: #000; white-space: nowrap; font-weight: bold; }

/* Bars */
.bar-row { display: flex; align-items: center; gap: 0.4rem; }
.bar-label { color: #666; width: 1.5rem; flex-shrink: 0; }
.bar-track { flex: 1; height: 6px; background: #222; border-radius: 3px; overflow: hidden; }
.bar-fill { height: 100%; border-radius: 3px; transition: width 0.3s; }
.bar-fill.hp { background: #2e7d32; }
.bar-num { color: #888; width: 5rem; text-align: right; flex-shrink: 0; }

/* Stats row (ally only) */
.stats-row {
  display: flex;
  gap: 0.75rem;
  color: #888;
  font-size: 0.72rem;
}
.stats-row strong { color: #ccc; }
.coins { letter-spacing: 0.05em; color: #c9a227; }

/* Speed dice */
.dice-row { display: flex; gap: 0.25rem; flex-wrap: wrap; }
.die {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1.4rem;
  height: 1.4rem;
  background: #1e1e3a;
  border: 1px solid #3a3a6a;
  border-radius: 3px;
  font-size: 0.75rem;
  color: #ccc;
}
.die.staggered { background: #2a0a0a; border-color: #6a1a1a; color: #e53935; }

/* Slotted cards */
.slotted-cards { display: flex; flex-direction: column; gap: 0.15rem; }
.slotted-card { display: flex; gap: 0.3rem; align-items: baseline; font-size: 0.72rem; }
.slot-num { color: #555; width: 1.5rem; flex-shrink: 0; }
.card-name { color: #bbb; flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.card-target { color: #555; white-space: nowrap; }

/* Buffs */
.buffs { display: flex; flex-wrap: wrap; gap: 0.2rem; }
.buff-tag {
  font-size: 0.65rem;
  padding: 0.1rem 0.3rem;
  background: #2a1a00;
  border: 1px solid #5a3a00;
  border-radius: 2px;
  color: #ff9800;
}

/* Collapsibles */
.collapse { margin-top: 0.1rem; }
.collapse summary {
  cursor: pointer;
  font-size: 0.7rem;
  color: #555;
  user-select: none;
  padding: 0.1rem 0;
}
.collapse summary:hover { color: #888; }

/* Resistance table */
.resist-table {
  margin-top: 0.3rem;
  font-size: 0.7rem;
  border-collapse: collapse;
  width: 100%;
}
.resist-table th { color: #555; font-weight: normal; text-align: center; padding: 0.1rem 0.3rem; }
.resist-table td { text-align: center; padding: 0.1rem 0.3rem; font-weight: bold; }

/* Card lists (hand, ego, passives, etc.) */
.card-list { display: flex; flex-direction: column; gap: 0.1rem; margin-top: 0.25rem; }
.card-entry {
  display: flex;
  gap: 0.3rem;
  align-items: baseline;
  font-size: 0.7rem;
  color: #aaa;
}
.card-entry.unavailable { color: #555; }
.card-cost {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1rem;
  height: 1rem;
  background: #1a1a2e;
  border: 1px solid #3a3a5a;
  border-radius: 2px;
  font-size: 0.65rem;
  color: #c9a227;
  flex-shrink: 0;
}
.card-range { color: #555; margin-left: auto; }
</style>
