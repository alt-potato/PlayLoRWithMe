/**
 * Tests for the pure display helpers in useBattleDisplay.ts.
 *
 * These run under the default `node` vitest environment (no DOM needed) and
 * cover the branching logic that drives ally colour assignment, resistance
 * tier rendering, slot ordering, and card-style helpers — pieces that are
 * easy to break silently because they only show up in the rendered UI.
 */

import { describe, it, expect } from "vitest";
import {
  ALLY_COLORS,
  LONG_PRESS_MS,
  buildAllyColors,
  cardBorderColor,
  coinDots,
  costStyle,
  diceIcon,
  dieTypeColor,
  fillPercentage,
  formatSpeedDieValue,
  SPEED_DIE_INFINITY_THRESHOLD,
  isDead,
  isMassRange,
  isSlotFilled,
  rarityBorderStyle,
  rarityColor,
  resistLabel,
  resistStyle,
  resistSymbol,
  sortedSlots,
  toRoman,
  turnColor,
  turnLabel,
} from "./useBattleDisplay";
import type { Card, SlottedCardEntry, SpeedDie, Unit } from "~/types/game";

// ---------------------------------------------------------------------------
// Tiny fixture builders — keep tests focused on logic, not on assembling the
// (sometimes large) wire types from scratch each time. Each helper sets only
// the fields the function under test reads; everything else is filled with
// representative defaults so type checks pass without leaking irrelevant
// noise into assertions.
// ---------------------------------------------------------------------------

function unit(overrides: Partial<Unit> = {}): Unit {
  return {
    id: 1,
    name: "Test",
    hp: 100,
    maxHp: 100,
    staggerGauge: 0,
    maxStaggerGauge: 50,
    emotionLevel: 0,
    emotionCoins: { positive: 0, negative: 0, max: 0 },
    turnState: "WAIT_TURN",
    targetable: true,
    speedDice: [],
    slottedCards: [],
    ...overrides,
  } as Unit;
}

function die(slot: number, value: number, staggered = false): SpeedDie {
  return { slot, value, staggered, type: "Atk" } as SpeedDie;
}

function slotted(slot: number, name = "Card"): SlottedCardEntry {
  return {
    slot,
    cardIndex: 0,
    name,
    cost: 0,
    range: "Near",
    dice: [],
  } as SlottedCardEntry;
}

// ---------------------------------------------------------------------------

describe("constants", () => {
  it("exposes the long-press threshold for unit consumers", () => {
    // The constant is consumed by HandCard and DieRow; locking it keeps the
    // gesture feel consistent if either component is rewritten.
    expect(LONG_PRESS_MS).toBe(500);
  });

  it("ships exactly five ally colour slots", () => {
    expect(ALLY_COLORS).toHaveLength(5);
  });
});

describe("buildAllyColors", () => {
  it("assigns the i-th ALLY_COLORS entry to the i-th ally id", () => {
    const m = buildAllyColors([{ id: 10 }, { id: 11 }, { id: 12 }]);
    expect(m[10]).toBe(ALLY_COLORS[0]);
    expect(m[11]).toBe(ALLY_COLORS[1]);
    expect(m[12]).toBe(ALLY_COLORS[2]);
  });

  it("cycles colours when there are more allies than palette entries", () => {
    const ids = Array.from({ length: ALLY_COLORS.length + 2 }, (_, i) => ({ id: i }));
    const m = buildAllyColors(ids);
    expect(m[ALLY_COLORS.length]).toBe(ALLY_COLORS[0]);
    expect(m[ALLY_COLORS.length + 1]).toBe(ALLY_COLORS[1]);
  });

  it("returns an empty map for no allies", () => {
    expect(buildAllyColors([])).toEqual({});
  });
});

describe("resistance helpers", () => {
  it("returns the symbol token for known tiers and an empty string otherwise", () => {
    expect(resistSymbol("Weak")).toBe("++");
    expect(resistSymbol("Immune")).toBe("∅");
    expect(resistSymbol("unknown")).toBe("");
    expect(resistSymbol(undefined)).toBe("");
  });

  it("returns the player-facing label for known tiers and an em-dash otherwise", () => {
    expect(resistLabel("Vulnerable")).toBe("Weak (1.5×)");
    expect(resistLabel(undefined)).toBe("—");
  });

  it("uses the kind hue for non-flat tiers and includes a glow style on Fatal", () => {
    const fatal = resistStyle("Weak", "hp");
    expect(fatal.color).toBe("var(--health-bar)");
    expect(fatal.opacity).toBe(1.0);
    expect(fatal.textShadow).toMatch(/var\(--health-bar\)/);
  });

  it("flattens Immune to grey regardless of the kind", () => {
    const immune = resistStyle("Immune", "hp");
    expect(immune.color).toBe("#666");
    expect(immune.textShadow).toBeUndefined();
  });

  it("returns an empty style for unknown tiers", () => {
    expect(resistStyle("nope", "hp")).toEqual({});
  });
});

