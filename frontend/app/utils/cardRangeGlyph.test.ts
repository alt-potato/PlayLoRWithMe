import { describe, it, expect } from "vitest";
import { cardRangeIconDescriptor } from "./cardRangeGlyph";

describe("cardRangeIconDescriptor", () => {
  it("maps Near to the sword svg glyph", () => {
    const d = cardRangeIconDescriptor("Near");
    expect(d.label).toBe("Near");
    expect(d.glyph).toEqual({ kind: "svg", id: "sword" });
  });

  it("maps Far to the gun svg glyph", () => {
    const d = cardRangeIconDescriptor("Far");
    expect(d.label).toBe("Far");
    expect(d.glyph).toEqual({ kind: "svg", id: "gun" });
  });

  it("maps Instance to the triangle-bolt svg glyph", () => {
    const d = cardRangeIconDescriptor("Instance");
    expect(d.label).toBe("Instance");
    expect(d.glyph).toEqual({ kind: "svg", id: "triangle-bolt" });
  });

  it("maps Special to the sword-plus svg glyph", () => {
    const d = cardRangeIconDescriptor("Special");
    expect(d.label).toBe("Special");
    expect(d.glyph).toEqual({ kind: "svg", id: "sword-plus" });
  });

  it("maps FarArea to the uppercase sigma unicode glyph", () => {
    const d = cardRangeIconDescriptor("FarArea");
    expect(d.label).toBe("FarArea");
    expect(d.glyph).toEqual({ kind: "unicode", symbol: "Σ" });
  });

  it("maps FarAreaEach to the inverted-A unicode glyph", () => {
    const d = cardRangeIconDescriptor("FarAreaEach");
    expect(d.label).toBe("FarAreaEach");
    expect(d.glyph).toEqual({ kind: "unicode", symbol: "∀" });
  });

  it("falls back to the raw range string for an unknown value", () => {
    const d = cardRangeIconDescriptor("SomethingElse");
    expect(d.label).toBe("SomethingElse");
    expect(d.glyph).toEqual({ kind: "fallback" });
  });

  it("preserves an empty-string range in the label and uses the fallback glyph", () => {
    const d = cardRangeIconDescriptor("");
    expect(d.label).toBe("");
    expect(d.glyph).toEqual({ kind: "fallback" });
  });
});
