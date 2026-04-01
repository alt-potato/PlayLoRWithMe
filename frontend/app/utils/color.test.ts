import { describe, it, expect } from "vitest";
import { rgbToHsl, hslToRgb } from "./color";

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