describe("turn-state helpers", () => {
  it("maps known states to readable labels and falls through for unknowns", () => {
    expect(turnLabel("WAIT_CARD")).toBe("READY");
    expect(turnLabel("BREAK")).toBe("STAGGERED");
    expect(turnLabel("MYSTERY")).toBe("MYSTERY");
  });

  it("maps known states to colours and falls back to grey", () => {
    expect(turnColor("BREAK")).toBe("#e53935");
    expect(turnColor("MYSTERY")).toBe("#888");
  });
});

describe("fillPercentage", () => {
  it("returns the percentage when max is positive", () => {
    expect(fillPercentage(50, 100)).toBe(50);
  });

  it("clamps above 100 when val exceeds max", () => {
    expect(fillPercentage(150, 100)).toBe(100);
  });

  it("returns 0 when max is zero or negative", () => {
    expect(fillPercentage(50, 0)).toBe(0);
    expect(fillPercentage(50, -1)).toBe(0);
  });
});

describe("coinDots", () => {
  it("composes filled, hollow, and empty dots up to max", () => {
    const u = unit({ emotionCoins: { positive: 2, negative: 1, max: 5 } });
    expect(coinDots(u)).toBe("●●○··");
  });

  it("never produces a negative number of empty dots when totals overshoot max", () => {
    // Defensive — mid-battle deltas can briefly carry positive+negative > max
    // when an emotion shift is in progress; coinDots should not throw.
    const u = unit({ emotionCoins: { positive: 4, negative: 4, max: 5 } });
    expect(coinDots(u)).toBe("●●●●○○○○");
  });

  it("returns an empty string when emotionCoins is absent", () => {
    const u = unit({ emotionCoins: undefined as unknown as Unit["emotionCoins"] });
    expect(coinDots(u)).toBe("");
  });
});

describe("isSlotFilled", () => {
  it("returns true when a slottedCard occupies the slot", () => {
    const u = unit({ slottedCards: [slotted(2)] });
    expect(isSlotFilled(u, 2)).toBe(true);
  });

  it("returns false for an empty slot", () => {
    const u = unit({ slottedCards: [slotted(2)] });
    expect(isSlotFilled(u, 0)).toBe(false);
  });

  it("returns false when slottedCards is missing", () => {
    expect(isSlotFilled(unit({ slottedCards: undefined }), 0)).toBe(false);
  });
});

describe("isMassRange", () => {
  it("recognises the mass-target ranges", () => {
    expect(isMassRange("FarArea")).toBe(true);
    expect(isMassRange("FarAreaEach")).toBe(true);
  });

  it("rejects non-mass ranges, including the deceptively-named NearArea variants", () => {
    // The CardRange enum has no NearArea / NearAreaEach members; mass attacks
    // are exclusively Far in LoR. Locking this prevents a future contributor
    // from speculatively adding NearArea to the set.
    expect(isMassRange("Near")).toBe(false);
    expect(isMassRange("Far")).toBe(false);
    expect(isMassRange("Instance")).toBe(false);
    expect(isMassRange("NearArea")).toBe(false);
  });
});

describe("diceIcon", () => {
  it("maps detail variants to their asset filename segments", () => {
    expect(diceIcon("Atk", "Slash")).toBe("/assets/dice/AtkSlash.png");
    expect(diceIcon("Atk", "Penetrate")).toBe("/assets/dice/AtkPierce.png");
    expect(diceIcon("Atk", "Hit")).toBe("/assets/dice/AtkBlunt.png");
    expect(diceIcon("Def", "Guard")).toBe("/assets/dice/DefGuard.png");
    expect(diceIcon("Def", "Evasion")).toBe("/assets/dice/DefEvade.png");
  });

  it("returns null for an unknown detail", () => {
    expect(diceIcon("Atk", "Mystery")).toBeNull();
  });
});

describe("rarity helpers", () => {
  it("returns the rarity colour for known rarities and a fallback otherwise", () => {
    expect(rarityColor("Common")).toBe("var(--rarity-common)");
    expect(rarityColor("Unique")).toBe("var(--rarity-unique)");
    expect(rarityColor("???")).toBe("#3c3830");
  });

  it("emits the --rarity-color CSS var only when rarity is provided", () => {
    expect(rarityBorderStyle("Rare")).toEqual({
      "--rarity-color": "var(--rarity-rare)",
    });
    expect(rarityBorderStyle(undefined)).toEqual({});
  });
});

