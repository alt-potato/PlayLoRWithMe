<!--
  AppearancePreview.vue

  CSS-layered composite sprite preview for a librarian's customized appearance.

  Layer order (bottom to top): back hair → head/body → eyes → eyebrows → mouth → front hair

  When a fashion projection (custom core book) is active, a body composite PNG extracted
  at runtime replaces the generic librarian body layer.  The face/hair CSS layers are shown
  on top unless the skin replaces the head model entirely (replacesHead = true).

  Color tinting technique:
    Each layer is a single <div> using:
      - `background-image`: the sprite
      - `background-color`: the tint color
      - `background-blend-mode: multiply`: tints opaque sprite pixels
      - `mask-image`: the same sprite URL — clips the element's rendered output
        to only the sprite's opaque pixels, so transparent areas stay transparent
        and lower layers show through correctly.

    All layers use `background-size: 100% auto` so they scale to exactly the
    container width regardless of their individual pixel heights.  Since all PNGs
    share the same canvas pixel width (written by AppearanceCache), the scale factor
    is identical across layers and face/body composites stay aligned.

  Preview box sizing:
    AppearanceCache writes wwwroot/assets/customize/dimensions.json with the shared
    canvas pixel dimensions { w, h }.  On mount we fetch this to compute a preview
    height that matches the canvas aspect ratio, avoiding both an over-tall empty box
    and a too-short clipped preview.  A sensible fallback is used until the fetch
    resolves.

  Props:
    appearance – AppearanceData with sprite IDs and color tuples (0–255 bytes).
    fashionBook – active FashionBook, or null/undefined if none.
-->
<script setup lang="ts">
import type { AppearanceData, FashionBook } from "~/types/game";

const props = defineProps<{
  appearance: AppearanceData;
  /**
   * When a fashion book is active, it changes the character body model.
   * If replacesHead is true, the face/hair composite is also replaced and
   * the composite layers are hidden.
   */
  fashionBook?: FashionBook | null;
}>();

const BASE = "/assets/customize/";

/** Preview width in CSS pixels — all calculations derive from this constant. */
const PREVIEW_W = 160;

/**
 * Canvas pixel dimensions from AppearanceCache's dimensions.json.
 * Used to compute the preview height and the rotation transform-origin.
 */
const dims = ref<{ w: number; h: number } | null>(null);

/**
 * Preview height derived from the canvas aspect ratio.  The face canvas defines
 * the top portion; body composites extend below by roughly 1.5× the face height,
 * so we multiply by 2.5 and clamp to a sensible range.
 */
const previewH = computed(() => {
  if (!dims.value) return 260;
  const scale = PREVIEW_W / dims.value.w;
  return Math.max(180, Math.min(380, Math.ceil(dims.value.h * scale * 2.5)));
});

// Module-level cache so multiple instances share one fetch.
let _dimsFetched = false;
let _dimsPromise: Promise<{ w: number; h: number } | null> | null = null;

function fetchDims(): Promise<{ w: number; h: number } | null> {
  if (_dimsPromise) return _dimsPromise;
  _dimsPromise = fetch("/assets/customize/dimensions.json")
    .then((r) => r.json() as Promise<{ w: number; h: number }>)
    .catch(() => null);
  return _dimsPromise;
}

onMounted(async () => {
  dims.value = await fetchDims();
});

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

/** URL of the fashion body composite PNG (behind face), or null if inactive. */
const fashionBodyUrl = computed(() =>
  props.fashionBook ? `/assets/fashionbodies/${props.fashionBook.id}.png` : null
);

const fashionBodyFailed = ref(false);
watch(() => props.fashionBook?.id, () => { fashionBodyFailed.value = false; });

/**
 * URL of the fashion front-layer composite PNG (in front of face), or null when
 * the book has no front-layer sprites (hasFrontLayer not set).
 */
const fashionFrontUrl = computed(() =>
  props.fashionBook?.hasFrontLayer
    ? `/assets/fashionbodies_front/${props.fashionBook.id}.png`
    : null
);

const fashionFrontFailed = ref(false);
watch(() => props.fashionBook?.id, () => { fashionFrontFailed.value = false; });

/**
 * Whether the face/hair CSS layers should be shown on top of the fashion body.
 * Hidden when the fashion skin replaces the head model (replacesHead = true)
 * because the extracted body already includes the head.
 */
const showFaceHairLayers = computed(() =>
  !props.fashionBook || !props.fashionBook.replacesHead
);

/**
 * CSS transform applied to face/hair layers when the active fashion book has a
 * non-zero head tilt.  The origin is set to the canonical librarian pivot so the
 * rotation matches what the game shows.
 *
 * Unity's left-hand screen space means a positive eulerAngles.z value is
 * counter-clockwise on screen, opposite to CSS rotate(+deg), so the angle is negated.
 */
