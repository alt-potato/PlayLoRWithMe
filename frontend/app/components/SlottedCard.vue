<!--
  SlottedCard.vue

  Compact card + dice row shown inside speed die slots for both allies and enemies.

  Props:
    sc          – slotted card object from game state (name, dice[], targetUnitId, …)
    targetLabel – formatted target string (e.g. "⚔ Roland ·2"), optional
    clash       – true when this card is clashing
    myColor     – ally color for target label (enemies omit this)
-->
<script setup lang="ts">
defineProps<{
  sc: any;
  targetLabel?: string;
  clash?: boolean;
  myColor?: string;
}>();
</script>

<template>
  <div class="sc-card">
    <div class="sc-top">
      <span class="sc-name">{{ sc.name }}</span>
      <span
        v-if="targetLabel"
        class="sc-target"
        :class="{ 'sc-clash': clash }"
        :style="{ color: clash ? '#e53935' : (myColor ?? '#aaa') }"
        >{{ targetLabel }}</span
      >
    </div>
    <div v-if="sc.dice?.length" class="sc-dice">
      <div v-for="(d, i) in sc.dice" :key="i" class="sc-die">
        <img
          v-if="diceIcon(d.type, d.detail)"
          :src="diceIcon(d.type, d.detail)!"
          class="sc-die-img"
          :alt="`${d.type} ${d.detail}`"
        />
        <span v-else class="sc-die-placeholder">·</span>
        <span class="sc-die-range" :style="{ color: dieTypeColor(d.type) }">
          {{ d.min }}–{{ d.max }}
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.sc-card {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0.06rem;
}

.sc-top {
  display: flex;
  align-items: baseline;
  gap: 0.25rem;
  min-width: 0;
}

.sc-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 0.68rem;
  color: var(--text-1);
  font-family: var(--font-body);
}

.sc-target {
  flex-shrink: 0;
  font-size: 0.62rem;
  font-family: var(--font-body);
}

.sc-clash {
  font-weight: bold;
}

.sc-dice {
  display: flex;
  flex-wrap: wrap;
  gap: 0.2rem;
}

.sc-die {
  display: flex;
  align-items: center;
  gap: 0.1rem;
}

.sc-die-img {
  width: 0.75rem;
  height: 0.75rem;
  object-fit: contain;
  flex-shrink: 0;
}

.sc-die-placeholder {
  width: 0.75rem;
  text-align: center;
  font-size: 0.55rem;
  color: var(--text-3);
  flex-shrink: 0;
}

.sc-die-range {
  font-family: var(--font-body);
  font-size: 0.48rem;
}
</style>
