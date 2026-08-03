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
    show the head and upper body within the same square crop.  The face canvas pixel
    size is injected via FACE_CANVAS_DIMS (sourced from customizeOptions) and is used
    only for the head-tilt pivot.

  Props:
    appearance – AppearanceData with sprite IDs and color tuples (0–255 bytes).
    fashionBook – active FashionBook, or null/undefined if none.
-->
<script setup lang="ts">
import type { Ref } from "vue";
import type { AppearanceData, FashionBook, GiftSlot } from "~/types/game";
import { ASSETS_READY } from "~/composables/useAssetsReady";
import { FACE_CANVAS_DIMS } from "~/composables/useFaceCanvasDims";
import {
  computeFaceRotStyle,
  computeFashionBodyHeightCss,
  computeFeetYCss,
  computeFloorY,
  computeHeightScale,
  computeZoomCompensateY,
} from "~/utils/appearanceGeometry";

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
  /**
   * Extra scale factor layered on top of the height-driven scale.  Makes the
   * character appear larger inside the same-sized viewport so the preview
   * reads as a "portrait" rather than a tiny silhouette in dead space.  The
   * scale-wrap also translates downward to keep the head visible — feet are
   * intentionally pushed below the viewport so the head's natural top
   * position is preserved as zoom grows.  Defaults to `DEFAULT_ZOOM`.
   */
  zoom?: number;
}>();

/**
 * Token bumped when assetsReady transitions to true, used to mint a
 * cache-bust query for URLs that previously 404'd. The bust is applied
 * **only to URLs in `failedSrcs`** — successful sprites keep their natural
 * cache entry so the assets-ready transition doesn't force the browser to
 * re-fetch every layer of every visible librarian preview.
 */
const bustToken = ref(0);

const BASE = "/assets/customize/";

/** Append a cache-bust query iff this URL previously 404'd. */
function withBust(url: string): string {
  return bustToken.value && failedSrcs.value.has(url)
    ? `${url}?_=${bustToken.value}`
    : url;
}

/**
 * Preview size in CSS pixels — square so the face canvas fills without empty
 * space. Driven by the optional `size` prop so call sites can request larger
 * thumbnails without altering the rotation/pivot math below.
 */
const PREVIEW_W = computed(() => props.size ?? 160);
const PREVIEW_H = PREVIEW_W;

/**
 * Shared face/hair canvas pixel dimensions, injected from app.vue and
 * sourced from `gameState.customizeOptions.faceCanvasW/H`.  Available
 * synchronously on every mount (provide/inject is reactive but not async),
 * so the head-tilt transform origin lands in the right place on first
 * paint — earlier versions fetched dimensions.json after mount, which
 * caused a head-snap on every floor-tab switch.
 */
const dims = inject<Ref<{ w: number; h: number } | null>>(
  FACE_CANVAS_DIMS,
  ref(null),
);

// When the server finishes extracting assets, mint a fresh bust token so
// `withBust` re-requests the URLs that 404'd before. Successful sprites stay
// cached — the previous behaviour appended the bust query to every URL,
// causing the browser to re-fetch every layer of every visible librarian on
// the same transition.
const assetsReady = inject<Ref<boolean>>(ASSETS_READY, ref(true));
watch(assetsReady, (ready) => {
  if (!ready) return;
  bustToken.value = Date.now();
  failedSrcs.value = new Set();
  fashionBodyFailed.value = false;
  fashionFrontFailed.value = false;
  fashionSkinFailed.value = false;
  patronFrontFailed.value = false;
  patronRearFailed.value = false;
});

/**
 * Build a CSS `filter` value that multiplies an image's RGB channels by the
 * given byte tuple, leaving its alpha channel untouched.  Mirrors Unity's
 * `SpriteRenderer.color` exactly.
 *
 * Why not `background-color` + `background-blend-mode: multiply` + `mask-image`?
 * That technique fills the element with the tint color and masks to the sprite
 * shape — but at anti-aliased edges (sprite α<1) the source-over composite
 * leaves `(1−α)·tint` showing through, producing a tint-coloured halo.  An
 * SVG `feColorMatrix` filter operates on the rendered image pixels directly,
 * so partial-alpha edges are tinted at their actual alpha with no halo.
 */
