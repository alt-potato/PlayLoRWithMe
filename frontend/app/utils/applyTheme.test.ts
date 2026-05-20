import { describe, expect, it } from "vitest";
import { applyTheme } from "./applyTheme";
import { deriveDieColors } from "./color";

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
  it("splits each faction tint into a derived background + numeral pair", () => {
    const root = makeFakeRoot();
    const written = applyTheme(
      { factionDieColors: { ally: "#aabbcc", enemy: "#ddeeff" } },
      root,
    );
    expect(written).toEqual([
      "--die-ally-bg",
      "--die-ally-num",
      "--die-enemy-bg",
      "--die-enemy-num",
    ]);
    // Values are exactly what deriveDieColors produces for each tint.
    const ally = deriveDieColors("#aabbcc")!;
    const enemy = deriveDieColors("#ddeeff")!;
    expect(root.style.getPropertyValue("--die-ally-bg")).toBe(ally.background);
    expect(root.style.getPropertyValue("--die-ally-num")).toBe(ally.numeral);
    expect(root.style.getPropertyValue("--die-enemy-bg")).toBe(enemy.background);
    expect(root.style.getPropertyValue("--die-enemy-num")).toBe(enemy.numeral);
  });

  it("no-ops when the theme block is missing", () => {
    const root = makeFakeRoot();
    const written = applyTheme(undefined, root);
    expect(written).toEqual([]);
    expect(root.style.getPropertyValue("--die-ally-bg")).toBe("");
    expect(root.style.getPropertyValue("--die-enemy-bg")).toBe("");
  });

  it("no-ops when factionDieColors is absent", () => {
    const root = makeFakeRoot();
    const written = applyTheme({}, root);
    expect(written).toEqual([]);
  });

  it("writes only the present faction when one colour is missing", () => {
    const root = makeFakeRoot();
    // Cast through unknown because the schema marks the inner object
    // optional but each field required; we deliberately exercise the
    // partial branch as a defensive guard.
    const partial = { factionDieColors: { ally: "#112233" } as unknown as {
      ally: string;
      enemy: string;
    } };
    const written = applyTheme(partial, root);
    expect(written).toEqual(["--die-ally-bg", "--die-ally-num"]);
    expect(root.style.getPropertyValue("--die-ally-bg")).toBe(
      deriveDieColors("#112233")!.background,
    );
    expect(root.style.getPropertyValue("--die-enemy-bg")).toBe("");
  });

  it("skips a faction whose tint is malformed", () => {
    const root = makeFakeRoot();
    const written = applyTheme(
      { factionDieColors: { ally: "not-a-colour", enemy: "#ddeeff" } as unknown as {
        ally: string;
        enemy: string;
      } },
      root,
    );
    expect(written).toEqual(["--die-enemy-bg", "--die-enemy-num"]);
    expect(root.style.getPropertyValue("--die-ally-bg")).toBe("");
  });
});