describe("costStyle", () => {
  it("returns red on increased cost", () => {
    const style = costStyle({ cost: 5, baseCost: 3 } as Card);
    expect(style?.color).toBe("#ef9a9a");
  });

  it("returns green on decreased cost", () => {
    const style = costStyle({ cost: 1, baseCost: 3 } as Card);
    expect(style?.color).toBe("#81c784");
  });

  it("returns null when cost equals base", () => {
    expect(costStyle({ cost: 3, baseCost: 3 } as Card)).toBeNull();
  });

  it("returns null when baseCost is missing (server omits it for ego/abnormality cards)", () => {
    expect(costStyle({ cost: 3 } as Card)).toBeNull();
  });
});

describe("cardBorderColor", () => {
  it("forces crimson for cards tagged Ego", () => {
    expect(cardBorderColor({ rarity: "Common", options: ["EgoOhArOhsam"] } as Card)).toBe(
      "#c62828",
    );
    expect(cardBorderColor({ rarity: "Common", options: ["EGO"] } as Card)).toBe("#c62828");
  });

  it("falls through to the rarity colour for non-EGO cards", () => {
    expect(cardBorderColor({ rarity: "Rare" } as Card)).toBe("var(--rarity-rare)");
  });
});

describe("dieTypeColor", () => {
  it("maps known die types and falls back to grey", () => {
    expect(dieTypeColor("Atk")).toBe("#c62828");
    expect(dieTypeColor("Def")).toBe("#4fc3f7");
    expect(dieTypeColor("Standby")).toBe("#c9a227");
    expect(dieTypeColor("Other")).toBe("#888");
  });
});

describe("isDead", () => {
  it("treats hp <= 0 as dead", () => {
    expect(isDead(unit({ hp: 0 }))).toBe(true);
    expect(isDead(unit({ hp: -3 }))).toBe(true);
  });

  it("treats positive hp as alive", () => {
    expect(isDead(unit({ hp: 1 }))).toBe(false);
  });
});

describe("toRoman", () => {
  it("converts 0 through 10 to roman numerals", () => {
    expect(toRoman(0)).toBe("0");
    expect(toRoman(4)).toBe("IV");
    expect(toRoman(10)).toBe("X");
  });

  it("returns the raw stringified number for out-of-range inputs", () => {
    expect(toRoman(11)).toBe("11");
    expect(toRoman(-1)).toBe("-1");
  });
});

describe("sortedSlots", () => {
  it("places staggered dice first, then non-staggered by descending value", () => {
    const u = unit({
      speedDice: [die(0, 4), die(1, 7, true), die(2, 6), die(3, 2, true)],
      slottedCards: [slotted(2, "Card on slot 2")],
    });
    const result = sortedSlots(u);
    const order = result.map((r) => r.die.slot);
    // Staggered (slots 1, 3) come first — relative order between them is not
    // contractually defined, so just assert they precede the non-staggered.
    expect(order.indexOf(1)).toBeLessThan(order.indexOf(0));
    expect(order.indexOf(3)).toBeLessThan(order.indexOf(0));
    // Among non-staggered, slot 2 (value 6) precedes slot 0 (value 4).
    expect(order.indexOf(2)).toBeLessThan(order.indexOf(0));
  });

  it("pairs each die with the slottedCard sharing its slot", () => {
    const u = unit({
      speedDice: [die(0, 5), die(1, 3)],
      slottedCards: [slotted(1, "Pair me")],
    });
    const result = sortedSlots(u);
    const slotZero = result.find((r) => r.die.slot === 0);
    const slotOne = result.find((r) => r.die.slot === 1);
    expect(slotZero?.card).toBeUndefined();
    expect(slotOne?.card?.name).toBe("Pair me");
  });

  it("returns an empty array when speedDice is missing", () => {
    expect(sortedSlots(unit({ speedDice: undefined as unknown as SpeedDie[] }))).toEqual([]);
  });
});

describe("formatSpeedDieValue", () => {
  // The threshold mirrors SpeedDiceUI.ChangeSprite's `value >= 999` branch.
  // Any passive that pushes a die past that point renders as ∞ in-game.
  it("renders normal rolled values as their decimal string", () => {
    expect(formatSpeedDieValue(0)).toBe("0");
    expect(formatSpeedDieValue(7)).toBe("7");
    expect(formatSpeedDieValue(99)).toBe("99");
    expect(formatSpeedDieValue(998)).toBe("998");
  });

  it("renders the infinity glyph at and above the threshold", () => {
    expect(formatSpeedDieValue(SPEED_DIE_INFINITY_THRESHOLD)).toBe("∞");
    expect(formatSpeedDieValue(999)).toBe("∞");
    expect(formatSpeedDieValue(1000)).toBe("∞");
    expect(formatSpeedDieValue(2_147_483_647)).toBe("∞");
  });
});
