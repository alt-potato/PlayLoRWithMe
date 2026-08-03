/**
 * Tests for the pure sprite-layout math behind AppearancePreview.vue.
 *
 * These run under the default `node` vitest environment (no DOM/Vue needed)
 * and cover the branches the component relies on for feet-anchored floor
 * alignment, height scaling, and head-tilt rotation — none of which is
 * visible to a type check.
 */

import { describe, it, expect } from "vitest";
import {
  DEFAULT_HEIGHT,
  DEFAULT_ZOOM,
  HEAD_TILT_EPSILON_DEG,
  HEIGHT_SCALE_FACTOR,
  computeBaseHeightScale,
  computeFaceRotStyle,
  computeFashionBodyHeightCss,
  computeFeetYCss,
  computeFloorY,
  computeHeightScale,
  computeZoomCompensateY,
} from "./appearanceGeometry";

describe("computeFeetYCss", () => {
  it("falls back to the bottom of the preview box when no body dims are available", () => {
    expect(computeFeetYCss(null, undefined, false, 160, 160)).toBe(160);
    // Falls back the same way even for a replacesHead book with no dims yet.
    expect(computeFeetYCss(null, 0.8, true, 160, 160)).toBe(160);
  });

  it("defaults feetYFrac to 1 (feet at the PNG bottom) when omitted", () => {
    // Non-replacesHead: renderedH = previewW * aspect = 160 * (300/200) = 240.
    const withDefault = computeFeetYCss({ w: 200, h: 300 }, undefined, false, 160, 160);
    const explicit1 = computeFeetYCss({ w: 200, h: 300 }, 1, false, 160, 160);
    expect(withDefault).toBe(240);
    expect(withDefault).toBe(explicit1);
  });

  it("scales a non-replacesHead body to preview width and applies feetYFrac", () => {
    // aspect = 400/200 = 2, renderedH = previewW(160) * 2 = 320, feet at 0.9 -> 288.
    expect(computeFeetYCss({ w: 200, h: 400 }, 0.9, false, 160, 160)).toBeCloseTo(288);
  });

  describe("replacesHead (background-size: contain)", () => {
    // previewH / previewW = 1 for a square preview box.
    it("uses previewH when aspect is above the previewH/previewW threshold", () => {
      // aspect = 300/200 = 1.5 >= 1 -> contain fits by height.
      expect(computeFeetYCss({ w: 200, h: 300 }, 1, true, 160, 160)).toBe(160);
    });

    it("uses previewH exactly at the aspect === previewH/previewW boundary", () => {
      // aspect = 160/160 = 1 === previewH/previewW(1) -> the >= branch, not the < branch.
      expect(computeFeetYCss({ w: 160, h: 160 }, 1, true, 160, 160)).toBe(160);
    });

    it("uses previewW * aspect when aspect is below the threshold (wide body)", () => {
      // aspect = 100/200 = 0.5 < 1 -> contain fits by width, renderedH = 160 * 0.5 = 80.
      expect(computeFeetYCss({ w: 200, h: 100 }, 1, true, 160, 160)).toBe(80);
    });

    it("respects a non-rectangular preview box's threshold", () => {
      // previewW=200, previewH=100 -> threshold = 0.5. aspect = 0.6 >= 0.5 -> previewH.
      expect(computeFeetYCss({ w: 100, h: 60 }, 1, true, 200, 100)).toBe(100);
      // aspect = 0.4 < 0.5 -> previewW * aspect = 200 * 0.4 = 80.
      expect(computeFeetYCss({ w: 100, h: 40 }, 1, true, 200, 100)).toBe(80);
    });
  });
});

describe("computeFloorY", () => {
  it("equals previewH (FOOT_BUFFER_FRACTION is currently 0)", () => {
    expect(computeFloorY(160)).toBe(160);
    expect(computeFloorY(320)).toBe(320);
  });
});

describe("computeZoomCompensateY", () => {
  it("is the difference between the floor line and the feet position", () => {
    expect(computeZoomCompensateY(160, 100)).toBe(60);
    expect(computeZoomCompensateY(160, 160)).toBe(0);
    expect(computeZoomCompensateY(160, 200)).toBe(-40);
  });
});

