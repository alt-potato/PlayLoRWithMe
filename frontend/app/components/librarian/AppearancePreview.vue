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
    The preview is a fixed square (PREVIEW_W × PREVIEW_W).  Face canvas sprites are
    square-canvas, so they fill the box without dead space.  Fashion body composites
    show the head and upper body within the same square crop.  AppearanceCache writes
    dimensions.json with the face canvas pixel size, used only for the head-tilt pivot.

  Props:
    appearance – AppearanceData with sprite IDs and color tuples (0–255 bytes).
    fashionBook – active FashionBook, or null/undefined if none.
-->
<script setup lang="ts">
import type { AppearanceData, FashionBook, GiftSlot } from "~/types/game";

const props = defineProps<{
  appearance: AppearanceData;
  /**
   * When a fashion book is active, it changes the character body model.
   * If replacesHead is true, the face/hair composite is also replaced and
   * the composite layers are hidden.
   */
  fashionBook?: FashionBook | null;
  /**
   * Active body type variant: "F", "M", or "N".  When the fashion book has
   * gendered variants (skinGender != undefined), this selects which body PNG
   * to display (e.g. fashionbodies/123_f.png vs 123_m.png).
   */
  appearanceType?: string;
  /** Equipped gift slots — visible ones are overlaid on the head area. */
  gifts?: (GiftSlot | null)[];
  /**
   * Optional preview size in CSS pixels. Defaults to 160. Allows callers to
   * render larger thumbnails (e.g. roster tiles) without forking the component.
   */
  size?: number;
}>();

const BASE = "/assets/customize/";

/**
 * Preview size in CSS pixels — square so the face canvas fills without empty
 * space. Driven by the optional `size` prop so call sites can request larger
 * thumbnails without altering the rotation/pivot math below.
 */
const PREVIEW_W = computed(() => props.size ?? 160);
const PREVIEW_H = PREVIEW_W;

/**
 * Canvas pixel dimensions from AppearanceCache's dimensions.json.
 * Used only for the rotation transform-origin calculation.
 */
const dims = ref<{ w: number; h: number } | null>(null);

// Module-level cache so multiple instances share one fetch.
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
  return {
    backHair: { src: `${BASE}backhair_${a.backHairID}.png`, tint: hair },
    face: [
      { src: `${BASE}head_${a.headID ?? 0}.png`, tint: skin },
      { src: `${BASE}eyes_${a.eyeID}.png`, tint: eye },
      { src: `${BASE}brows_${a.browID}.png`, tint: hair },
      { src: `${BASE}mouths_${a.mouthID}.png`, tint: skin },
      { src: `${BASE}fronthair_${a.frontHairID}.png`, tint: hair },
    ],
  };
});

/** All layer URLs for 404 probe images. */
const allLayerSrcs = computed(() => [
  layers.value.backHair,
  ...layers.value.face,
]);

// Track which sprite URLs have failed to load so we can hide those layers entirely
// rather than showing a solid-colored rectangle.
const failedSrcs = ref<Set<string>>(new Set());
function markFailed(src: string) {
  failedSrcs.value = new Set([...failedSrcs.value, src]);
}

/**
 * File suffix for gendered fashion book body PNGs.  When the fashion book has
 * skinGender (F or M), the body PNGs are stored as {id}_f.png and {id}_m.png.
 * The variant to display is determined by the active appearanceType.
 */
const fashionVariantSuffix = computed(() => {
  if (!props.fashionBook?.skinGender) return "";
  const at = props.appearanceType?.toLowerCase();
  if (at === "f" || at === "m") return `_${at}`;
  return "_f"; // default to female variant when neutral
});

/**
 * File stem for fashion body PNGs.  Core books use their numeric id; workshop
 * books prefix with packageId to avoid collisions between mods that share the
 * same integer id.  Mirrors AppearanceCache.FashionBookBody.FileStem in C#.
 */
const fashionFileStem = computed(() => {
  if (!props.fashionBook) return null;
  if (props.fashionBook.fileStem) return props.fashionBook.fileStem;
  const pkg = props.fashionBook.packageId;
  return pkg ? `${pkg}_${props.fashionBook.id}` : `${props.fashionBook.id}`;
});

/** URL of the fashion body composite PNG (behind face), or null if inactive. */
const fashionBodyUrl = computed(() =>
  fashionFileStem.value
    ? `/assets/fashionbodies/${fashionFileStem.value}${fashionVariantSuffix.value}.png`
    : null
);

