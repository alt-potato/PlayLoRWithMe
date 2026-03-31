<!--
  FaceTab.vue

  Face & Expressions sub-tab inside CustomizePanel.
  Controls eye, eyebrow, and mouth sprite IDs, plus skin and eye color swatches.

  Color values are byte integers (0–255) matching the Color32 format
  serialized by the C# backend.
-->
<script setup lang="ts">
const EYE_MAX = 31;
const BROW_MAX = 13;
const MOUTH_MAX = 13;

const props = defineProps<{
  eyeID: number;
  browID: number;
  mouthID: number;
  skinColor: [number, number, number];
  eyeColor: [number, number, number];
  busy: boolean;
}>();

const emit = defineEmits<{
  "update:eyeID": [value: number];
  "update:browID": [value: number];
  "update:mouthID": [value: number];
  "update:skinColor": [value: [number, number, number]];
  "update:eyeColor": [value: [number, number, number]];
}>();

type StepField = "eyeID" | "browID" | "mouthID";

function step(field: StepField, delta: number): void {
  const max = field === "eyeID" ? EYE_MAX : MOUTH_MAX;
  const cur = props[field];
  const next = ((cur + delta) % (max + 1) + (max + 1)) % (max + 1);
  if (field === "eyeID") emit("update:eyeID", next);
  else if (field === "browID") emit("update:browID", next);
  else emit("update:mouthID", next);
}

</script>

<template>
  <div class="tab-inner">
    <div class="section-label">Eyes</div>
    <div class="stepper-row">
      <button class="step-btn" :disabled="busy" @click="step('eyeID', -1)">◀</button>
      <span class="step-value">{{ eyeID }}</span>
      <button class="step-btn" :disabled="busy" @click="step('eyeID', 1)">▶</button>
      <span class="step-range">/ {{ EYE_MAX }}</span>
    </div>

    <div class="section-label" style="margin-top: 0.75rem;">Eyebrows</div>
    <div class="stepper-row">
      <button class="step-btn" :disabled="busy" @click="step('browID', -1)">◀</button>
      <span class="step-value">{{ browID }}</span>
      <button class="step-btn" :disabled="busy" @click="step('browID', 1)">▶</button>
      <span class="step-range">/ {{ BROW_MAX }}</span>
    </div>

    <div class="section-label" style="margin-top: 0.75rem;">Mouth</div>
    <div class="stepper-row">
      <button class="step-btn" :disabled="busy" @click="step('mouthID', -1)">◀</button>
      <span class="step-value">{{ mouthID }}</span>
      <button class="step-btn" :disabled="busy" @click="step('mouthID', 1)">▶</button>
      <span class="step-range">/ {{ MOUTH_MAX }}</span>
    </div>

    <div class="section-label" style="margin-top: 0.75rem;">Skin Color</div>
    <LibrarianCustomizeHslColorPicker
      :model-value="skinColor"
      :disabled="busy"
      @update:model-value="emit('update:skinColor', $event)"
    />
    <div class="section-label" style="margin-top: 0.75rem;">Eye Color</div>
    <LibrarianCustomizeHslColorPicker
      :model-value="eyeColor"
      :disabled="busy"
      @update:model-value="emit('update:eyeColor', $event)"
    />
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

.stepper-row {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.step-btn {
  width: 2rem;
  height: 2rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: var(--bg-card-2);
  color: var(--text-1);
  font-size: 0.75rem;
  cursor: pointer;
  transition: border-color 0.1s, color 0.1s;
}

.step-btn:hover:not(:disabled) {
  border-color: var(--gold-dim);
  color: var(--gold);
}

.step-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.step-value {
  min-width: 2rem;
  text-align: center;
  font-size: 0.85rem;
  color: var(--text-1);
  font-family: var(--font-display);
}

.step-range {
  font-size: 0.6rem;
  color: var(--text-3);
}
</style>
