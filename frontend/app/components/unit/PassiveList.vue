<!--
  PassiveList.vue

  Shared passive-entry list used in both the battle view (DisplayCard) and the
  pre-battle detail panel (SettingDetailPanel). Renders each passive as a
  left-border tile coloured by rarity. Entries with a description are
  expandable via a native <details> element; entries without are plain divs.

  Props:
    passives – array of Passive objects (may be empty)
-->
<script setup lang="ts">
import type { Passive } from "~/types/game";

defineProps<{ passives: Passive[] }>();

function passiveClass(p: Passive) {
  const cls: Record<string, boolean> = {
    unavailable: !!p.disabled,
    "passive-negative": !!p.isNegative,
  };
  if (p.rare) cls[`rarity-${p.rare.toLowerCase()}`] = true;
  return cls;
}
</script>

<template>
  <div class="passive-list">
    <template v-for="p in passives" :key="p.id.id + p.id.packageId">
      <details v-if="p.desc" class="passive-entry" :class="passiveClass(p)">
        <summary class="passive-name"><span class="chevron">▸</span>{{ p.name }}</summary>
        <p class="passive-desc">{{ p.desc }}</p>
      </details>
      <div v-else class="passive-entry" :class="passiveClass(p)">
        <span class="passive-name">{{ p.name }}</span>
      </div>
    </template>
  </div>
</template>

<style scoped>
.passive-list {
  display: flex;
  flex-direction: column;
  margin-top: 0.2rem;
}

.passive-entry {
  border-left: 2px solid var(--border-mid);
  padding-left: 0.4rem;
  padding-top: 0.15rem;
  padding-bottom: 0.15rem;
}

.passive-entry + .passive-entry {
  border-top: 1px solid var(--border);
}

.passive-entry.rarity-uncommon {
  border-left-color: var(--rarity-uncommon);
}

.passive-entry.rarity-rare {
  border-left-color: var(--rarity-rare);
}

.passive-entry.rarity-unique {
  border-left-color: var(--gold);
}

.passive-entry.rarity-special {
  border-left-color: var(--crimson-hi);
}

.passive-entry.unavailable {
  opacity: 0.42;
}

.passive-name {
  font-size: 0.7rem;
  color: var(--text-1);
  list-style: none;
  display: flex;
  align-items: baseline;
  gap: 0.3rem;
  cursor: default;
  user-select: none;
  line-height: 1.4;
  text-align: left;
}

.passive-name::marker,
.passive-name::-webkit-details-marker {
  display: none;
}

details.passive-entry > .passive-name {
  cursor: pointer;
}

.chevron {
  font-size: 0.55rem;
  color: var(--text-3);
  flex-shrink: 0;
  display: inline-block;
  margin-right: 0.25em;
  transition: transform 0.18s ease;
}

details[open].passive-entry > .passive-name .chevron {
  transform: rotate(90deg);
}

.passive-negative > .passive-name {
  color: var(--crimson-hi);
}

.passive-desc {
  font-size: 0.68rem;
  color: var(--text-2);
  line-height: 1.45;
  margin: 0.2rem 0 0.05rem;
}
</style>
