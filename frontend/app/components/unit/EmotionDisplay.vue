<!-- 
  Emotion coin display, plus current emotion level.

  Defined as:

  ┌─────────────────────┬┄ max

  │ green │ red │ empty │

  └─────────────┴┄┄┄┄┄┄┄┄┄ positive
          └─────┴┄┄┄┄┄┄┄┄┄ negative

  Props:
    positive: number of positive coins
    negative: number of negative coins
    max: maximum number of coins
    level: current emotion level
-->
<script setup lang="ts">
import { toRoman } from '~/composables/useBattleDisplay';

const props = defineProps<{
  positive: number;
  negative: number;
  max: number;
  level: number;
}>();
</script>

<template>
  <div v-if="max > 0" class="emotion-meta reversible-container">
    <div class="epips reversible-container">
      <span v-for="n in positive" :key="'p' + n" class="epip epip--pos" />
      <span v-for="n in negative" :key="'n' + n" class="epip epip--neg" />
      <span
        v-for="n in Math.max(0, max - positive - negative)"
        :key="'e' + n"
        class="epip epip--empty"
      />
    </div>
    <span class="em-level">Em{{ toRoman(level) }}</span>
  </div>
</template>

<style scoped>
.emotion-meta {
  display: flex;
  align-items: center;
  gap: 0.2rem;
}
.em-level {
  font-family: var(--font-body);
  font-size: 0.55rem;
  color: var(--text-2);
  white-space: nowrap;
  flex-shrink: 0;
}
.epips {
  display: flex;
  gap: 0.09rem;
  align-items: center;
  flex-wrap: wrap;
}
.epip {
  width: 0.45rem;
  height: 0.45rem;
  border-radius: 50%;
  flex-shrink: 0;
}
.epip--pos {
  background: #4caf50;
}
.epip--neg {
  background: #e53935;
}
.epip--empty {
  background: var(--border-mid);
}
</style>
