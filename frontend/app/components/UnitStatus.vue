<!--
  UnitStatus.vue

  HP and stagger bars for a unit. Used by both AllyUnit and EnemyUnit.
  Light pips and emotion are rendered inline in each unit's header.

  Props:
    unit – unit object from the game state snapshot
-->
<script setup lang="ts">
defineProps<{ unit: any }>();
</script>

<template>
  <div class="bar-row" :title="`HP: ${unit.hp} / ${unit.maxHp}`">
    <div class="bar-track">
      <div class="bar-fill bar-hp" :style="{ width: hpPct(unit) + '%' }" />
    </div>
    <span class="bar-num">{{ unit.hp }}/{{ unit.maxHp }}</span>
  </div>
  <div
    class="bar-row"
    :title="`Stagger: ${unit.staggerGauge} / ${unit.maxStaggerGauge}`"
  >
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
</template>

<style scoped>
.bar-row {
  display: flex;
  align-items: center;
  gap: 0.3rem;
}
.bar-track {
  flex: 1;
  height: 4px;
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
  width: 4.2rem;
  text-align: right;
  font-family: var(--font-mono);
  font-size: 0.62rem;
  flex-shrink: 0;
}
</style>
