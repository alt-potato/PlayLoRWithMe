/**
 * appearanceGeometry.ts
 *
 * Pure sprite-layout math for the librarian appearance preview: head-tilt
 * rotation, height-driven uniform scale, and the feet-anchored floor
 * alignment that keeps every librarian standing on the same line regardless
 * of body aspect or configured height.
 *
 * Kept out of the component so this math — largely reverse-engineered from
 * the game's own `UICharacterRenderer` and the per-book metadata written by
 * `AppearanceCache` — is testable without mounting Vue. Every function here
 * is a plain function of its inputs: no props, no injection, no DOM.
 */

import type { FashionBook } from "~/types/game";

/** Pixel dimensions of a rendered canvas or sprite. */
export interface Dims {
  w: number;
  h: number;
}

// ── Head tilt ────────────────────────────────────────────────────────────

/** Minimum tilt magnitude, in degrees, before a rotation transform is applied. */
export const HEAD_TILT_EPSILON_DEG = 0.05;

/** Fields of `FashionBook` that drive the head-tilt transform. */
export type HeadTiltSource = Pick<
  FashionBook,
  "headTiltDeg" | "pivotFracX" | "pivotFracY"
>;

/** CSS transform for the head-tilt rotation; empty when the tilt is inactive. */
export interface FaceRotStyle {
  transform?: string;
  transformOrigin?: string;
}

/**
 * CSS transform applied to face/hair layers when the active fashion book has a
 * non-zero head tilt. The origin is set to the canonical librarian pivot so the
 * rotation matches what the game shows.
 *
 * Unity's left-hand screen space means a positive eulerAngles.z value is
 * counter-clockwise on screen, opposite to CSS rotate(+deg), so the angle is negated.
 */
export function computeFaceRotStyle(
  fashionBook: HeadTiltSource | null | undefined,
  faceCanvasDims: Dims | null,
  previewW: number,
  previewH: number,
): FaceRotStyle {
  if (
    !fashionBook ||
    !fashionBook.headTiltDeg ||
    Math.abs(fashionBook.headTiltDeg) < HEAD_TILT_EPSILON_DEG
  ) {
    return {};
  }

  const fracX = fashionBook.pivotFracX ?? 0.5;
  const fracY = fashionBook.pivotFracY ?? 0.5;
  // CSS canvas height at previewW: scale = previewW / dims.w, height = dims.h * scale.
  const canvasCssH = faceCanvasDims
    ? previewW * (faceCanvasDims.h / faceCanvasDims.w)
    : previewH; // fallback: assume square face canvas

  const originX = previewW * fracX;
  const originY = canvasCssH * fracY;

  return {
    transform: `rotate(${-fashionBook.headTiltDeg}deg)`,
    transformOrigin: `${originX}px ${originY}px`,
  };
}

// ── Height scale ─────────────────────────────────────────────────────────

/**
 * Height scale factor matching the game's own character scaling:
 * `UICharacterRenderer.GetRenderTextureByIndexAndSize` sets
 * `unitAppearance.localScale` to `Vector2.one * customizeData.height * 0.005`,
 * so height=200 is the 1.0 reference and a 170-height librarian renders at 0.85x.
 */
export const HEIGHT_SCALE_FACTOR = 0.005;

/** Librarian height (cm) assumed when `appearance.height` is not supplied. */
export const DEFAULT_HEIGHT = 170;

/**
 * Default extra zoom layered on top of the height-driven scale. Makes the
 * character appear larger inside the same-sized viewport so the preview
 * reads as a "portrait" rather than a tiny silhouette in dead space.
 */
export const DEFAULT_ZOOM = 2.25;

/** Height-only scale factor, before the extra preview zoom is applied. */
export function computeBaseHeightScale(height: number | undefined): number {
  return (height ?? DEFAULT_HEIGHT) * HEIGHT_SCALE_FACTOR;
}

/**
 * Full scale factor applied to the sprite stack, combining the height-driven
 * base scale with the extra preview zoom.
 *
 * The scale is anchored at each body's natural feet position (see
 * `computeFeetYCss`, driven by the per-book `feetYFrac` exported by
 * AppearanceCache) so resizing keeps feet planted on a shared floor line —
 * matching the in-game behavior where the prefab's transform origin sits at
 * the feet and scaling about the transform origin trivially preserves foot
 * alignment.
 */
