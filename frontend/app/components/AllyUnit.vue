<!--
  AllyUnit.vue

  Displays a single ally unit card. Header shows turn state, name, light pips,
  and emotion level. During SelectCard phase the hand becomes interactive.
  Everything else (hand read-only, EGO, team pages, passives, abnormalities,
  resistances) is collapsed into a single details section.

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

function dieColor(sc: any): string {
  if (sc.clash) return ARROW_COLORS.clash;
  if (sc.targetUnitId != null) return ARROW_COLORS.outgoing;
  return myColor.value; // Instance / untargeted
}

function targetLabel(sc: any): string {
  if (sc?.targetUnitId == null) return "";
  const u = allUnits.value.find((u: any) => u.id === sc.targetUnitId);
  const prefix = sc.clash ? "⚔" : "↗";
  return `${prefix} ${u?.name ?? `#${sc.targetUnitId}`} ·${sc.targetSlot}`;
}

const hasDetails = computed(() => {
  const u = props.unit;
  return (
    (!isSelectPhase.value && u.hand?.length) ||
    u.ego?.length ||
    u.teamHand?.length ||
    u.passives?.length ||
    u.abnormalities?.length ||
    u.keyPage
  );
});

const detailsLabel = computed(() => {
  const u = props.unit;
  const parts: string[] = [];
  if (!isSelectPhase.value && u.hand?.length)
    parts.push(`Hand (${u.hand.length})`);
  if (u.ego?.length) parts.push(`EGO (${u.ego.length})`);
  if (u.teamHand?.length) parts.push(`Team (${u.teamHand.length})`);
  if (u.passives?.length) parts.push(`Passives (${u.passives.length})`);
  if (u.abnormalities?.length) parts.push(`Abn. (${u.abnormalities.length})`);
  if (u.keyPage) parts.push("Res.");
  return parts.length ? parts.join(" · ") : "Details";
});
</script>

<template>
  <div class="unit-card">
    <!-- ── Header ── -->
    <div class="unit-header">
      <!-- row 1: turn badge, name -->
      <div class="unit-header-1">
        <span
          class="state-badge"
          :style="{ background: turnColor(unit.turnState) }"
          >{{ turnLabel(unit.turnState) }}</span
        >
        <span class="unit-name">{{
          unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}`
        }}</span>
      </div>

      <!-- row 2: light pips, emotion level -->
      <div class="unit-header-2">
        <div
          v-if="unit.maxPlayPoint"
          class="ap-pips"
          :title="`Light: ${unit.playPoint}/${unit.maxPlayPoint}`"
        >
          <span
            v-for="n in unit.maxPlayPoint"
            :key="n"
            class="ap-pip"
            :class="{ 'ap-pip--lit': n <= unit.playPoint }"
          />
        </div>
        <div class="unit-meta">
          <div v-if="unit.emotionCoins?.max" class="emotion-meta">
            <div class="epips">
              <span
                v-for="n in unit.emotionCoins.positive"
                :key="'p' + n"
                class="epip epip--pos"
              />
              <span
                v-for="n in unit.emotionCoins.negative"
                :key="'n' + n"
                class="epip epip--neg"
              />
              <span
                v-for="n in Math.max(
                  0,
                  unit.emotionCoins.max -
                    unit.emotionCoins.positive -
                    unit.emotionCoins.negative,
                )"
                :key="'e' + n"
                class="epip epip--empty"
              />
            </div>
            <span class="em-level">Em{{ unit.emotionLevel }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- ── HP / SG bars ── -->
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
          :style="sc !== null && !d.staggered ? { background: dieColor(sc) } : {}"
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

    <!-- ── Interactive hand (select phase only) ── -->
    <template v-if="isSelectPhase && unit.hand?.length">
      <div class="hand-section">
        <span class="section-label">Hand</span>

        <div class="hand-row">
          <HandCard
            v-for="(c, i) in unit.hand"
            :key="c.id.id + c.id.packageId"
            :card="c"
            :selected="selectingSlotFor?.unitId === unit.id && selectingSlotFor?.cardIndex === i"
            :dimmed="selectingSlotFor !== null && !(selectingSlotFor.unitId === unit.id && selectingSlotFor.cardIndex === i)"
            :color="myColor"
            @click="onCardClick(unit.id, i)"
          />
        </div>

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
    </template>

    <!-- ── Collapsed details ── -->
    <details v-if="hasDetails" class="collapse">
      <summary>{{ detailsLabel }}</summary>

      <!-- Hand (read-only, outside select phase) -->
      <template v-if="!isSelectPhase && unit.hand?.length">
        <div class="det-label">Hand</div>
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
      </template>

      <!-- EGO -->
      <template v-if="unit.ego?.length">
        <div class="det-label">EGO</div>
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
      </template>

      <!-- Team pages -->
      <template v-if="unit.teamHand?.length">
        <div class="det-label">Team Pages</div>
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
      </template>

      <!-- Passives -->
      <template v-if="unit.passives?.length">
        <div class="det-label">Passives</div>
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
      </template>

      <!-- Abnormalities -->
      <template v-if="unit.abnormalities?.length">
        <div class="det-label">Abnormalities</div>
        <div class="clist">
          <div v-for="ab in unit.abnormalities" :key="ab.id" class="centry">
            <span>{{ ab.name }}</span>
            <span class="centry-range">Lv{{ ab.emotionLevel }}</span>
          </div>
        </div>
      </template>

      <!-- Resistances -->
      <template v-if="unit.keyPage">
        <div class="det-label">Resistances</div>
        <ResistanceTable :resistances="unit.keyPage?.resistances" />
      </template>
    </details>
  </div>
</template>

<style scoped>
/* ── Card shell — ally accent on right ───────────────────────────────────── */
.unit-card {
  width: 100%;
  border-right: 2px solid var(--gold-dim);
}

/* ── Header — ally: meta left, name center, badge right (via row-reverse) ── */
.unit-header {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}
.unit-header div {
  display: flex;
  justify-content: space-between;
  gap: 0.4rem;
}
.unit-name {
  text-align: right;
}

/* ── Slot list (die on left, protrudes beyond card edge) ─────────────────── */
.slot-list {
  display: flex;
  flex-direction: column;
  gap: 0.07rem;
  margin-left: -1.8rem;
}
.slot-row {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.06rem 0.15rem 0.06rem 0;
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

/* ── Hand card row ───────────────────────────────────────────────────────── */
.hand-row {
  display: flex;
  gap: 0.35rem;
  flex-wrap: wrap;
}

/* ── Slot picker ─────────────────────────────────────────────────────────── */
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

/* Slot picker slide-in */
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

/* ── Detail sub-section labels ───────────────────────────────────────────── */
.det-label {
  font-family: var(--font-display);
  font-size: 0.53rem;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--text-3);
  margin-top: 0.35rem;
  padding-bottom: 0.1rem;
  border-bottom: 1px solid var(--border);
}
</style>