const fashionBodyFailed = ref(false);
/**
 * Natural pixel dimensions of the fashion body PNG once it finishes loading.
 * Used to compute the feet-Y in CSS space so the height scale transform pins
 * the character's feet instead of the preview box's bottom edge.
 */
const fashionBodyDims = ref<{ w: number; h: number } | null>(null);
watch(fashionFileStem, () => {
  fashionBodyFailed.value = false;
  fashionBodyDims.value = null;
});
// also reset when variant changes (different PNG)
watch(fashionVariantSuffix, () => {
  fashionBodyFailed.value = false;
  fashionBodyDims.value = null;
});

/**
 * URL of the fashion front-layer composite PNG (in front of face), or null when
 * the book has no front-layer sprites (hasFrontLayer not set).
 */
const fashionFrontUrl = computed(() =>
  props.fashionBook?.hasFrontLayer
    ? `/assets/fashionbodies_front/${fashionFileStem.value}${fashionVariantSuffix.value}.png`
    : null
);

const fashionFrontFailed = ref(false);
watch(fashionFileStem, () => { fashionFrontFailed.value = false; });

/**
 * URL of the fashion skin-layer composite PNG — exposed body areas (neck, collarbone)
 * that are white silhouettes in the prefab.  Rendered as a CSS multiply-tinted layer
 * using the librarian's skin color, behind the main body composite so body sprites
 * (clothing) cover them naturally.
 */
const fashionSkinUrl = computed(() =>
  fashionFileStem.value
    ? `/assets/fashionbodies/${fashionFileStem.value}${fashionVariantSuffix.value}_skin.png`
    : null
);

const fashionSkinFailed = ref(false);
watch(fashionFileStem, () => { fashionSkinFailed.value = false; });
watch(fashionVariantSuffix, () => { fashionSkinFailed.value = false; });

/**
 * Patron librarians have two composite PNGs extracted from their
 * SpecialCustomizedAppearance prefab:
 *   head_special_{id}.png      — head, face, front hair (above fashion body)
 *   head_special_{id}_rear.png — rear hair (behind fashion body)
 */
const patronFrontUrl = computed(() =>
  props.appearance.patronHeadId
    ? `${BASE}head_special_${props.appearance.patronHeadId}.png`
    : null
);
const patronRearUrl = computed(() =>
  props.appearance.patronHeadId
    ? `${BASE}head_special_${props.appearance.patronHeadId}_rear.png`
    : null
);

const patronFrontFailed = ref(false);
const patronRearFailed = ref(false);
watch(() => props.appearance.patronHeadId, () => {
  patronFrontFailed.value = false;
  patronRearFailed.value = false;
});

/**
 * Whether the fashion book replaces the entire head model.
 * When true, neither generic face/hair layers nor patron composites are shown —
 * the fashion body already includes the head (e.g. Roland's Black Silence book).
 */
const fashionReplacesHead = computed(() =>
  props.fashionBook?.replacesHead === true
);

/** True when the patron composites should be shown (not overridden by a replacesHead book). */
const hasPatronHead = computed(() =>
  patronFrontUrl.value != null
  && !patronFrontFailed.value
  && !fashionReplacesHead.value
);

/**
 * Whether the generic face/hair CSS layers should be shown.
 * Hidden when a patron composite replaces the face, or when the fashion skin
 * replaces the head model (replacesHead = true).
 */
const showFaceHairLayers = computed(() =>
  !hasPatronHead.value && !fashionReplacesHead.value
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
  const previewW = PREVIEW_W.value;
  const canvasCssH = dims.value
    ? previewW * (dims.value.h / dims.value.w)
    : PREVIEW_H.value; // fallback: assume square face canvas

  const originX = previewW * fracX;
  const originY = canvasCssH * fracY;

  return {
    transform: `rotate(${-fb.headTiltDeg}deg)`,
    transformOrigin: `${originX}px ${originY}px`,
  };
});

/**
 * Uniform scale factor applied to all sprite layers, matching the game's own
 * character scaling: `UICharacterRenderer` sets `unitAppearance.localScale` to
 * `Vector2.one * customizeData.height * 0.005`, so height=200 is the 1.0
 * reference and a 170-height librarian renders at 0.85x.
 *
 * The scale is anchored at each body's natural feet position (see `feetYCss`)
 * so that resizing keeps feet planted — matching the in-game behavior where
 * characters stand on a fixed floor and grow upward from it.  Note that feet
 * positions differ between fashion projections because bodies have different
 * natural aspect ratios; a cross-projection common ground would require
 * per-book normalization, which breaks face/body canvas alignment.
 */
