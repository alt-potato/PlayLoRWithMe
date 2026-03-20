<!--
  HairstyleTab.vue

  Hairstyle sub-tab inside CustomizePanel.
  Controls front hair (bangs), back hair, and hair color via preset swatches.

  Hair color values are byte integers (0–255) matching the Color32 format
  serialized by the C# backend.
-->
<script setup lang="ts">
const FRONT_HAIR_MAX = 42;
const BACK_HAIR_MAX = 22;

/** Preset hair colors from the game's HairColorTable, as Color32 bytes (0–255). */
const HAIR_COLORS: Array<{ label: string; color: [number, number, number] }> = [
  { label: "Black",      color: [13, 13, 13] },
  { label: "Dark Brown", color: [51, 26, 13] },
  { label: "Brown",      color: [115, 64, 26] },
  { label: "Gray",       color: [128, 128, 128] },
  { label: "White",      color: [242, 242, 242] },
  { label: "Red",        color: [179, 26, 26] },
  { label: "Blue",       color: [26, 51, 179] },
  { label: "Cyan",       color: [26, 179, 179] },
  { label: "Blonde",     color: [217, 191, 77] },
];

const props = defineProps<{
  frontHairID: number;
  backHairID: number;
  hairColor: [number, number, number];
  busy: boolean;
}>();

const emit = defineEmits<{
  "update:frontHairID": [value: number];
  "update:backHairID": [value: number];
  "update:hairColor": [value: [number, number, number]];
}>();

function step(field: "frontHairID" | "backHairID", delta: number): void {
  const max = field === "frontHairID" ? FRONT_HAIR_MAX : BACK_HAIR_MAX;
  const cur = props[field];
  const next = ((cur + delta) % (max + 1) + (max + 1)) % (max + 1);
  if (field === "frontHairID") emit("update:frontHairID", next);
  else emit("update:backHairID", next);
}

/** Check if a swatch color matches the current hair color. */
function isColorActive(c: [number, number, number]): boolean {
  return (
    c[0] === props.hairColor[0] &&
    c[1] === props.hairColor[1] &&
    c[2] === props.hairColor[2]
  );
}

function swatchStyle(c: [number, number, number]) {
  return { background: `rgb(${c[0]}, ${c[1]}, ${c[2]})` };
}
</script>

<template>
  <div class="tab-inner">
    <div class="section-label">Bangs (Front Hair)</div>
    <div class="stepper-row">
      <button class="step-btn" :disabled="busy" @click="step('frontHairID', -1)">◀</button>
      <span class="step-value">{{ frontHairID }}</span>
      <button class="step-btn" :disabled="busy" @click="step('frontHairID', 1)">▶</button>
      <span class="step-range">/ {{ FRONT_HAIR_MAX }}</span>
    </div>

    <div class="section-label" style="margin-top: 0.75rem;">Backside Hair</div>
    <div class="stepper-row">
      <button class="step-btn" :disabled="busy" @click="step('backHairID', -1)">◀</button>
      <span class="step-value">{{ backHairID }}</span>
      <button class="step-btn" :disabled="busy" @click="step('backHairID', 1)">▶</button>
      <span class="step-range">/ {{ BACK_HAIR_MAX }}</span>
    </div>

    <div class="section-label" style="margin-top: 0.75rem;">Hair Color</div>
    <div class="swatch-row">
      <button
        v-for="preset in HAIR_COLORS"
        :key="preset.label"
        class="swatch"
        :class="{ active: isColorActive(preset.color) }"
        :style="swatchStyle(preset.color)"
        :title="preset.label"
        :disabled="busy"
        @click="emit('update:hairColor', preset.color)"
      />
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

.swatch-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}

.swatch {
  width: 1.6rem;
  height: 1.6rem;
  border-radius: 50%;
  border: 2px solid transparent;
  cursor: pointer;
  transition: transform 0.1s, border-color 0.1s;
  outline: none;
}

.swatch:hover:not(:disabled) {
  transform: scale(1.15);
}

.swatch.active {
  border-color: var(--gold);
  transform: scale(1.15);
}

.swatch:disabled {
  opacity: 0.4;
  cursor: default;
}
</style>
