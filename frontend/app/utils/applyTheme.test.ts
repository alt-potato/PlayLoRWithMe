import { describe, expect, it } from "vitest";
import { applyTheme } from "./applyTheme";

// vitest defaults to a node environment, so we substitute a minimal stand-in
// for CSSStyleDeclaration that supports the two methods applyTheme uses.
class FakeStyle {
  private map = new Map<string, string>();
  setProperty(name: string, value: string) {
    this.map.set(name, value);
  }
  getPropertyValue(name: string): string {
    return this.map.get(name) ?? "";
  }
}

function makeFakeRoot(): HTMLElement {
  return { style: new FakeStyle() as unknown as CSSStyleDeclaration } as HTMLElement;
}

describe("applyTheme", () => {
  it("writes both faction die fills when theme is fully populated", () => {
    const root = makeFakeRoot();
    const written = applyTheme(
      { factionDieColors: { ally: "#aabbcc", enemy: "#ddeeff" } },
      root,
    );
    expect(written).toEqual(["--die-ally-fill", "--die-enemy-fill"]);
    expect(root.style.getPropertyValue("--die-ally-fill")).toBe("#aabbcc");
    expect(root.style.getPropertyValue("--die-enemy-fill")).toBe("#ddeeff");
  });

  it("no-ops when the theme block is missing", () => {
    const root = makeFakeRoot();
    const written = applyTheme(undefined, root);
    expect(written).toEqual([]);
    expect(root.style.getPropertyValue("--die-ally-fill")).toBe("");
    expect(root.style.getPropertyValue("--die-enemy-fill")).toBe("");
  });

  it("no-ops when factionDieColors is absent", () => {
    const root = makeFakeRoot();
    const written = applyTheme({}, root);
    expect(written).toEqual([]);
  });

  it("writes only the present half when one colour is missing", () => {
    const root = makeFakeRoot();
    // Cast through unknown because the schema marks the inner object
    // optional but each field required; we deliberately exercise the
    // partial branch as a defensive guard.
    const partial = { factionDieColors: { ally: "#112233" } as unknown as {
      ally: string;
      enemy: string;
    } };
    const written = applyTheme(partial, root);
    expect(written).toEqual(["--die-ally-fill"]);
    expect(root.style.getPropertyValue("--die-ally-fill")).toBe("#112233");
    expect(root.style.getPropertyValue("--die-enemy-fill")).toBe("");
  });
});