const HEIGHT_SCALE_FACTOR = 0.005;
const heightScale = computed(
  () => (props.appearance.height ?? 170) * HEIGHT_SCALE_FACTOR,
);

/**
 * Y coordinate (CSS px) of the character's feet within the preview box, used
 * as the scale transform origin so the feet stay pinned across height changes.
 *
 * Derivation depends on how the body PNG is rendered:
 * - Non-replacesHead bodies share the face canvas width and are drawn with
 *   `background-size: 100% auto; background-position: left top`, so the CSS
 *   height of the body PNG = PREVIEW_W * (naturalH / naturalW).  The body's
 *   feet sit at the bottom of the PNG, i.e. at that same Y (often off-screen
 *   below the preview for full-body bodies — perfectly fine as a transform
 *   origin since CSS allows origins outside the element).
 * - ReplacesHead bodies use `background-size: contain; background-position:
 *   top center`, which fits the whole body inside the PREVIEW_W x PREVIEW_H
 *   box.  A body taller than the box (aspect h/w > 1) fills the height, so
 *   feet land at PREVIEW_H.  A wider body fits by width, feet at
 *   PREVIEW_W * (naturalH / naturalW).
 * - When no body PNG is loaded (face-only librarians), fall back to the
 *   bottom of the preview box so scaling still behaves reasonably.
 */
const feetYCss = computed(() => {
  const previewW = PREVIEW_W.value;
  const previewH = PREVIEW_H.value;
  const bd = fashionBodyDims.value;
  if (!bd || !props.fashionBook) return previewH;

  const aspect = bd.h / bd.w;
  if (fashionReplacesHead.value) {
    // `contain` fits the image fully inside the box while preserving aspect.
    const boxAspect = previewH / previewW;
    return aspect >= boxAspect
      ? previewH // image fills height, feet at preview bottom
      : previewW * aspect; // image fits by width, feet at scaled bottom
  }
  // Non-replacesHead: width pinned to previewW, height scales with aspect.
  return previewW * aspect;
});

/** Z-index per position so overlapping gifts layer in a natural order. */
const POSITION_Z: Record<string, number> = {
  Helmet: 10, Nose: 11, Cheek: 11, Mouth: 11, Eye: 12, Ear: 12,
  Mask: 13, HairAccessory: 14, Hood: 15,
};

/**
 * Equipped visible gifts.  Each gift PNG is rendered onto the same shared
 * canvas as the face/hair sprites, so it layers correctly with the same
 * background-size / background-position CSS — no coordinate conversion needed.
 */
const visibleGifts = computed(() => {
  if (!props.gifts) return [];
  return props.gifts
    .filter((g): g is GiftSlot => g != null && g.visible)
    .map((g) => ({ ...g, z: POSITION_Z[g.position] ?? 11 }));
});
</script>

