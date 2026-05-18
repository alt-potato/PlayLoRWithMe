import { describe, it, expect } from "vitest";
import { rarityStyle } from "./rarityStyle";

describe("rarityStyle", () => {
  it("resolves a vanilla rarity name to its --rarity-* token", () => {
    expect(rarityStyle({ rarity: "Rare" })).toEqual({
      "--rarity-color": "var(--rarity-rare)",
    });
    expect(rarityStyle({ rarity: "Common" })).toEqual({
      "--rarity-color": "var(--rarity-common)",
    });
  });

  it("returns an empty object for an unknown rarity name and no overrides", () => {
    expect(rarityStyle({ rarity: "Mythic" })).toEqual({});
    expect(rarityStyle({})).toEqual({});
  });

  it("payload override wins over the vanilla token lookup", () => {
    expect(
      rarityStyle({ rarity: "Rare", rarityColor: "#ff00ff" }),
    ).toEqual({ "--rarity-color": "#ff00ff" });
  });

  it("emits all four vars when every override is provided", () => {
    expect(
      rarityStyle({
        rarity: "Common",
        rarityColor: "#000001",
        rarityRangeIconColor: "#000002",
        rarityAbilityColor: "#000003",
        rarityKeywordColor: "#000004",
      }),
    ).toEqual({
      "--rarity-color": "#000001",
      "--rarity-range-icon-color": "#000002",
      "--rarity-ability-color": "#000003",
      "--rarity-keyword-color": "#000004",
    });
  });

  it("only emits the rarity-color var when only rarity is provided", () => {
    // sibling vars are uniform across vanilla rarities — components rely on the
    // app.vue defaults via var(--name, <default>) fallbacks instead.
    expect(rarityStyle({ rarity: "Unique" })).toEqual({
      "--rarity-color": "var(--rarity-unique)",
    });
  });

  it("emits a sibling var without --rarity-color when only that override is supplied", () => {
    expect(
      rarityStyle({ rarityRangeIconColor: "#abcdef" }),
    ).toEqual({ "--rarity-range-icon-color": "#abcdef" });
  });
});