function tintFilter(c: [number, number, number]): string {
  const r = c[0] / 255;
  const g = c[1] / 255;
  const b = c[2] / 255;
  const svg =
    `<svg xmlns='http://www.w3.org/2000/svg'>` +
    `<filter id='t' color-interpolation-filters='sRGB'>` +
    `<feColorMatrix type='matrix' values='` +
    `${r} 0 0 0 0 ` +
    `0 ${g} 0 0 0 ` +
    `0 0 ${b} 0 0 ` +
    `0 0 0 1 0'/></filter></svg>`;
  return `url("data:image/svg+xml;utf8,${encodeURIComponent(svg)}#t")`;
}

/**
 * Ordered sprite layers with their tint filters.  Back hair renders first so
 * it sits behind the head; front hair renders last.  A `filter: null` entry
 * paints the PNG directly (mouth — the source already carries its own colors;
 * the game renders mouths with `Color.white` i.e. no tint).
 */
const layers = computed(() => {
  const a = props.appearance;
  const hair = tintFilter(a.hairColor);
  const skin = tintFilter(a.skinColor);
  const eye = tintFilter(a.eyeColor);
  return {
    backHair: { src: `${BASE}backhair_${a.backHairID}.png`, filter: hair },
    face: [
      { src: `${BASE}head_${a.headID ?? 0}.png`, filter: skin },
      { src: `${BASE}eyes_${a.eyeID}.png`, filter: eye },
      { src: `${BASE}brows_${a.browID}.png`, filter: hair },
      { src: `${BASE}mouths_${a.mouthID}.png`, filter: null as string | null },
      { src: `${BASE}fronthair_${a.frontHairID}.png`, filter: hair },
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
    : null,
);

const fashionBodyFailed = ref(false);
/**
 * Pixel dimensions of the fashion body PNG, supplied by the server in
 * `FashionBook.bodyW/bodyH`.  Derived synchronously from props so the
 * preview can lay out the body layer on first paint — earlier versions
 * measured the dims via an `@load` handler on a hidden probe, which
 * caused a feet-snap because layout ran with `null` dims until the
 * image decoded (and never ran at all when `loading="lazy"` skipped the
 * off-viewport fetch).
 */
const fashionBodyDims = computed<{ w: number; h: number } | null>(() => {
  const fb = props.fashionBook;
  if (!fb || !fb.bodyW || !fb.bodyH) return null;
  return { w: fb.bodyW, h: fb.bodyH };
});
watch(fashionFileStem, () => {
  fashionBodyFailed.value = false;
});
// also reset when variant changes (different PNG)
watch(fashionVariantSuffix, () => {
  fashionBodyFailed.value = false;
});

/**
 * URL of the fashion front-layer composite PNG (in front of face), or null when
 * the book has no front-layer sprites (hasFrontLayer not set).
 */
const fashionFrontUrl = computed(() =>
  props.fashionBook?.hasFrontLayer
    ? `/assets/fashionbodies_front/${fashionFileStem.value}${fashionVariantSuffix.value}.png`
    : null,
);

const fashionFrontFailed = ref(false);
watch(fashionFileStem, () => {
  fashionFrontFailed.value = false;
});

/**
 * URL of the fashion skin-layer composite PNG — exposed body areas (neck, collarbone)
 * that are white silhouettes in the prefab.  Rendered as a CSS multiply-tinted layer
 * using the librarian's skin color, behind the main body composite so body sprites
 * (clothing) cover them naturally.
 */
const fashionSkinUrl = computed(() =>
  fashionFileStem.value
    ? `/assets/fashionbodies/${fashionFileStem.value}${fashionVariantSuffix.value}_skin.png`
    : null,
);

const fashionSkinFailed = ref(false);
watch(fashionFileStem, () => {
  fashionSkinFailed.value = false;
});
watch(fashionVariantSuffix, () => {
  fashionSkinFailed.value = false;
});

/**
 * Patron librarians have two composite PNGs extracted from their
 * SpecialCustomizedAppearance prefab:
 *   head_special_{id}.png      — head, face, front hair (above fashion body)
 *   head_special_{id}_rear.png — rear hair (behind fashion body)
 */
const patronFrontUrl = computed(() =>
  props.appearance.patronHeadId
    ? `${BASE}head_special_${props.appearance.patronHeadId}.png`
    : null,
);
const patronRearUrl = computed(() =>
  props.appearance.patronHeadId
    ? `${BASE}head_special_${props.appearance.patronHeadId}_rear.png`
    : null,
);

const patronFrontFailed = ref(false);
const patronRearFailed = ref(false);
watch(
  () => props.appearance.patronHeadId,
  () => {
    patronFrontFailed.value = false;
    patronRearFailed.value = false;
  },
);

/**
 * Whether the fashion book replaces the entire head model.
 * When true, neither generic face/hair layers nor patron composites are shown —
 * the fashion body already includes the head (e.g. Roland's Black Silence book).
 */
const fashionReplacesHead = computed(
  () => props.fashionBook?.replacesHead === true,
);

/** True when the patron composites should be shown (not overridden by a replacesHead book). */
const hasPatronHead = computed(
  () =>
    patronFrontUrl.value != null &&
    !patronFrontFailed.value &&
    !fashionReplacesHead.value,
);

/**
 * Whether the generic face/hair CSS layers should be shown.
 * Hidden when a patron composite replaces the face, or when the fashion skin
 * replaces the head model (replacesHead = true).
 */
const showFaceHairLayers = computed(
  () => !hasPatronHead.value && !fashionReplacesHead.value,
);

// Head-tilt rotation, height scaling, and feet-anchored floor alignment are pure
// functions of props/dims — see utils/appearanceGeometry.ts for the rationale
// behind each (largely reverse-engineered from the game's own renderer), kept
// there so this math is unit-tested without mounting the component.
const faceRotStyle = computed(() =>
  computeFaceRotStyle(props.fashionBook, dims.value, PREVIEW_W.value, PREVIEW_H.value),
);

const heightScale = computed(() =>
  computeHeightScale(props.appearance.height, props.zoom),
);

const floorY = computed(() => computeFloorY(PREVIEW_H.value));

const feetYCss = computed(() =>
  computeFeetYCss(
    fashionBodyDims.value,
    props.fashionBook?.feetYFrac,
    fashionReplacesHead.value,
    PREVIEW_W.value,
    PREVIEW_H.value,
  ),
);

const zoomCompensateY = computed(() =>
  computeZoomCompensateY(floorY.value, feetYCss.value),
);

const fashionBodyHeightCss = computed(() =>
  computeFashionBodyHeightCss(
    fashionReplacesHead.value,
    fashionBodyDims.value,
    PREVIEW_W.value,
  ),
);

/** Z-index per position so overlapping gifts layer in a natural order. */
const POSITION_Z: Record<string, number> = {
  Helmet: 10,
  Nose: 11,
  Cheek: 11,
  Mouth: 11,
  Eye: 12,
  Ear: 12,
  Mask: 13,
  HairAccessory: 14,
  Hood: 15,
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
    Preview backdrop sits visually "below" the surrounding surface.  The
    tinted sprite layers compute their color entirely within their own
    element (`background-image × background-color` via `background-blend-mode:
    multiply`, clipped by `mask-image`), so the backdrop color only shows in
    transparent regions and has no effect on the character tint.
  -->
  <div
    class="preview-box"
    :style="{ width: `${PREVIEW_W}px`, height: `${PREVIEW_H}px` }"
  >
    <!--
      Hidden <img> probes: detect 404 for each sprite URL. We can't attach @error
      directly to a CSS background-image, so these invisible elements act as sentinels.
    -->
    <img
      v-for="(layer, i) in allLayerSrcs"
      :key="`probe-${i}`"
      :src="withBust(layer.src)"
      class="probe"
      alt=""
      loading="lazy"
      decoding="async"
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
        transform: `translate(0, ${zoomCompensateY}px) scale(${heightScale})`,
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
        v-show="
          showFaceHairLayers &&
          !failedSrcs.has(layers.backHair.src) &&
          !fashionBook?.hidesBackHair
        "
        class="layer-sprite body-layer"
        :style="{
          backgroundImage: `url(${withBust(layers.backHair.src)})`,
          filter: layers.backHair.filter,
          ...faceRotStyle,
        }"
      />
      <div
        v-if="
          hasPatronHead &&
          patronRearUrl &&
          !patronRearFailed &&
          !fashionBook?.hidesBackHair
        "
        class="layer-sprite body-layer"
        :style="{ backgroundImage: `url(${withBust(patronRearUrl)})`, ...faceRotStyle }"
      />
      <img
        v-if="patronRearUrl"
        :src="withBust(patronRearUrl)"
        class="probe"
        alt=""
        loading="lazy"
        decoding="async"
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
        class="layer-sprite body-layer"
        :style="{
          backgroundImage: `url(${withBust(fashionSkinUrl)})`,
          filter: tintFilter(appearance.skinColor),
          ...(fashionBodyHeightCss != null
            ? { height: `${fashionBodyHeightCss}px`, bottom: 'auto' }
            : {}),
        }"
      />
      <img
        v-if="fashionSkinUrl"
        :src="withBust(fashionSkinUrl)"
        class="probe"
        alt=""
        loading="lazy"
        decoding="async"
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
        :style="{
          backgroundImage: `url(${withBust(fashionBodyUrl)})`,
          ...(fashionBodyHeightCss != null
            ? { height: `${fashionBodyHeightCss}px`, bottom: 'auto' }
            : {}),
        }"
      />
      <img
        v-if="fashionBook && fashionBodyUrl"
        :src="withBust(fashionBodyUrl)"
        class="probe"
        alt=""
        loading="lazy"
        decoding="async"
        @error="fashionBodyFailed = true"
      />

      <!--
      Patron front composite: head, face, and front hair for patron librarians.
      Rendered above the fashion body, replacing the generic face/hair layers.
    -->
      <div
        v-if="hasPatronHead && patronFrontUrl"
        class="layer-sprite body-layer"
        :style="{ backgroundImage: `url(${withBust(patronFrontUrl)})`, ...faceRotStyle }"
      />
      <img
        v-if="patronFrontUrl"
        :src="withBust(patronFrontUrl)"
        class="probe"
        alt=""
        loading="lazy"
        decoding="async"
        @error="patronFrontFailed = true"
      />

      <!--
      Remaining face/hair layers (head, eyes, brows, mouth, fronthair) rendered on top
      of the fashion body so the librarian's face shows through the body composite.
      Hidden when a patron head composite is active.

      All layers paint the PNG directly via `body-layer` (no background-color,
      no mask).  Tinted layers apply an SVG `feColorMatrix` filter that
      multiplies image RGB by the tint and preserves source alpha — see
      `tintFilter()` for why this is used instead of the
      multiply-blend + mask-image technique (which haloed anti-aliased edges).
    -->
      <div
        v-for="(layer, i) in layers.face"
        :key="`face-${i}`"
        v-show="showFaceHairLayers && !failedSrcs.has(layer.src)"
        class="layer-sprite body-layer"
        :style="{
          backgroundImage: `url(${withBust(layer.src)})`,
          ...(layer.filter != null ? { filter: layer.filter } : {}),
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
        :style="{
          backgroundImage: `url(${withBust(fashionFrontUrl)})`,
          ...(fashionBodyHeightCss != null
            ? { height: `${fashionBodyHeightCss}px`, bottom: 'auto' }
            : {}),
        }"
      />
      <img
        v-if="fashionBook && fashionFrontUrl"
        :src="withBust(fashionFrontUrl)"
        class="probe"
        alt=""
        loading="lazy"
        decoding="async"
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
          backgroundImage: `url(${withBust(`/assets/gifts/gift_${gift.id}.png`)})`,
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
  /* One step "below" the surrounding surface: matches the deepest app
   * background so the preview reads as an inset panel rather than the
   * bright white tile that previously clashed with the dark UI.  A subtle
   * border keeps the inset visible even when the parent is `--bg` too
   * (e.g. CustomizePanel). */
  background: var(--bg);
  border: 1px solid var(--border);
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
