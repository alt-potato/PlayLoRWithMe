<!--
  AbnormalityList.vue

  In-battle list of an ally's assigned abnormality/emotion cards. Mirrors the
  layout conventions of UnitPassiveList: each entry is a left-border tile
  coloured by the card's MentalState (positive/negative/fallback). Entries
  whose ability description is available expand via a native <details>.

  Props:
    abnormalities - AbnormalityEntry[]; emotionLevel is required for the
                    roman-numeral badge (cards lacking it render as plain Lv?).
-->
<script setup lang="ts">
import type { AbnormalityEntry } from "~/types/game";

defineProps<{ abnormalities: AbnormalityEntry[] }>();

// Maps MentalState to the styling bucket. Anything outside the known game
// values falls through to "fallback" so unexpected data still renders.
function stateBucket(state: string | undefined): "positive" | "negative" | "fallback" {
  if (state === "Positive") return "positive";
  if (state === "Negative") return "negative";
  return "fallback";
}
</script>

<template>
  <div class="abn-list">
    <template v-for="ab in abnormalities" :key="ab.id">
      <details v-if="ab.desc" class="abn-entry" :class="`state-${stateBucket(ab.state)}`">
        <summary class="abn-header">
          <span class="abn-level">{{ ab.emotionLevel != null ? toRoman(ab.emotionLevel) : "?" }}</span>
          <span class="abn-name">{{ ab.name }}</span>
          <span class="chevron">▸</span>
        </summary>
        <p class="abn-desc">{{ ab.desc }}</p>
        <p v-if="ab.flavorText" class="abn-flavor">{{ ab.flavorText }}</p>
      </details>
      <div v-else class="abn-entry" :class="`state-${stateBucket(ab.state)}`">
        <span class="abn-header">
          <span class="abn-level">{{ ab.emotionLevel != null ? toRoman(ab.emotionLevel) : "?" }}</span>
          <span class="abn-name">{{ ab.name }}</span>
        </span>
      </div>
    </template>
  </div>
</template>

<style scoped>
.abn-list {
  display: flex;
  flex-direction: column;
  margin-top: 0.2rem;
}

.abn-entry {
  border-left: 2px solid var(--border-mid);
  padding-left: 0;
  padding-top: 0;
  padding-bottom: 0;
}

.abn-entry + .abn-entry {
  border-top: 1px solid var(--border);
}

.abn-entry.state-positive {
  border-left-color: var(--green);
}
.abn-entry.state-negative {
  border-left-color: var(--crimson-hi);
}
.abn-entry.state-fallback {
  border-left-color: var(--border-mid);
}

.abn-header {
  font-size: var(--fs-3xs);
  color: var(--text-1);
  list-style: none;
  display: flex;
  align-items: center;
  gap: 0.3rem;
  cursor: default;
  user-select: none;
  line-height: 1.4;
  text-align: left;
  padding: 0.15rem 0.4rem;
}

.abn-header::marker,
.abn-header::-webkit-details-marker {
  display: none;
}

details.abn-entry > .abn-header {
  cursor: pointer;
}

.abn-level {
  flex-shrink: 0;
  min-width: 1.1rem;
  text-align: left;
  font-family: var(--font-display);
  font-size: var(--fs-3xs);
  letter-spacing: 0.04em;
  color: var(--text-2);
}
.abn-entry.state-positive .abn-level {
  color: var(--green);
}
.abn-entry.state-negative .abn-level {
  color: var(--crimson-hi);
}

.abn-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chevron {
  font-size: var(--fs-4xs);
  color: var(--text-3);
  flex-shrink: 0;
  display: inline-block;
  transition: transform 0.18s ease;
}

details[open].abn-entry > .abn-header .chevron {
  transform: rotate(90deg);
}

.abn-desc {
  font-size: var(--fs-3xs);
  color: var(--text-2);
  line-height: 1.45;
  margin: 0.2rem 0 0.05rem;
  padding-left: 0.4rem;
}

.abn-flavor {
  font-size: var(--fs-4xs);
  color: var(--text-3);
  font-style: italic;
  line-height: 1.4;
  margin: 0.15rem 0 0.2rem;
  padding-left: 0.4rem;
  border-top: 1px solid var(--border);
  padding-top: 0.25rem;
}
</style>