describe("height scaling", () => {
  it("treats height=200 as the 1.0 base-scale reference", () => {
    expect(computeBaseHeightScale(200)).toBe(1);
    expect(HEIGHT_SCALE_FACTOR * 200).toBe(1);
  });

  it("scales a shorter librarian proportionally", () => {
    // Documented example: a 170-height librarian renders at 0.85x.
    expect(computeBaseHeightScale(170)).toBeCloseTo(0.85);
  });

  it("defaults height to DEFAULT_HEIGHT when omitted", () => {
    expect(computeBaseHeightScale(undefined)).toBe(computeBaseHeightScale(DEFAULT_HEIGHT));
  });

  it("combines the base scale with the extra preview zoom", () => {
    expect(computeHeightScale(200, 1)).toBe(1);
    expect(computeHeightScale(200, 2)).toBe(2);
  });

  it("defaults zoom to DEFAULT_ZOOM when omitted", () => {
    expect(computeHeightScale(200, undefined)).toBe(DEFAULT_ZOOM);
  });
});

describe("computeFaceRotStyle", () => {
  const dims = { w: 256, h: 256 };

  it("returns no transform when the fashion book is absent", () => {
    expect(computeFaceRotStyle(null, dims, 160, 160)).toEqual({});
    expect(computeFaceRotStyle(undefined, dims, 160, 160)).toEqual({});
  });

  it("returns no transform when headTiltDeg is unset or zero", () => {
    expect(computeFaceRotStyle({}, dims, 160, 160)).toEqual({});
    expect(computeFaceRotStyle({ headTiltDeg: 0 }, dims, 160, 160)).toEqual({});
  });

  it("returns no transform below the epsilon threshold", () => {
    expect(
      computeFaceRotStyle({ headTiltDeg: HEAD_TILT_EPSILON_DEG - 0.01 }, dims, 160, 160),
    ).toEqual({});
    expect(
      computeFaceRotStyle({ headTiltDeg: -(HEAD_TILT_EPSILON_DEG - 0.01) }, dims, 160, 160),
    ).toEqual({});
  });

  it("applies a rotation at and above the epsilon threshold, negating the angle", () => {
    const atThreshold = computeFaceRotStyle({ headTiltDeg: HEAD_TILT_EPSILON_DEG }, dims, 160, 160);
    expect(atThreshold.transform).toBe(`rotate(${-HEAD_TILT_EPSILON_DEG}deg)`);

    const style = computeFaceRotStyle({ headTiltDeg: 10 }, dims, 160, 160);
    expect(style.transform).toBe("rotate(-10deg)");
  });

  it("also negates a negative tilt", () => {
    const style = computeFaceRotStyle({ headTiltDeg: -10 }, dims, 160, 160);
    expect(style.transform).toBe("rotate(10deg)");
  });

  it("places the transform origin at the pivot fraction of the canvas", () => {
    const style = computeFaceRotStyle(
      { headTiltDeg: 10, pivotFracX: 0.25, pivotFracY: 0.75 },
      dims,
      160,
      160,
    );
    // Square canvas: canvasCssH = previewW * (256/256) = 160.
    expect(style.transformOrigin).toBe("40px 120px");
  });

  it("defaults the pivot to the center when unset", () => {
    const style = computeFaceRotStyle({ headTiltDeg: 10 }, dims, 160, 160);
    expect(style.transformOrigin).toBe("80px 80px");
  });

  it("falls back to a square canvas assumption when face canvas dims are unavailable", () => {
    const style = computeFaceRotStyle({ headTiltDeg: 10 }, null, 160, 160);
    expect(style.transformOrigin).toBe("80px 80px");
  });

  it("derives canvas CSS height from a non-square face canvas", () => {
    // canvasCssH = previewW(160) * (512/256) = 320; pivotFracY 0.5 -> origin Y 160.
    const style = computeFaceRotStyle(
      { headTiltDeg: 10 },
      { w: 256, h: 512 },
      160,
      160,
    );
    expect(style.transformOrigin).toBe("80px 160px");
  });
});

describe("computeFashionBodyHeightCss", () => {
  it("returns null for replacesHead bodies regardless of dims", () => {
    expect(computeFashionBodyHeightCss(true, { w: 200, h: 400 }, 160)).toBeNull();
    expect(computeFashionBodyHeightCss(true, null, 160)).toBeNull();
  });

  it("returns null when body dims are unavailable", () => {
    expect(computeFashionBodyHeightCss(false, null, 160)).toBeNull();
  });

  it("returns the PNG's natural rendered height at the preview width", () => {
    // aspect = 400/200 = 2 -> 160 * 2 = 320.
    expect(computeFashionBodyHeightCss(false, { w: 200, h: 400 }, 160)).toBe(320);
  });
});
