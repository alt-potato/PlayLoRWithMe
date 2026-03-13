<!--
  UnitStatus.vue

  HP and stagger bars for a unit. Used by both AllyUnit and EnemyUnit.
  Light pips and emotion are rendered inline in each unit's header.

  Props:
    unit – unit object from the game state snapshot
-->
<script setup lang="ts">
import { fillPercentage } from "~/composables/useBattleDisplay";

const props = defineProps<{
  hp: number;
  maxHp: number;
  sg: number;
  maxSg: number;
}>();

const hpPercent = computed(() => fillPercentage(props.hp, props.maxHp));
const sgPercent = computed(() => fillPercentage(props.sg, props.maxSg));

// Stagger bar turns red when fully depleted (broken), matching the in-game visual.
const sgColor = computed(() =>
  sgPercent.value <= 0 ? "var(--red-hi)" : "var(--stagger-bar)",
);
</script>

<template>
  <div class="bar-row" :title="`HP: ${hp} / ${maxHp}`">
    <div class="bar-track">
      <div class="bar-fill bar-fill--hp" :style="{ width: hpPercent + '%' }" />
    </div>
    <span class="bar-num">{{ hp }}/{{ maxHp }}</span>
  </div>
  <div class="bar-row" :title="`Stagger: ${sg} / ${maxSg}`">
    <div class="bar-track">
      <div
        class="bar-fill"
        :style="{ width: sgPercent + '%', background: sgColor }"
      />
    </div>
    <span class="bar-num">{{ sg }}/{{ maxSg }}</span>
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
  transition:
    width 0.3s ease,
    background 0.4s ease;
}

.bar-fill--hp {
  background: var(--health-bar);
}
.bar-num {
  color: var(--text-2);
  width: 4.2rem;
  text-align: right;
  font-family: var(--font-body);
  font-size: 0.62rem;
  flex-shrink: 0;
}
</style>
