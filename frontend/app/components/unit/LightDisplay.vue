<!-- 
  Displays a row of light pips.
  Defined as:

  ┌─────── max ───────┐

  │ lit │ dim │ unlit │

  └───────────┴┄┄┄┄┄┄┄┄┄ current
        └─────┴┄┄┄┄┄┄┄┄┄ reserved
-->
<script setup lang="ts">
const props = defineProps<{ current: number; max: number; reserved: number }>();
</script>

<template>
  <div
    v-if="max > 0"
    class="ap-pips"
    :title="`Light: ${current}/${max} (${reserved ?? 0} reserved)`"
  >
    <!-- lit -->
    <span
      v-for="n in Math.max(0, current - (reserved ?? 0))"
      :key="'f' + n"
      class="ap-pip ap-pip--lit"
    />
    <!-- dim -->
    <span
      v-for="n in reserved ?? 0"
      :key="'r' + n"
      class="ap-pip ap-pip--reserved"
    />
    <!-- unlit -->
    <span
      v-for="n in Math.max(0, max - current)"
      :key="'u' + n"
      class="ap-pip"
    />
  </div>
</template>

<style scoped>
.ap-pips {
  display: flex;
  gap: 0.08rem;
  align-items: center;
  flex-wrap: wrap;
}
.ap-pip {
  width: 0.62rem;
  height: 0.54rem;
  clip-path: var(--hex);
  background: var(--border-hi);
  flex-shrink: 0;
  transition: background 0.15s;
}
.ap-pip--lit {
  background: var(--gold);
}
.ap-pip--reserved {
  background: var(--gold-dim);
}
</style>