const faceRotStyle = computed(() => {
  const fb = props.fashionBook;
  if (!fb || !fb.headTiltDeg || Math.abs(fb.headTiltDeg) < 0.05) return {};

  const fracX = fb.pivotFracX ?? 0.5;
  const fracY = fb.pivotFracY ?? 0.5;
  // CSS canvas height at PREVIEW_W: scale = PREVIEW_W / dims.w, height = dims.h * scale.
  const canvasCssH = dims.value
    ? PREVIEW_W * (dims.value.h / dims.value.w)
    : previewH.value * 0.4; // rough fallback: face canvas ≈ 40% of previewH

  const originX = PREVIEW_W * fracX;
  const originY = canvasCssH * fracY;

  return {
    transform: `rotate(${-fb.headTiltDeg}deg)`,
    transformOrigin: `${originX}px ${originY}px`,
  };
});
</script>

<template>
  <!--
    White background required for background-blend-mode: multiply to work correctly:
    white × tint-color = tint-color.
  -->
  <div class="preview-box" :style="{ width: `${PREVIEW_W}px`, height: `${previewH}px` }">
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
      Back hair (layers[0]) rendered before the fashion body so it sits behind the body.
      Hidden when the fashion book has a Hood sprite — the game hides all back hair
      renderers unconditionally in that case (RefreshAppearanceByMotion).
    -->
    <div
      v-show="showFaceHairLayers && !failedSrcs.has(layers[0].src) && !fashionBook?.hidesBackHair"
      class="layer-sprite"
      :style="{
        backgroundImage: `url(${layers[0].src})`,
        backgroundColor: layers[0].tint,
        maskImage: `url(${layers[0].src})`,
        WebkitMaskImage: `url(${layers[0].src})`,
        ...faceRotStyle,
      }"
    />

    <!--
      Fashion body composite: replaces the generic body when a fashion skin is active.
      Rendered as a plain background-image (no tinting) since body sprites already carry
      their own colors from the source art.  Falls back to face/hair-only display if the
      composite hasn't been extracted yet (fashionBodyFailed).
    -->
    <div
      v-if="fashionBook && fashionBodyUrl && !fashionBodyFailed"
      class="layer-sprite body-layer"
      :style="{ backgroundImage: `url(${fashionBodyUrl})` }"
    />
    <img
      v-if="fashionBook && fashionBodyUrl"
      :src="fashionBodyUrl"
      class="probe"
      alt=""
      @error="fashionBodyFailed = true"
    />

    <!--
      Remaining face/hair layers (head, eyes, brows, mouth, fronthair) rendered on top
      of the fashion body so the librarian's face shows through the body composite.
    -->
    <div
      v-for="(layer, i) in layers.slice(1)"
      :key="`layer-${i + 1}`"
      v-show="showFaceHairLayers && !failedSrcs.has(layer.src)"
      class="layer-sprite"
      :style="{
        backgroundImage: `url(${layer.src})`,
        backgroundColor: layer.tint,
        maskImage: `url(${layer.src})`,
        WebkitMaskImage: `url(${layer.src})`,
        ...faceRotStyle,
      }"
    />

    <!--
      Fashion front layer: body sprites whose sortingOrder was at or above the face
      overlay threshold in-game (e.g. ribbons, collars, hats that sit in front of the
      face).  Rendered above all face/hair layers.
    -->
    <div
      v-if="fashionBook && fashionFrontUrl && !fashionFrontFailed"
      class="layer-sprite body-layer"
      :style="{ backgroundImage: `url(${fashionFrontUrl})` }"
    />
    <img
      v-if="fashionBook && fashionFrontUrl"
      :src="fashionFrontUrl"
      class="probe"
      alt=""
      @error="fashionFrontFailed = true"
    />
  </div>
</template>

<style scoped>
.preview-box {
  position: relative;
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
  /*
   * 100% auto: scales the PNG to exactly fill the container width, with height
   * proportional.  All PNGs share the same canvas pixel width, so all layers
   * use the same scale factor — no centering offset differences between layers
   * of different heights (which broke alignment with `contain`).
   */
  background-size: 100% auto;
  background-repeat: no-repeat;
  /* Top-align so the character's head is always at the top of the preview. */
  background-position: left top;
  background-blend-mode: multiply;
  /* Clip to the sprite's opaque pixels — must match background sizing exactly. */
  mask-size: 100% auto;
  mask-repeat: no-repeat;
  mask-position: left top;
  -webkit-mask-size: 100% auto;
  -webkit-mask-repeat: no-repeat;
  -webkit-mask-position: left top;
}

/* Body composite has no tint, so no blend mode or mask needed. */
.body-layer {
  background-blend-mode: normal;
  mask-image: none;
  -webkit-mask-image: none;
}
</style>
