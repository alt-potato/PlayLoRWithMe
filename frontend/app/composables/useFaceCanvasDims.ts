import type { InjectionKey, Ref } from "vue";

/**
 * Injection key for the shared face/hair canvas pixel dimensions, derived from
 * `gameState.customizeOptions.faceCanvasW/H` (populated by AppearanceCache once
 * sprite extraction has run).  Provided by app.vue; consumed by
 * AppearancePreview to compute the head-tilt transform origin synchronously
 * instead of fetching dimensions.json after mount — that fetch caused a
 * head-snap on every fresh remount (floor-tab switches, page reloads).
 *
 * Null when extraction hasn't completed yet; consumers fall back to a square
 * canvas assumption for that initial window.
 */
export const FACE_CANVAS_DIMS: InjectionKey<Ref<{ w: number; h: number } | null>> =
  Symbol("FaceCanvasDims");
