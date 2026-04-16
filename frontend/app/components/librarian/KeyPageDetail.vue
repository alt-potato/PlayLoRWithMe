<!--
  KeyPageDetail.vue

  Read-only stat overview for a key page. Accepts either an AvailableKeyPage
  (from the inventory) or a KeyPage (from a librarian or battle unit).
  Sections are omitted gracefully when optional fields are absent.

  Props:
    keyPage – key page data to display
-->
<script setup lang="ts">
import type { AvailableKeyPage, KeyPage, Passive, Resistances } from "~/types/game";

/** Union of both key page shapes — use optional fields for extras not on KeyPage. */
export type AnyKeyPage = AvailableKeyPage | KeyPage;

const props = defineProps<{ keyPage: AnyKeyPage }>();

const kp = computed(() => props.keyPage);
const resistances = computed((): Resistances | undefined => kp.value.resistances);
const passives = computed((): Passive[] => ("passives" in kp.value ? kp.value.passives : []));
const hp = computed((): number | undefined => kp.value.hp);
const breakGauge = computed((): number | undefined => kp.value.breakGauge);
const hasSpeed = computed(() => kp.value.speedMin != null && kp.value.speedMax != null);
</script>

<template>
  <div class="kp-detail">
    <div class="kp-name">{{ kp.name }}</div>

    <!-- Stats: HP, break gauge, speed -->
    <div class="kp-stats">
      <div v-if="hp != null" class="stat-row">
        <img src="/assets/stats/health.png" class="stat-icon" alt="HP" />
        <span class="stat-value">{{ hp }}</span>
      </div>
      <div v-if="breakGauge != null" class="stat-row">
        <img src="/assets/stats/stagger.png" class="stat-icon" alt="Stagger" />
        <span class="stat-value">{{ breakGauge }}</span>
      </div>
      <div v-if="hasSpeed" class="stat-row">
        <img src="/assets/stats/speed.png" class="stat-icon" alt="Speed" />
        <span class="stat-value">{{ kp.speedMin }}–{{ kp.speedMax }}</span>
      </div>
    </div>

    <!-- Resistances -->
    <template v-if="resistances">
      <div class="section-label">Resistances</div>
      <UnitResistanceTable :resistances="resistances" />
    </template>

    <!-- Passives -->
    <template v-if="passives.length">
      <div class="section-label">Passives</div>
      <UnitPassiveList :passives="passives" />
    </template>
  </div>
</template>

<style scoped>
.kp-detail {
  display: flex;
  flex-direction: column;
  gap: var(--sp-2);
  padding: var(--sp-2) 0;
}

.kp-name {
  font-size: var(--fs-lg);
  font-weight: 600;
  color: var(--gold-bright);
  font-family: var(--font-display);
}

.kp-stats {
  display: flex;
  gap: var(--sp-3);
  flex-wrap: wrap;
}

.stat-row {
  display: flex;
  align-items: center;
  gap: var(--sp-1);
}

.stat-icon {
  width: 1.1rem;
  height: 1.1rem;
  object-fit: contain;
  opacity: 0.9;
}

.stat-value {
  font-size: var(--fs-md);
  color: var(--text-1);
  font-family: var(--font-display);
}

.section-label {
  font-size: var(--fs-xs);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-2);
  font-family: var(--font-display);
  margin-top: var(--sp-1);
}
</style>
