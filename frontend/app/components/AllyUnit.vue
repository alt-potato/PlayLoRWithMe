<!--
  AllyUnit.vue

  Displays a single ally unit card. During SelectCard phase the hand becomes
  interactive: cards are shown as tappable card shapes; selecting one reveals
  hexagonal slot-picker dice below the hand row.

  Props:
    unit – ally unit object from the game state snapshot

  Injects: BATTLE_CTX (provided by BattleView)
-->
<script setup lang="ts">
import type { BattleCtx } from "~/composables/useBattleContext";

const props = defineProps<{ unit: any }>();

const {
  isSelectPhase,
  selectingSlotFor,
  onCardClick,
  onSlotClick,
  onRemoveCard,
  allyColors,
  allUnits,
} = inject(BATTLE_CTX) as BattleCtx;

const myColor = computed(() => allyColors.value[props.unit.id] ?? "#888");

const slots = computed(() =>
  [...(props.unit.speedDice ?? [])]
    .sort((a: any, b: any) => {
      if (a.staggered !== b.staggered) return a.staggered ? -1 : 1;
      return b.value - a.value;
    })
    .map((d: any) => ({
      die: d,
      card:
        (props.unit.slottedCards ?? []).find((sc: any) => sc.slot === d.slot) ??
        null,
    })),
);

const sortedDice = computed(() =>
  [...(props.unit.speedDice ?? [])].sort((a: any, b: any) => {
    if (a.staggered !== b.staggered) return a.staggered ? -1 : 1;
    return b.value - a.value;
  }),
);

function targetLabel(sc: any): string {
  if (sc?.targetUnitId == null) return "";
  const u = allUnits.value.find((u: any) => u.id === sc.targetUnitId);
  const prefix = sc.clash ? "⚔" : "↗";
  return `${prefix} ${u?.name ?? `#${sc.targetUnitId}`} ·${sc.targetSlot}`;
}
</script>

