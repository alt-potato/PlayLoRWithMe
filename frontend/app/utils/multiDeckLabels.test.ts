import { describe, expect, it } from "vitest";
import { FALLBACK_DECK_LABELS, resolveDeckLabels } from "./multiDeckLabels";

describe("resolveDeckLabels", () => {
  it("returns Purple Tear stance labels for (0, 250035)", () => {
    const labels = resolveDeckLabels(0, 250035);
    expect(labels).toEqual(["Slash", "Pierce", "Blunt", "Guard"]);
  });

  it("returns the same Purple Tear labels when packageId is the empty string", () => {
    // Vanilla key pages may be serialised with packageId omitted/empty;
    // the helper normalises those to "0" so the same lookup wins.
    expect(resolveDeckLabels("", 250035)).toEqual([
      "Slash",
      "Pierce",
      "Blunt",
      "Guard",
    ]);
  });

  it("falls back to generic labels for unknown ids", () => {
    expect(resolveDeckLabels(0, 999999)).toEqual(FALLBACK_DECK_LABELS);
  });

  it("falls back to generic labels for unknown workshop pages", () => {
    expect(resolveDeckLabels("workshop_2900000000", 100)).toEqual(
      FALLBACK_DECK_LABELS,
    );
  });

  it("falls back when packageId is missing", () => {
    expect(resolveDeckLabels(undefined, 250035)).toEqual([
      "Slash",
      "Pierce",
      "Blunt",
      "Guard",
    ]);
  });

  it("falls back when id is missing", () => {
    expect(resolveDeckLabels(0, undefined)).toEqual(FALLBACK_DECK_LABELS);
  });

  it("always returns four labels", () => {
    expect(resolveDeckLabels(0, 250035)).toHaveLength(4);
    expect(resolveDeckLabels(0, 999999)).toHaveLength(4);
  });
});
