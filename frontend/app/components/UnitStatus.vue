<!--
  UnitStatus.vue

  HP/stagger bars, light (action point) pips, emotion level, and emotion coin
  pips for a unit. Used by both AllyUnit and EnemyUnit.

  Props:
    unit – unit object from the game state snapshot
-->
<script setup lang="ts">
defineProps<{ unit: any }>();
</script>

<template>
  <!-- ── HP / SG bars ── -->
  <div class="bar-row">
    <span class="bar-lbl">HP</span>
    <div class="bar-track">
      <div class="bar-fill bar-hp" :style="{ width: hpPct(unit) + '%' }" />
    </div>
    <span class="bar-num">{{ unit.hp }}/{{ unit.maxHp }}</span>
  </div>
  <div class="bar-row">
    <span class="bar-lbl">SP</span>
    <div class="bar-track">
      <div
        class="bar-fill"
        :style="{ width: sgPct(unit) + '%', background: sgColor(unit) }"
      />
    </div>
    <span class="bar-num"
      >{{ unit.staggerGauge }}/{{ unit.maxStaggerGauge }}</span
    >
  </div>

  <!-- ── Light + Emotion ── -->
  <div class="stats-row">
    <div
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
    <span class="stat"
      >Em <strong>{{ unit.emotionLevel }}</strong></span
    >
    <div
      class="emotion-pips"
      :title="`${unit.emotionCoins?.positive ?? 0}+ / ${unit.emotionCoins?.negative ?? 0}-`"
    >
      <span
        v-for="n in unit.emotionCoins?.positive ?? 0"
        :key="'p' + n"
        class="emotion-pip emotion-pip--pos"
      />
      <span
        v-for="n in unit.emotionCoins?.negative ?? 0"
        :key="'n' + n"
        class="emotion-pip emotion-pip--neg"
      />
      <span
        v-for="n in Math.max(
          0,
          (unit.emotionCoins?.max ?? 0) -
            (unit.emotionCoins?.positive ?? 0) -
            (unit.emotionCoins?.negative ?? 0),
        )"
        :key="'e' + n"
        class="emotion-pip emotion-pip--empty"
      />
    </div>
  </div>

  <details class="collapse">
    <summary>Resistances</summary>
    <ResistanceTable :resistances="unit.keyPage?.resistances" />
  </details>
</template>

<style scoped>
/* ── Bars ── */
.bar-row {
  display: flex;
  align-items: center;
  gap: 0.35rem;
}
.bar-lbl {
  color: var(--text-2);
  width: 1.4rem;
  font-family: var(--font-mono);
  font-size: 0.65rem;
  flex-shrink: 0;
}
.bar-track {
  flex: 1;
  height: 5px;
  background: var(--bg-card-3);
  overflow: hidden;
}
.bar-fill {
  height: 100%;
  transition: width 0.3s;
}
.bar-hp {
  background: var(--green-hi);
}
.bar-num {
  color: var(--text-2);
  width: 4.8rem;
  text-align: right;
  font-family: var(--font-mono);
  font-size: 0.65rem;
  flex-shrink: 0;
}

/* ── Stats ── */
.stats-row {
  display: flex;
  gap: 0.65rem;
  align-items: center;
  color: var(--text-2);
  font-size: 0.68rem;
  font-family: var(--font-mono);
}
.stat {
  color: var(--text-2);
  font-size: 0.68rem;
  font-family: var(--font-mono);
}
.stat strong {
  color: var(--text-1);
}

.ap-pips {
  display: flex;
  gap: 0.1rem;
  flex-wrap: wrap;
  align-items: center;
}
.ap-pip {
  width: 0.75rem;
  height: 0.65rem;
  clip-path: var(--hex);
  background: var(--border-hi);
  flex-shrink: 0;
  transition: background 0.15s;
}
.ap-pip--lit {
  background: var(--gold);
}

.emotion-pips {
  display: flex;
  gap: 0.1rem;
  align-items: center;
  flex-wrap: wrap;
}
.emotion-pip {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  flex-shrink: 0;
}
.emotion-pip--pos {
  background: #4caf50;
}
.emotion-pip--neg {
  background: #e53935;
}
.emotion-pip--empty {
  background: var(--border-mid);
}
</style>