export function computeHeightScale(
  height: number | undefined,
  zoom: number | undefined,
): number {
  return computeBaseHeightScale(height) * (zoom ?? DEFAULT_ZOOM);
}

// ── Feet-anchored floor alignment ───────────────────────────────────────

/**
 * Fraction of the preview height reserved as breathing room between the
 * shared floor line and the bottom edge of the viewport.
 */
export const FOOT_BUFFER_FRACTION = 0;

/**
 * Y coordinate (CSS px) of the shared floor line — the position in the
 * viewport where every librarian's feet land.
 */
export function computeFloorY(previewH: number): number {
  return previewH * (1 - FOOT_BUFFER_FRACTION);
}

/**
 * Y coordinate (CSS px) of the character's feet within the preview box, used
 * as the scale transform origin so the feet stay pinned across height changes.
 *
 * The per-book `feetYFrac` (exported by AppearanceCache; defaults to 1.0 when
 * omitted = feet at PNG bottom) marks where feet actually sit inside the PNG,
 * letting us offset inward when the PNG extends below feet (weapons/props).
 *
 * Layout specifics:
 * - Non-replacesHead bodies share the face canvas width and are drawn with
 *   `background-size: 100% auto; background-position: left top`, so the CSS
 *   height of the body PNG = previewW * (naturalH / naturalW). Feet CSS Y
 *   is that height times feetYFrac.
 * - ReplacesHead bodies use `background-size: contain; background-position:
 *   top center`, which fits the whole body inside previewW x previewH.
 *   The rendered height is `min(previewH, previewW * aspect)`; feet CSS Y
 *   is that height times feetYFrac.
 * - When no body PNG is loaded (face-only librarians), fall back to the
 *   bottom of the preview box so scaling still behaves reasonably.
 */
export function computeFeetYCss(
  bodyDims: Dims | null,
  feetYFrac: number | undefined,
  replacesHead: boolean,
  previewW: number,
  previewH: number,
): number {
  if (!bodyDims) return previewH;

  const aspect = bodyDims.h / bodyDims.w;
  const feetFrac = feetYFrac ?? 1;
  const renderedH = replacesHead
    ? // `contain` fits the image fully inside the box while preserving aspect.
      aspect >= previewH / previewW
      ? previewH
      : previewW * aspect
    : // non-replacesHead: width pinned to previewW, height scales with aspect.
      previewW * aspect;
  return renderedH * feetFrac;
}

/**
 * Vertical translation (CSS px) applied before the scale. Pins each
 * librarian's `feetYCss` (the transform-origin Y) to the shared `floorY`,
 * so all librarians stand on the same floor regardless of body aspect or
 * height — taller librarians have heads correspondingly higher, shorter
 * ones lower, mirroring the in-game fixed-camera view.
 */
export function computeZoomCompensateY(floorY: number, feetYCss: number): number {
  return floorY - feetYCss;
}

/**
 * Explicit CSS pixel height for non-replacesHead body/skin/front layer divs.
 * Without this, `inset: 0` constrains the div to previewH while the PNG
 * painted via `background-size: 100% auto` has natural rendered height
 * `previewW * aspect`. When aspect > 1 (body PNG taller than wide — common
 * when the book has a tall hat or a feet-at-PNG-bottom layout with no weapon
 * extending below), the background image is clipped at the element's border
 * box *before* the feet-anchored transform applies, chopping off the feet.
 * Extending the layer height to the PNG's natural rendered height keeps the
 * full body painted so the feet land correctly on the shared floor line after
 * the transform. Returns null for replacesHead bodies (those use
 * `background-size: contain` which never exceeds the element's bounds).
 */
export function computeFashionBodyHeightCss(
  replacesHead: boolean,
  bodyDims: Dims | null,
  previewW: number,
): number | null {
  if (replacesHead) return null;
  if (!bodyDims) return null;
  const aspect = bodyDims.h / bodyDims.w;
  return previewW * aspect;
}
