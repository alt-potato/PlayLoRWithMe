/**
 * Pure RGB ↔ HSL conversion utilities used by HslColorPicker.
 *
 * All functions operate on integer byte values for RGB (0–255) and
 * floating-point degrees/percentages for HSL (h: 0–360, s/l: 0–100).
 * Rounding is applied on output to keep values at integer precision.
 */

/** Converts an RGB byte tuple to HSL (h: 0–360, s: 0–100, l: 0–100). */
export function rgbToHsl(
  r: number,
  g: number,
  b: number,
): [number, number, number] {
  const r1 = r / 255;
  const g1 = g / 255;
  const b1 = b / 255;
  const max = Math.max(r1, g1, b1);
  const min = Math.min(r1, g1, b1);
  const l = (max + min) / 2;

  if (max === min) {
    // achromatic — hue and saturation are undefined, default to 0
    return [0, 0, Math.round(l * 100)];
  }

  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);

  let h: number;
  if (max === r1) {
    h = ((g1 - b1) / d + (g1 < b1 ? 6 : 0)) / 6;
  } else if (max === g1) {
    h = ((b1 - r1) / d + 2) / 6;
  } else {
    h = ((r1 - g1) / d + 4) / 6;
  }

  return [Math.round(h * 360), Math.round(s * 100), Math.round(l * 100)];
}

/** Converts HSL (h: 0–360, s: 0–100, l: 0–100) to an RGB byte tuple. */
export function hslToRgb(
  h: number,
  s: number,
  l: number,
): [number, number, number] {
  const h1 = h / 360;
  const s1 = s / 100;
  const l1 = l / 100;

  if (s1 === 0) {
    // achromatic
    const v = Math.round(l1 * 255);
    return [v, v, v];
  }

  const q = l1 < 0.5 ? l1 * (1 + s1) : l1 + s1 - l1 * s1;
  const p = 2 * l1 - q;

  return [
    Math.round(hue2rgb(p, q, h1 + 1 / 3) * 255),
    Math.round(hue2rgb(p, q, h1) * 255),
    Math.round(hue2rgb(p, q, h1 - 1 / 3) * 255),
  ];
}

function hue2rgb(p: number, q: number, t: number): number {
  if (t < 0) t += 1;
  if (t > 1) t -= 1;
  if (t < 1 / 6) return p + (q - p) * 6 * t;
  if (t < 1 / 2) return q;
  if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
  return p;
}
