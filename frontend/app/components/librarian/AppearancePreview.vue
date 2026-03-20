<!--
  AppearancePreview.vue

  CSS-layered composite sprite preview for a librarian's customized appearance.

  Layer order (bottom to top): back hair → head/body → eyes → eyebrows → mouth → front hair

  Color tinting technique:
    Each layer is a single <div> using:
      - `background-image`: the sprite
      - `background-color`: the tint color
      - `background-blend-mode: multiply`: tints opaque sprite pixels
      - `mask-image`: the same sprite URL — clips the element's rendered output
        to only the sprite's opaque pixels, so transparent areas stay transparent
        and lower layers show through correctly.

    Both `background-position` and `mask-position` use the same value on the same
    element, so there is no sub-pixel alignment difference between image and mask —
    this is what caused the 1px fringe with the separate overlay approach.

  Props:
    appearance – AppearanceData with sprite IDs and color tuples (0–255 bytes).
-->
<script setup lang="ts">
import type { AppearanceData } from "~/types/game";

const props = defineProps<{ appearance: AppearanceData }>();

const BASE = "/assets/customize/";

/** Convert a [r, g, b] byte tuple (0–255) to a CSS rgb() string. */
function toRgb(c: [number, number, number]): string {
  return `rgb(${c[0]}, ${c[1]}, ${c[2]})`;
}

/**
 * Ordered sprite layers with associated tint colors.
 * Back hair renders first so it sits behind the head; front hair renders last.
 */
const layers = computed(() => {
  const a = props.appearance;
  const hair = toRgb(a.hairColor);
  const skin = toRgb(a.skinColor);
  const eye = toRgb(a.eyeColor);
  return [
    { src: `${BASE}backhair_${a.backHairID}.png`, tint: hair },
    { src: `${BASE}head_${a.headID}.png`, tint: skin },
    { src: `${BASE}eyes_${a.eyeID}.png`, tint: eye },
    { src: `${BASE}brows_${a.browID}.png`, tint: hair },
    { src: `${BASE}mouths_${a.mouthID}.png`, tint: skin },
    { src: `${BASE}fronthair_${a.frontHairID}.png`, tint: hair },
  ];
});

// Track which sprite URLs have failed to load so we can hide those layers entirely
// rather than showing a solid-colored rectangle.
const failedSrcs = ref<Set<string>>(new Set());
function markFailed(src: string) {
  failedSrcs.value = new Set([...failedSrcs.value, src]);
}
</script>

<template>
  <!--
    White background required for background-blend-mode: multiply to work correctly:
    white × tint-color = tint-color.
  -->
  <div class="preview-box">
    <!--
      Hidden <img> probes: detect 404 for each sprite URL. We can't attach @error
      directly to a CSS background-image, so these invisible elements act as sentinels.
    -->
    <img
      v-for="(layer, i) in layers"
      :key="`probe-${i}`"
      :src="layer.src"
      class="probe"
      alt=""
      @error="markFailed(layer.src)"
    />

    <!--
      Visual layers: background-image + background-color blended with multiply,
      masked to the sprite's opaque pixel shape.
    -->
    <div
      v-for="(layer, i) in layers"
      :key="`layer-${i}`"
      v-show="!failedSrcs.has(layer.src)"
      class="layer-sprite"
      :style="{
        backgroundImage: `url(${layer.src})`,
        backgroundColor: layer.tint,
        maskImage: `url(${layer.src})`,
        WebkitMaskImage: `url(${layer.src})`,
      }"
    />
  </div>
</template>

<style scoped>
.preview-box {
  position: relative;
  width: 120px;
  height: 200px;
  background: #fff;
  border-radius: 4px;
  overflow: hidden;
  flex-shrink: 0;
}

/* Hidden sentinel images used only for 404 detection. */
.probe {
  display: none;
}

.layer-sprite {
  position: absolute;
  inset: 0;
  background-size: contain;
  background-repeat: no-repeat;
  /* Align to bottom so the character stands on the bottom edge of the preview box. */
  background-position: center bottom;
  background-blend-mode: multiply;
  /* Clip to the sprite's opaque pixels — must match background sizing exactly. */
  mask-size: contain;
  mask-repeat: no-repeat;
  mask-position: center bottom;
  -webkit-mask-size: contain;
  -webkit-mask-repeat: no-repeat;
  -webkit-mask-position: center bottom;
}
</style>
