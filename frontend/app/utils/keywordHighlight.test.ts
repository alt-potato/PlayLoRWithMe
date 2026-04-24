import { describe, it, expect } from "vitest";
import { splitKeywordSegments } from "./keywordHighlight";

describe("splitKeywordSegments", () => {
  it("returns an empty array for an empty input", () => {
    expect(splitKeywordSegments("")).toEqual([]);
  });

  it("returns a single non-keyword segment for text without brackets", () => {
    expect(splitKeywordSegments("Deal damage equal to Power.")).toEqual([
      { text: "Deal damage equal to Power.", isKeyword: false },
    ]);
  });

  it("strips brackets from a bracketed keyword and splits surrounding text", () => {
    expect(splitKeywordSegments("[On Use] Gain 1 Haste.")).toEqual([
      { text: "On Use", isKeyword: true },
      { text: " Gain 1 Haste.", isKeyword: false },
    ]);
  });

  it("splits multiple keywords with plain text between them", () => {
    expect(
      splitKeywordSegments("[On Use] Draw 1 card. [On Clash Win] Inflict 2 Bleed."),
    ).toEqual([
      { text: "On Use", isKeyword: true },
      { text: " Draw 1 card. ", isKeyword: false },
      { text: "On Clash Win", isKeyword: true },
      { text: " Inflict 2 Bleed.", isKeyword: false },
    ]);
  });

  it("does not emit an empty padding segment for a leading keyword", () => {
    const out = splitKeywordSegments("[Counter] Gain 2 Protection.");
    expect(out[0]).toEqual({ text: "Counter", isKeyword: true });
    expect(out).toHaveLength(2);
  });

  it("does not emit an empty padding segment for a trailing keyword", () => {
    const out = splitKeywordSegments("Gain 2 Protection. [Counter]");
    expect(out[out.length - 1]).toEqual({ text: "Counter", isKeyword: true });
    expect(out).toHaveLength(2);
  });

  it("treats a keyword-only string as a single keyword segment", () => {
    expect(splitKeywordSegments("[Reroll]")).toEqual([
      { text: "Reroll", isKeyword: true },
    ]);
  });

  it("treats an unclosed bracket as plain text", () => {
    expect(splitKeywordSegments("[On Use Gain 1 Haste.")).toEqual([
      { text: "[On Use Gain 1 Haste.", isKeyword: false },
    ]);
  });

  it("treats a stray closing bracket as plain text", () => {
    expect(splitKeywordSegments("Gain 1 Haste] token.")).toEqual([
      { text: "Gain 1 Haste] token.", isKeyword: false },
    ]);
  });

  it("handles adjacent keywords with no separator", () => {
    expect(splitKeywordSegments("[On Use][Combat Start]")).toEqual([
      { text: "On Use", isKeyword: true },
      { text: "Combat Start", isKeyword: true },
    ]);
  });

  it("is safe against repeated invocation (regex state does not leak)", () => {
    const first = splitKeywordSegments("[A] tail");
    const second = splitKeywordSegments("[A] tail");
    expect(first).toEqual(second);
  });
});
