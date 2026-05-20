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

/** Parses a `#rgb` or `#rrggbb` hex string to an RGB byte tuple, or null. */
export function hexToRgb(hex: string): [number, number, number] | null {
  const m = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(hex.trim());
  if (!m) return null;
  let h = m[1]!;
  if (h.length === 3) h = h[0]! + h[0]! + h[1]! + h[1]! + h[2]! + h[2]!;
  return [
    parseInt(h.slice(0, 2), 16),
    parseInt(h.slice(2, 4), 16),
    parseInt(h.slice(4, 6), 16),
  ];
}

/** Formats an RGB byte tuple as a lowercase `#rrggbb` hex string. */
export function rgbToHex([r, g, b]: [number, number, number]): string {
  const h = (v: number) => Math.max(0, Math.min(255, v)).toString(16).padStart(2, "0");
  return `#${h(r)}${h(g)}${h(b)}`;
}

// In-game speed dice tint a single faction colour onto pre-shaded sprite
// assets: the body sprite is dark, so the rendered background reads much
// darker than the tint, while the numeral sprite is a bright highlight, so the
// rendered numeral reads brighter and more saturated than the tint. The mod
// only samples the one tint colour, so we reproduce that split here rather
// than painting the raw tint flat (which looks washed out and leaves the
// numeral with poor contrast). Constants are fitted to the vanilla enemy tint
// #e2a3c4, which renders in-game as ~#8f2d62 background / bright pink numeral.
const DIE_BG_LIGHTNESS_SCALE = 0.49;
const DIE_NUMERAL_SATURATION_SCALE = 1.8;
const DIE_NUMERAL_LIGHTNESS_SCALE = 1.05;
const DIE_NUMERAL_MAX_LIGHTNESS = 95;

const clamp = (v: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, v));

/**
 * Derives the rendered speed-die background and numeral colours from a single
 * sampled faction tint, approximating the game's sprite-tinting. Both keep the
 * tint's hue so the die stays in its colour family; the background is darkened
 * and the numeral is brightened and saturated, giving inherent legibility
 * regardless of the tint a mod author picks. Returns null for malformed hex.
 */
export function deriveDieColors(
  tint: string,
): { background: string; numeral: string } | null {
  const rgb = hexToRgb(tint);
  if (!rgb) return null;
  const [h, s, l] = rgbToHsl(...rgb);
  const background = rgbToHex(hslToRgb(h, s, l * DIE_BG_LIGHTNESS_SCALE));
  const numeral = rgbToHex(
    hslToRgb(
      h,
      clamp(s * DIE_NUMERAL_SATURATION_SCALE, 0, 100),
      clamp(l * DIE_NUMERAL_LIGHTNESS_SCALE, 0, DIE_NUMERAL_MAX_LIGHTNESS),
    ),
  );
  return { background, numeral };
}
