<!--
  ProjectionTab.vue

  Projection (body appearance) sub-tab inside CustomizePanel.
  Controls body type (headID: 0 = Type A, 1 = Type B) and body height.
  X page fashion skin is stubbed as "coming soon" — requires additional backend
  investigation into WorkshopAppearanceInfo persistence before it can be implemented.
-->
<script setup lang="ts">
const HEIGHT_MIN = 140;
const HEIGHT_MAX = 220;

const props = defineProps<{
  headID: number;
  height: number;
  busy: boolean;
}>();

const emit = defineEmits<{
  "update:headID": [value: number];
  "update:height": [value: number];
}>();

function onHeightInput(e: Event): void {
  const raw = Number((e.target as HTMLInputElement).value);
  if (!isNaN(raw)) {
    emit("update:height", Math.min(HEIGHT_MAX, Math.max(HEIGHT_MIN, raw)));
  }
}
</script>

<template>
  <div class="tab-inner">
    <div class="section-label">Body Type</div>
    <div class="type-row">
      <button
        class="type-btn"
        :class="{ active: headID === 0 }"
        :disabled="busy"
        @click="emit('update:headID', 0)"
      >
        Type A
      </button>
      <button
        class="type-btn"
        :class="{ active: headID === 1 }"
        :disabled="busy"
        @click="emit('update:headID', 1)"
      >
        Type B
      </button>
    </div>

    <div class="section-label" style="margin-top: 0.75rem;">
      Body Size <span class="range-hint">({{ HEIGHT_MIN }}–{{ HEIGHT_MAX }})</span>
    </div>
    <div class="height-row">
      <input
        :value="height"
        type="number"
        class="height-input"
        :min="HEIGHT_MIN"
        :max="HEIGHT_MAX"
        :disabled="busy"
        @input="onHeightInput"
      />
      <span class="height-unit">cm</span>
    </div>

    <div class="section-label" style="margin-top: 0.75rem;">X Page Fashion</div>
    <div class="coming-soon">
      Coming soon — equipping a key page's character skin is not yet supported.
    </div>
  </div>
</template>

<style scoped>
.tab-inner {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.section-label {
  font-size: 0.6rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-3);
}

.range-hint {
  font-size: 0.55rem;
  color: var(--text-3);
  text-transform: none;
  letter-spacing: 0;
}

.type-row {
  display: flex;
  gap: 0.4rem;
}

.type-btn {
  padding: 0.35rem 0.8rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: var(--bg-card-2);
  color: var(--text-2);
  font-size: 0.75rem;
  cursor: pointer;
  transition: color 0.1s, border-color 0.1s, background 0.1s;
}

.type-btn:hover:not(:disabled) {
  border-color: var(--gold-dim);
  color: var(--gold);
}

.type-btn.active {
  border-color: var(--gold);
  color: var(--gold);
  background: rgba(201, 162, 39, 0.08);
}

.type-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.height-row {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.height-input {
  width: 5rem;
  padding: 0.35rem 0.5rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: var(--bg);
  color: var(--text-1);
  font-size: 0.8rem;
  text-align: right;
}

.height-unit {
  font-size: 0.7rem;
  color: var(--text-2);
}

.coming-soon {
  padding: 0.5rem 0.75rem;
  border: 1px dashed var(--border);
  border-radius: 4px;
  font-size: 0.7rem;
  color: var(--text-3);
  font-style: italic;
}
</style>
