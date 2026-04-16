<!--
  PassiveList.vue

  Shared passive-entry list used in battle (DisplayCard), pre-battle detail
  (SettingDetailPanel / KeyPageDetail), and the passives tab. Each passive is a
  left-border tile coloured by rarity with a cost digit. Entries with a
  description are expandable via a native <details> element.

  Props:
    passives – array of Passive objects (may be empty)

  Slots:
    #action({ passive }) – optional; rendered on the right side of each row
                           for context-specific buttons (Attribute, Remove, etc.)
-->
<script setup lang="ts">
import type { Passive } from "~/types/game";

defineProps<{ passives: Passive[] }>();

defineSlots<{
  action?(props: { passive: Passive }): unknown;
}>();

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
        <summary class="passive-header">
          <span class="chevron">▸</span>
          <span v-if="p.cost != null" class="passive-cost">{{ p.cost }}</span>
          <span class="passive-name">{{ p.name }}</span>
          <span class="passive-action">
            <slot name="action" :passive="p" />
          </span>
        </summary>
        <p class="passive-desc">{{ p.desc }}</p>
      </details>
      <div v-else class="passive-entry" :class="passiveClass(p)">
        <span class="passive-header">
          <span v-if="p.cost != null" class="passive-cost">{{ p.cost }}</span>
          <span class="passive-name">{{ p.name }}</span>
          <span class="passive-action">
            <slot name="action" :passive="p" />
          </span>
        </span>
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
  padding-left: 0;
  padding-top: 0;
  padding-bottom: 0;
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

.passive-header {
  font-size: 0.7rem;
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

.passive-header::marker,
.passive-header::-webkit-details-marker {
  display: none;
}

details.passive-entry > .passive-header {
  cursor: pointer;
}

.chevron {
  font-size: 0.55rem;
  color: var(--text-3);
  flex-shrink: 0;
  display: inline-block;
  transition: transform 0.18s ease;
}

details[open].passive-entry > .passive-header .chevron {
  transform: rotate(90deg);
}

.passive-cost {
  font-size: 0.65rem;
  color: var(--text-3);
  flex-shrink: 0;
  width: 1rem;
  text-align: center;
  font-family: var(--font-display);
}

.passive-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.passive-action {
  flex-shrink: 0;
  display: flex;
  align-items: center;
}

.passive-negative > .passive-header .passive-name {
  color: var(--crimson-hi);
}

.passive-desc {
  font-size: 0.68rem;
  color: var(--text-2);
  line-height: 1.45;
  margin: 0.2rem 0 0.05rem;
  padding-left: 0.4rem;
}
</style>
