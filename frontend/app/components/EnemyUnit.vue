<!--
  EnemyUnit.vue

  Displays a single enemy unit card. Speed dice protrude from the right edge
  toward the battlefield centre (centerpoint at card border). Each slot row
  shows card info on the left and the die on the right.

  Props:
    unit – enemy unit object from the game state snapshot

  Injects: BATTLE_CTX (provided by BattleView)
-->
<script setup lang="ts">
import type { BattleCtx } from "~/composables/useBattleContext";

const props = defineProps<{ unit: any }>();

const { attackMap } = inject(BATTLE_CTX) as BattleCtx;

const sortedSlots = computed(() =>
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
</script>

<template>
  <div class="unit-card">
    <!-- ── Header ── -->
    <div class="unit-header">
      <span
        class="turn-badge"
        :style="{ background: turnColor(unit.turnState) }"
        >{{ unit.turnState }}</span
      >
      <span class="unit-name">{{
        unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}`
      }}</span>
    </div>

    <!-- ── HP / SG bars + Light + Emotion ── -->
    <UnitStatus :unit="unit" />

    <!-- ── Speed dice + slotted cards ── -->
    <div v-if="sortedSlots.length" class="slot-list">
      <div
        v-for="{ die: d, card: sc } in sortedSlots"
        :key="d.slot"
        class="slot-row"
        :class="{ 'slot-filled': sc !== null }"
      >
        <!-- Content: card info + incoming chips (left) -->
        <div class="slot-content">
          <div v-if="sc !== null" class="slot-card-row">
            <span class="sc-name">{{ sc.name }}</span>
            <span
              v-if="sc.targetUnitId != null"
              class="sc-target"
              :class="{ 'sc-clash': sc.clash }"
              >{{ sc.clash ? "⚔" : "↗" }} #{{ sc.targetUnitId }}·{{
                sc.targetSlot
              }}</span
            >
          </div>
          <div v-if="attackMap[unit.id]?.[d.slot]?.length" class="incoming-row">
            <span
              v-for="atk in attackMap[unit.id][d.slot]"
              :key="atk.name"
              class="incoming-chip"
              :class="{ 'chip-mass': isMassRange(atk.range) }"
              :style="{ borderColor: atk.color, color: atk.color }"
              >↑ {{ atk.name }}{{ isMassRange(atk.range) ? " ✦" : "" }}</span
            >
          </div>
          <span
            v-if="sc === null && !attackMap[unit.id]?.[d.slot]?.length"
            class="slot-empty"
            >—</span
          >
        </div>

        <!-- Die (protrudes beyond right card edge) -->
        <span
          class="hex-wrap"
          :class="{ staggered: d.staggered }"
          :data-die="`${unit.id}-${d.slot}`"
          :title="`Slot ${d.slot}`"
        >
          <span class="hex-inner">{{ d.staggered ? "✕" : d.value }}</span>
        </span>
      </div>
    </div>

    <!-- ── Buffs ── -->
    <div v-if="unit.buffs?.length" class="buffs">
      <span v-for="b in unit.buffs" :key="b.type" class="buff-tag"
        >{{ b.type }}×{{ b.stacks }}</span
      >
    </div>

    <!-- ── Collapsible sections ── -->
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
/* ── Card shell — enemy accent on left ───────────────────────────────────── */
.unit-card {
  width: 100%;
  border-left: 2px solid var(--crimson);
}

/* ── Header — enemy: badge left, name right ──────────────────────────────── */
.unit-header {
  display: flex;
  justify-content: flex-start;
  align-items: center;
  gap: 0.5rem;
}

/* ── Slot list (die on right, protrudes beyond card edge) ────────────────── */
.slot-list {
  display: flex;
  flex-direction: column;
  gap: 0.09rem;
  margin-right: -1.8rem; /* die centerpoint at card right border */
}
.slot-row {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.08rem 0 0.08rem 0.15rem;
}
.slot-content {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0.06rem;
}
.slot-card-row {
  display: flex;
  gap: 0.3rem;
  align-items: baseline;
  min-width: 0;
}
/* ── Incoming attack chips ───────────────────────────────────────────────── */
.incoming-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.2rem;
}
.incoming-chip {
  font-size: 0.6rem;
  padding: 0.08rem 0.28rem;
  background: transparent;
  border: 1px solid;
  white-space: nowrap;
  font-family: var(--font-mono);
}
.chip-mass {
  font-weight: bold;
}
</style>