<template>
  <div
    class="unit-card"
    :style="{ '--accent': myColor, borderRightColor: myColor }"
  >
    <!-- ── Header ── -->
    <div class="unit-header">
      <span class="unit-name">{{
        unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}`
      }}</span>
      <span
        class="turn-badge"
        :style="{ background: turnColor(unit.turnState) }"
        >{{ unit.turnState }}</span
      >
    </div>

    <!-- ── HP / SG bars + Light + Emotion ── -->
    <UnitStatus :unit="unit" />

    <!-- ── Speed dice + slotted cards ── -->
    <div v-if="slots.length" class="slot-list">
      <div
        v-for="{ die: d, card: sc } in slots"
        :key="d.slot"
        class="slot-row"
        :class="{ 'slot-filled': sc !== null }"
      >
        <!-- Hexagonal die (data-die used by ArrowOverlay for coordinate lookup) -->
        <span
          class="hex-wrap"
          :class="{ staggered: d.staggered }"
          :data-die="`${unit.id}-${d.slot}`"
          :style="sc !== null && !d.staggered ? { background: myColor } : {}"
        >
          <span class="hex-inner">{{ d.staggered ? "✕" : d.value }}</span>
        </span>

        <template v-if="sc !== null">
          <span class="sc-name">{{ sc.name }}</span>
          <span
            v-if="targetLabel(sc)"
            class="sc-target"
            :class="{ 'sc-clash': sc.clash }"
            :style="{ color: sc.clash ? '#e53935' : myColor }"
            >{{ targetLabel(sc) }}</span
          >
          <button
            v-if="isSelectPhase"
            class="remove-btn"
            title="Return to hand"
            @click="onRemoveCard(unit.id, sc.slot)"
          >
            ×
          </button>
        </template>
        <template v-else>
          <span class="slot-empty">—</span>
        </template>
      </div>
    </div>

    <!-- ── Buffs ── -->
    <div v-if="unit.buffs?.length" class="buffs">
      <span v-for="b in unit.buffs" :key="b.type" class="buff-tag"
        >{{ b.type }}×{{ b.stacks }}</span
      >
    </div>

    <!-- ── Hand ── -->
    <template v-if="unit.hand?.length">
      <!-- Interactive card shapes during SelectCard phase -->
      <div v-if="isSelectPhase" class="hand-section">
        <span class="section-label">Hand</span>

        <!-- Card shapes -->
        <div class="hand-row">
          <div
            v-for="(c, i) in unit.hand"
            :key="c.id.id + c.id.packageId"
            class="hcard"
            :class="{
              'hcard--selected':
                selectingSlotFor?.unitId === unit.id &&
                selectingSlotFor?.cardIndex === i,
              'hcard--dim':
                selectingSlotFor !== null &&
                !(
                  selectingSlotFor.unitId === unit.id &&
                  selectingSlotFor.cardIndex === i
                ),
            }"
            :style="
              selectingSlotFor?.unitId === unit.id &&
              selectingSlotFor?.cardIndex === i
                ? { borderColor: myColor, '--glow': myColor + '44' }
                : {}
            "
            @click="onCardClick(unit.id, i)"
          >
            <span class="hcard-cost">{{ c.cost }}</span>
            <span class="hcard-name">{{ c.name }}</span>
            <span class="hcard-range">{{ c.range }}</span>
          </div>
        </div>

        <!-- Slot picker: shows hex dice after a card is selected -->
        <transition name="picker">
          <div v-if="selectingSlotFor?.unitId === unit.id" class="slot-picker">
            <span class="picker-label">Slot</span>
            <span
              v-for="d in sortedDice"
              :key="d.slot"
              class="shex-outer"
              :class="{
                'shex-open': !isSlotFilled(unit, d.slot) && !d.staggered,
                'shex-occupied': isSlotFilled(unit, d.slot),
                'shex-staggered': d.staggered,
              }"
              :title="
                d.staggered
                  ? 'Broken'
                  : isSlotFilled(unit, d.slot)
                    ? 'Occupied'
                    : `Play to slot ${d.slot}`
              "
              @click.stop="
                !(isSlotFilled(unit, d.slot) || d.staggered) &&
                onSlotClick(unit, selectingSlotFor!.cardIndex, d.slot)
              "
            >
              <span class="shex-inner">{{ d.staggered ? "✕" : d.value }}</span>
            </span>
            <button class="picker-cancel" @click.stop="selectingSlotFor = null">
              ✕
            </button>
          </div>
        </transition>
      </div>

      <!-- Collapsed read-only outside select phase -->
      <details v-else class="collapse">
        <summary>Hand ({{ unit.hand.length }})</summary>
        <div class="clist">
          <div
            v-for="c in unit.hand"
            :key="c.id.id + c.id.packageId"
            class="centry"
          >
            <span class="centry-cost">{{ c.cost }}</span>
            <span>{{ c.name }}</span>
            <span class="centry-range">{{ c.range }}</span>
          </div>
        </div>
      </details>
    </template>

    <!-- ── EGO ── -->
    <details v-if="unit.ego?.length" class="collapse">
      <summary>EGO ({{ unit.ego.length }})</summary>
      <div class="clist">
        <div
          v-for="c in unit.ego"
          :key="c.id.id + c.id.packageId"
          class="centry"
          :class="{ unavailable: !c.available }"
        >
          <span class="centry-cost">{{ c.cost }}</span>
          <span>{{ c.name }}</span>
          <span class="centry-range">{{ c.available ? "✓" : "…" }}</span>
        </div>
      </div>
    </details>

    <!-- ── Team pages ── -->
    <details v-if="unit.teamHand?.length" class="collapse">
      <summary>Team Pages ({{ unit.teamHand.length }})</summary>
      <div class="clist">
        <div
          v-for="c in unit.teamHand"
          :key="c.id.id + c.id.packageId"
          class="centry"
        >
          <span class="centry-cost">{{ c.cost }}</span>
          <span>{{ c.name }}</span>
        </div>
      </div>
    </details>

    <!-- ── Passives ── -->
    <details v-if="unit.passives?.length" class="collapse">
      <summary>Passives ({{ unit.passives.length }})</summary>
      <div class="clist">
        <div
          v-for="p in unit.passives"
          :key="p.id.id + p.id.packageId"
          class="centry"
          :class="{ unavailable: p.disabled }"
        >
          <span>{{ p.name }}</span>
        </div>
      </div>
    </details>

    <!-- ── Abnormalities ── -->
    <details v-if="unit.abnormalities?.length" class="collapse">
      <summary>Abnormalities ({{ unit.abnormalities.length }})</summary>
      <div class="clist">
        <div v-for="ab in unit.abnormalities" :key="ab.id" class="centry">
          <span>{{ ab.name }}</span>
          <span class="centry-range">Lv{{ ab.emotionLevel }}</span>
        </div>
      </div>
    </details>
  </div>
</template>

<style scoped>
/* ── Card shell — ally accent on right ───────────────────────────────────── */
.unit-card {
  width: 100%;
  border-right: 2px solid var(--gold-dim);
}

/* ── Header — ally: name right, badge left ───────────────────────────────── */
.unit-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.5rem;
  flex-direction: row-reverse;
}
.unit-name {
  text-align: right;
}

/* ── Slot list (die on left, protrudes beyond card edge) ─────────────────── */
.slot-list {
  display: flex;
  flex-direction: column;
  gap: 0.09rem;
  margin-left: -1.8rem; /* die centerpoint at card left border */
}
.slot-row {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.08rem 0.15rem 0.08rem 0;
}
.remove-btn {
  margin-left: auto;
  flex-shrink: 0;
  background: transparent;
  border: none;
  color: var(--text-3);
  cursor: pointer;
  font-size: 0.8rem;
  padding: 0 0.15rem;
  line-height: 1;
  font-family: var(--font-mono);
}
.remove-btn:hover {
  color: var(--crimson-hi);
}

/* ── Hand section ────────────────────────────────────────────────────────── */
.hand-section {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.section-label {
  font-family: var(--font-display);
  font-size: 0.58rem;
  text-transform: uppercase;
  letter-spacing: 0.12em;
  color: var(--text-2);
}

/* ── Hand card shapes ────────────────────────────────────────────────────── */
.hand-row {
  display: flex;
  gap: 0.35rem;
  flex-wrap: wrap;
}

.hcard {
  flex-shrink: 0;
  width: 3.8rem;
  min-height: 5.2rem;
  background: linear-gradient(
    160deg,
    var(--bg-card-2) 0%,
    var(--bg-card-3) 100%
  );
  border: 1px solid var(--border-mid);
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 0.2rem 0.15rem 0.15rem;
  gap: 0.15rem;
  cursor: pointer;
  position: relative;
  touch-action: manipulation;
  transition:
    transform 0.1s,
    border-color 0.12s,
    box-shadow 0.12s;
  user-select: none;
  -webkit-user-select: none;
}
.hcard:hover {
  transform: translateY(-3px);
  border-color: var(--border-hi);
}
.hcard--selected {
  transform: translateY(-5px);
  box-shadow: 0 4px 16px var(--glow, rgba(201, 162, 39, 0.25));
}
.hcard--dim {
  opacity: 0.28;
  pointer-events: none;
}

.hcard-cost {
  width: 1.25rem;
  height: 1.25rem;
  background: var(--gold-dim);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.72rem;
  color: var(--gold-bright);
  font-family: var(--font-mono);
  font-weight: bold;
  flex-shrink: 0;
  align-self: flex-start;
}
.hcard-name {
  flex: 1;
  font-size: 0.58rem;
  color: var(--text-1);
  font-family: var(--font-body);
  text-align: center;
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 4;
  line-clamp: 4;
  -webkit-box-orient: vertical;
  line-height: 1.3;
  width: 100%;
}
.hcard-range {
  font-size: 0.5rem;
  color: var(--text-2);
  font-family: var(--font-mono);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  align-self: flex-end;
}

/* ── Slot picker (hex dice for slot selection) ───────────────────────────── */
.slot-picker {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  flex-wrap: wrap;
  padding: 0.4rem 0.5rem;
  background: #091509;
  border: 1px solid var(--green-hi);
}

.picker-label {
  font-family: var(--font-display);
  font-size: 0.58rem;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: #4caf50;
}

/* Slot picker hex dice — slightly larger for easy tapping */
.shex-outer {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.8rem;
  height: 2.45rem;
  clip-path: var(--hex);
  background: var(--border-mid);
  cursor: pointer;
  transition: background 0.1s;
}
.shex-inner {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.35rem;
  height: 2.05rem;
  clip-path: var(--hex);
  background: var(--bg-card-3);
  font-family: var(--font-mono);
  font-size: 0.88rem;
  color: var(--text-2);
  pointer-events: none;
  transition:
    background 0.1s,
    color 0.1s;
}

.shex-outer.shex-open {
  background: var(--green-hi);
  cursor: pointer;
}
.shex-outer.shex-open .shex-inner {
  background: #0c1e0c;
  color: var(--text-1);
}
.shex-outer.shex-open:hover {
  background: #4caf50;
}
.shex-outer.shex-open:hover .shex-inner {
  background: #102010;
  color: #fff;
}

.shex-outer.shex-occupied {
  background: var(--border);
  opacity: 0.4;
  cursor: not-allowed;
}
.shex-outer.shex-occupied .shex-inner {
  background: var(--bg-card);
  color: var(--text-3);
}

.shex-outer.shex-staggered {
  background: var(--crimson-dim);
  cursor: not-allowed;
}
.shex-outer.shex-staggered .shex-inner {
  background: #1a0606;
  color: var(--crimson-hi);
}

.picker-cancel {
  background: transparent;
  border: 1px solid var(--crimson);
  color: #e57373;
  font-size: 0.72rem;
  padding: 0.2rem 0.5rem;
  cursor: pointer;
  font-family: var(--font-mono);
  margin-left: auto;
}
.picker-cancel:hover {
  background: var(--crimson);
  color: #fff;
}

/* Slot picker slide-in animation */
.picker-enter-active {
  transition:
    opacity 0.15s,
    transform 0.15s;
}
.picker-leave-active {
  transition:
    opacity 0.1s,
    transform 0.1s;
}
.picker-enter-from,
.picker-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}
</style>