<template>
  <!--
    White background required for background-blend-mode: multiply to work correctly:
    white × tint-color = tint-color.
  -->
  <div class="preview-box" :style="{ width: `${PREVIEW_W}px`, height: `${PREVIEW_H}px` }">
    <!--
      Hidden <img> probes: detect 404 for each sprite URL. We can't attach @error
      directly to a CSS background-image, so these invisible elements act as sentinels.
    -->
    <img
      v-for="(layer, i) in allLayerSrcs"
      :key="`probe-${i}`"
      :src="layer.src"
      class="probe"
      alt=""
      @error="markFailed(layer.src)"
    />

    <!--
      Height scale wrapper — fills the preview box and applies a uniform scale
      based on the librarian's configured height.  All sprite layers are children
      so they scale together, keeping face/body alignment intact.
    -->
    <div
      class="scale-wrap"
      :style="{
        transform: `scale(${heightScale})`,
        transformOrigin: `${PREVIEW_W / 2}px ${feetYCss}px`,
      }"
    >

    <!--
      Back hair — rendered before the fashion body so it sits behind the body.
      For regular librarians: the generic back hair sprite with hair color tint.
      For patrons: the extracted rear-hair composite (no tint — source art has colors).
      Hidden when the fashion book has a Hood sprite (game hides all back hair).
    -->
    <div
      v-if="!hasPatronHead"
      v-show="showFaceHairLayers && !failedSrcs.has(layers.backHair.src) && !fashionBook?.hidesBackHair"
      class="layer-sprite"
      :style="{
        backgroundImage: `url(${layers.backHair.src})`,
        backgroundColor: layers.backHair.tint,
        maskImage: `url(${layers.backHair.src})`,
        WebkitMaskImage: `url(${layers.backHair.src})`,
        ...faceRotStyle,
      }"
    />
    <div
      v-if="hasPatronHead && patronRearUrl && !patronRearFailed && !fashionBook?.hidesBackHair"
      class="layer-sprite body-layer"
      :style="{ backgroundImage: `url(${patronRearUrl})`, ...faceRotStyle }"
    />
    <img
      v-if="patronRearUrl"
      :src="patronRearUrl"
      class="probe"
      alt=""
      @error="patronRearFailed = true"
    />

    <!--
      Fashion body skin layer: exposed skin areas (neck, collarbone) that are white
      silhouettes in the character model.  Tinted with the librarian's skin color via
      CSS multiply blend.  Rendered behind the main body composite so clothing covers
      the skin naturally.
    -->
    <div
      v-if="fashionBook && fashionSkinUrl && !fashionSkinFailed"
      class="layer-sprite"
      :style="{
        backgroundImage: `url(${fashionSkinUrl})`,
        backgroundColor: toRgb(appearance.skinColor),
        maskImage: `url(${fashionSkinUrl})`,
        WebkitMaskImage: `url(${fashionSkinUrl})`,
      }"
    />
    <img
      v-if="fashionSkinUrl"
      :src="fashionSkinUrl"
      class="probe"
      alt=""
      @error="fashionSkinFailed = true"
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
      :class="{ 'body-layer--replaces-head': fashionReplacesHead }"
      :style="{ backgroundImage: `url(${fashionBodyUrl})` }"
    />
    <img
      v-if="fashionBook && fashionBodyUrl"
      :src="fashionBodyUrl"
      class="probe"
      alt=""
      @error="fashionBodyFailed = true"
      @load="(e) => {
        const el = e.target as HTMLImageElement;
        fashionBodyDims = { w: el.naturalWidth, h: el.naturalHeight };
      }"
    />

    <!--
      Patron front composite: head, face, and front hair for patron librarians.
      Rendered above the fashion body, replacing the generic face/hair layers.
    -->
    <div
      v-if="hasPatronHead && patronFrontUrl"
      class="layer-sprite body-layer"
      :style="{ backgroundImage: `url(${patronFrontUrl})`, ...faceRotStyle }"
    />
    <img
      v-if="patronFrontUrl"
      :src="patronFrontUrl"
      class="probe"
      alt=""
      @error="patronFrontFailed = true"
    />

    <!--
      Remaining face/hair layers (head, eyes, brows, mouth, fronthair) rendered on top
      of the fashion body so the librarian's face shows through the body composite.
      Hidden when a patron head composite is active.
    -->
    <div
      v-for="(layer, i) in layers.face"
      :key="`face-${i}`"
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

    <!--
      Gift sprite overlays — each PNG is rendered onto the same shared canvas as
      face/hair sprites, so they use the same CSS stacking (inset: 0, 100% auto).
    -->
    <div
      v-for="gift in visibleGifts"
      :key="`gift-${gift.id}`"
      v-show="showFaceHairLayers"
      class="layer-sprite gift-layer"
      :style="{
        backgroundImage: `url(/assets/gifts/gift_${gift.id}.png)`,
        zIndex: gift.z,
        ...faceRotStyle,
      }"
    />
    </div>
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

/*
 * Height scale wrapper — fills the preview box; its inline `transform: scale()`
 * is driven by the librarian's height and anchored at the body's natural feet
 * position so resizing keeps feet planted while growing the character upward,
 * mirroring the in-game fixed-camera view.
 */
.scale-wrap {
  position: absolute;
  inset: 0;
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

/*
 * replacesHead books render the body alone (no face/hair layers to align with),
 * and the PNG is extracted at a tight aspect ratio matching the character's own
 * bounds.  `contain` fits the full body into the square preview box — a full-body
 * sprite taller than it is wide ends up fitting by height, centered horizontally,
 * with the head at the top of the preview and the feet at the bottom.
 */
.body-layer.body-layer--replaces-head {
  background-size: contain;
  background-position: top center;
}

/* Gift overlays — same canvas-positioned PNGs as face layers, no tint. */
.gift-layer {
  background-blend-mode: normal;
  mask-image: none;
  -webkit-mask-image: none;
  pointer-events: none;
}
</style>
