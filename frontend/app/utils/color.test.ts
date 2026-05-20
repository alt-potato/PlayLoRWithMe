import { describe, it, expect } from "vitest";
import {
  rgbToHsl,
  hslToRgb,
  hexToRgb,
  rgbToHex,
  deriveDieColors,
} from "./color";

describe("rgbToHsl", () => {
  it("converts black", () => {
    expect(rgbToHsl(0, 0, 0)).toEqual([0, 0, 0]);
  });

  it("converts white", () => {
    expect(rgbToHsl(255, 255, 255)).toEqual([0, 0, 100]);
  });

  it("converts pure red", () => {
    expect(rgbToHsl(255, 0, 0)).toEqual([0, 100, 50]);
  });

  it("converts pure green", () => {
    expect(rgbToHsl(0, 255, 0)).toEqual([120, 100, 50]);
  });

  it("converts pure blue", () => {
    expect(rgbToHsl(0, 0, 255)).toEqual([240, 100, 50]);
  });

  it("converts mid-gray", () => {
    expect(rgbToHsl(128, 128, 128)).toEqual([0, 0, 50]);
  });
});

describe("hslToRgb", () => {
  it("converts black", () => {
    expect(hslToRgb(0, 0, 0)).toEqual([0, 0, 0]);
  });

  it("converts white", () => {
    expect(hslToRgb(0, 0, 100)).toEqual([255, 255, 255]);
  });

  it("converts pure red", () => {
    expect(hslToRgb(0, 100, 50)).toEqual([255, 0, 0]);
  });

  it("converts pure green", () => {
    expect(hslToRgb(120, 100, 50)).toEqual([0, 255, 0]);
  });

  it("converts pure blue", () => {
    expect(hslToRgb(240, 100, 50)).toEqual([0, 0, 255]);
  });

  it("converts achromatic (s=0) to gray", () => {
    expect(hslToRgb(180, 0, 50)).toEqual([128, 128, 128]);
  });
});

describe("round-trip", () => {
  it("black survives rgb→hsl→rgb", () => {
    const [h, s, l] = rgbToHsl(0, 0, 0);
    expect(hslToRgb(h, s, l)).toEqual([0, 0, 0]);
  });

  it("white survives rgb→hsl→rgb", () => {
    const [h, s, l] = rgbToHsl(255, 255, 255);
    expect(hslToRgb(h, s, l)).toEqual([255, 255, 255]);
  });

  it("a mid-tone color survives rgb→hsl→rgb within ±1", () => {
    // Rounding in both directions may cause ±1 drift on round-trip.
    const original: [number, number, number] = [115, 64, 26];
    const [h, s, l] = rgbToHsl(...original);
    const result = hslToRgb(h, s, l);
    for (let i = 0; i < 3; i++) {
      expect(Math.abs(result[i as 0 | 1 | 2] - original[i as 0 | 1 | 2])).toBeLessThanOrEqual(1);
    }
  });

  it("pure hues survive rgb→hsl→rgb", () => {
    const cases: Array<[number, number, number]> = [
      [255, 0, 0],
      [0, 255, 0],
      [0, 0, 255],
      [255, 255, 0],
      [0, 255, 255],
      [255, 0, 255],
    ];
    for (const rgb of cases) {
      const [h, s, l] = rgbToHsl(...rgb);
      expect(hslToRgb(h, s, l)).toEqual(rgb);
    }
  });
});

describe("hexToRgb", () => {
  it("parses #rrggbb", () => {
    expect(hexToRgb("#e2a3c4")).toEqual([226, 163, 196]);
  });

  it("parses shorthand #rgb", () => {
    expect(hexToRgb("#abc")).toEqual([170, 187, 204]);
  });

  it("tolerates a missing leading # and surrounding whitespace", () => {
    expect(hexToRgb("  e2a3c4 ")).toEqual([226, 163, 196]);
  });

  it("returns null for malformed input", () => {
    expect(hexToRgb("not-a-colour")).toBeNull();
    expect(hexToRgb("#12")).toBeNull();
    expect(hexToRgb("#1234")).toBeNull();
  });
});

describe("rgbToHex", () => {
  it("formats a tuple as lowercase #rrggbb", () => {
    expect(rgbToHex([226, 163, 196])).toBe("#e2a3c4");
  });

  it("zero-pads single-digit channels", () => {
    expect(rgbToHex([1, 2, 3])).toBe("#010203");
  });

  it("clamps out-of-range channels", () => {
    expect(rgbToHex([300, -5, 128])).toBe("#ff0080");
  });
});

describe("deriveDieColors", () => {
  it("returns null for malformed hex", () => {
    expect(deriveDieColors("nope")).toBeNull();
  });

  it("darkens the background and brightens/saturates the numeral", () => {
    const tint = "#e2a3c4"; // vanilla enemy tint
    const derived = deriveDieColors(tint)!;
    expect(derived).not.toBeNull();

    const [th, ts, tl] = rgbToHsl(...hexToRgb(tint)!);
    const [bh, bs, bl] = rgbToHsl(...hexToRgb(derived.background)!);
    const [nh, ns, nl] = rgbToHsl(...hexToRgb(derived.numeral)!);

    // hue stays in the same family for both halves (allow rounding drift)
    expect(Math.abs(bh - th)).toBeLessThanOrEqual(3);
    expect(Math.abs(nh - th)).toBeLessThanOrEqual(3);

    // background is markedly darker than the tint
    expect(bl).toBeLessThan(tl);
    // numeral is brighter than the background and more saturated than the tint
    expect(nl).toBeGreaterThan(bl);
    expect(ns).toBeGreaterThan(ts);
  });

  it("reproduces the documented enemy-tint split within tolerance", () => {
    // #e2a3c4 renders in-game as ~#8f2d62 background / bright pink numeral.
    const derived = deriveDieColors("#e2a3c4")!;
    const bg = hexToRgb(derived.background)!;
    const target: [number, number, number] = [0x8f, 0x2d, 0x62];
    for (let i = 0; i < 3; i++) {
      expect(Math.abs(bg[i as 0 | 1 | 2] - target[i as 0 | 1 | 2])).toBeLessThanOrEqual(4);
    }
    // numeral is a bright pink: high lightness, near-full saturation
    const [, ns, nl] = rgbToHsl(...hexToRgb(derived.numeral)!);
    expect(nl).toBeGreaterThanOrEqual(75);
    expect(ns).toBeGreaterThanOrEqual(90);
  });
});
