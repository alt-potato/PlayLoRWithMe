<!--
  HslColorPicker.vue

  Reusable HSL color picker that accepts and emits an RGB byte tuple [r, g, b] (0–255).
  Internally stores hue (0–360), saturation (0–100), and lightness (0–100) so users
  interact with a perceptually intuitive space.

  Each slider track is a reactive CSS gradient so users can immediately see what
  dimension they are adjusting.  The track gradient is applied to a wrapper <div>
  rather than directly to the <input> element, which avoids cross-browser quirks
  with ::-webkit-slider-runnable-track and ::-moz-range-track vendor prefixes.

  Drift prevention: internal HSL state is only re-initialised from the incoming prop
  when the prop value differs from the last value this component emitted.  This
  prevents rgbToHsl→hslToRgb rounding from oscillating on every reactive cycle.
-->
<script setup lang="ts">
import { rgbToHsl, hslToRgb } from "~/utils/color";

const props = defineProps<{
  modelValue: [number, number, number];
  disabled?: boolean;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: [number, number, number]];
}>();

// Internal HSL state
const h = ref(0);
const s = ref(0);
const l = ref(0);

// Track the last RGB value this component emitted so we can distinguish
// external prop updates from our own round-tripped values.
let lastEmitted: [number, number, number] = [...props.modelValue] as [number, number, number];

function syncFromRgb(rgb: [number, number, number]) {
  [h.value, s.value, l.value] = rgbToHsl(rgb[0], rgb[1], rgb[2]);
}

syncFromRgb(props.modelValue);

watch(
  () => props.modelValue,
  (next) => {
    if (
      next[0] !== lastEmitted[0] ||
      next[1] !== lastEmitted[1] ||
      next[2] !== lastEmitted[2]
    ) {
      syncFromRgb(next);
      lastEmitted = [...next] as [number, number, number];
    }
  },
);

function emitChange() {
  const rgb = hslToRgb(h.value, s.value, l.value);
  lastEmitted = rgb;
  emit("update:modelValue", rgb);
}

// CSS gradient strings for each slider track — update reactively as values change.
const hueGradient = computed(
  () =>
    "linear-gradient(to right," +
    " hsl(0,100%,50%), hsl(30,100%,50%), hsl(60,100%,50%)," +
    " hsl(90,100%,50%), hsl(120,100%,50%), hsl(150,100%,50%)," +
    " hsl(180,100%,50%), hsl(210,100%,50%), hsl(240,100%,50%)," +
    " hsl(270,100%,50%), hsl(300,100%,50%), hsl(330,100%,50%)," +
    " hsl(360,100%,50%))",
);

const satGradient = computed(
  () =>
    `linear-gradient(to right, hsl(${h.value},0%,${l.value}%), hsl(${h.value},100%,${l.value}%))`,
);

const litGradient = computed(
  () =>
    `linear-gradient(to right, hsl(${h.value},${s.value}%,0%), hsl(${h.value},${s.value}%,50%), hsl(${h.value},${s.value}%,100%))`,
);

const previewColor = computed(
  () => `hsl(${h.value}, ${s.value}%, ${l.value}%)`,
);
</script>

<template>
  <div class="hsl-picker" :class="{ disabled }">
    <div class="picker-body">
      <!-- Live color preview -->
      <div class="color-preview" :style="{ background: previewColor }" />

      <!-- Sliders -->
      <div class="sliders">
        <!-- Hue -->
        <div class="slider-row">
          <span class="slider-label">H</span>
          <div class="track-wrap" :style="{ background: hueGradient }">
            <input
              type="range"
              min="0"
              max="360"
              :value="h"
              :disabled="disabled"
              @input="h = Number(($event.target as HTMLInputElement).value); emitChange()"
            />
          </div>
          <span class="slider-val">{{ h }}</span>
        </div>

        <!-- Saturation -->
        <div class="slider-row">
          <span class="slider-label">S</span>
          <div class="track-wrap" :style="{ background: satGradient }">
            <input
              type="range"
              min="0"
              max="100"
              :value="s"
              :disabled="disabled"
              @input="s = Number(($event.target as HTMLInputElement).value); emitChange()"
            />
          </div>
          <span class="slider-val">{{ s }}</span>
        </div>

        <!-- Lightness -->
        <div class="slider-row">
          <span class="slider-label">L</span>
          <div class="track-wrap" :style="{ background: litGradient }">
            <input
              type="range"
              min="0"
              max="100"
              :value="l"
              :disabled="disabled"
              @input="l = Number(($event.target as HTMLInputElement).value); emitChange()"
            />
          </div>
          <span class="slider-val">{{ l }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.hsl-picker {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.picker-body {
  display: flex;
  align-items: center;
  gap: 0.6rem;
}

/* Square swatch showing the currently selected color. */
.color-preview {
  flex-shrink: 0;
  width: 2.4rem;
  height: 2.4rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
}

.sliders {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.slider-row {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.slider-label {
  font-size: 0.6rem;
  color: var(--text-3);
  width: 0.8rem;
  flex-shrink: 0;
  text-align: right;
}

.slider-val {
  font-size: 0.6rem;
  color: var(--text-3);
  width: 1.8rem;
  flex-shrink: 0;
  text-align: right;
  font-variant-numeric: tabular-nums;
}

/*
 * The gradient track: a rounded pill that acts as the visual track.
 * The <input> overlays it fully with a transparent background so only
 * the thumb is visible on top of the gradient.
 */
.track-wrap {
  position: relative;
  flex: 1;
  height: 0.75rem;
  border-radius: 999px;
  overflow: visible;
}

.track-wrap input[type="range"] {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  margin: 0;
  padding: 0;
  cursor: pointer;
  appearance: none;
  -webkit-appearance: none;
  background: transparent;
  border: none;
  outline: none;
}

/* Webkit track — transparent so the wrapper gradient shows through */
.track-wrap input[type="range"]::-webkit-slider-runnable-track {
  height: 100%;
  background: transparent;
}

.track-wrap input[type="range"]::-webkit-slider-thumb {
  -webkit-appearance: none;
  width: 1rem;
  height: 1rem;
  border-radius: 50%;
  background: #fff;
  border: 2px solid rgba(0, 0, 0, 0.35);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.4);
  cursor: pointer;
  margin-top: -0.125rem;
}

/* Firefox track */
.track-wrap input[type="range"]::-moz-range-track {
  height: 100%;
  background: transparent;
  border: none;
}

.track-wrap input[type="range"]::-moz-range-thumb {
  width: 1rem;
  height: 1rem;
  border-radius: 50%;
  background: #fff;
  border: 2px solid rgba(0, 0, 0, 0.35);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.4);
  cursor: pointer;
}

.track-wrap input[type="range"]:disabled {
  cursor: default;
  opacity: 0.4;
}

.hsl-picker.disabled {
  opacity: 0.4;
  pointer-events: none;
}
</style>
