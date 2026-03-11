/**
 * useBattleDisplay.ts
 *
 * Shared pure display helpers used by BattleView, EnemyUnit, and AllyUnit.
 * All exports are auto-imported by Nuxt; no import statements needed in .vue files.
 */

import type {
  AllyUnit,
  Buff,
  Card,
  CardToken,
  SlottedCardEntry,
  SpeedDie,
  Unit,
} from "~/types/game";

/** Maps resistance tier name → display colour. */
export const RESIST_COLORS: Record<string, string> = {
  Weak: "#e53935",
  Vulnerable: "#bf360c",
  Normal: "#555",
  Endure: "#2e7d32",
  Resist: "#1565c0",
  Immune: "#6a1b9a",
};

/** Arrow / die highlight colours shared between ArrowOverlay and unit components. */
export const ARROW_COLORS = {
  incoming: "var(--incoming)",
  clash: "var(--clash)",
  outgoing: "var(--outgoing)",
} as const;

/** Maps TurnState enum string → human-readable label. */
export const TURNSTATE_LABELS: Record<string, string> = {
  WAIT_TURN: "WAITING",
  WAIT_CARD: "READY",
  DOING_ACTION: "ACTING",
  DONE_ACTION: "DONE",
  DOING_INTERLACE: "CLASHING",
  DOING_PARRYING: "PARRYING",
  SKIP_TURN: "SKIP",
  BREAK: "STAGGERED",
};

/** Human-readable turn state label. */
export function turnLabel(val: string) {
  return TURNSTATE_LABELS[val] ?? val;
}

/** Maps TurnState enum string → badge background colour. */
export const TURNSTATE_COLORS: Record<string, string> = {
  WAIT_TURN: "#888",
  WAIT_CARD: "#c9a227",
  DOING_ACTION: "#4fc3f7",
  DONE_ACTION: "#444",
  DOING_INTERLACE: "#c9a227",
  DOING_PARRYING: "#4fc3f7",
  SKIP_TURN: "#444",
  BREAK: "#e53935",
};

/** CSS colour for a resistance tier label. */
export function resistColor(val: string | undefined) {
  return (val && RESIST_COLORS[val]) ?? "#555";
}

/** CSS colour for a turn-state badge. */
export function turnColor(val: string) {
  return TURNSTATE_COLORS[val] ?? "#888";
}

/** Calculates the fill percentage of a bar (0-100), clamped. */
export function fillPercentage(val: number, max: number) {
  return max > 0 ? Math.min(100, (val / max) * 100) : 0;
}

/**
 * Renders emotion coin state as unicode dots:
 *   ● positive coins, ○ negative coins, · empty slots.
 */
export function coinDots(unit: Unit | AllyUnit): string {
  const coins = unit.emotionCoins;
  if (!coins) return "";
  return (
    "●".repeat(coins.positive) +
    "○".repeat(coins.negative) +
    "·".repeat(Math.max(0, coins.max - coins.positive - coins.negative))
  );
}

/** Returns true if any slotted card already occupies the given dice slot. */
export function isSlotFilled(unit: Unit, slot: number): boolean {
  return unit.slottedCards?.some((sc) => sc.slot === slot) ?? false;
}

/** CardRange values that hit all enemies (or all allies) in addition to a primary target. */
export const MASS_RANGES = new Set(["FarArea", "FarAreaEach"]);

export function isMassRange(range: string) {
  return MASS_RANGES.has(range);
}

// ── Card display helpers ───────────────────────────────────────────────────

const DETAIL_SEGMENT: Record<string, string> = {
  Slash: "Slash",
  Penetrate: "Pierce",
  Hit: "Blunt",
  Guard: "Guard",
  Evasion: "Evade",
};

/** Returns the dice icon path for a die type+detail pair, or null if unknown. */
export function diceIcon(type: string, detail: string): string | null {
  const seg = DETAIL_SEGMENT[detail];
  return seg ? `/assets/dice/${type}${seg}.png` : null;
}

export const RARITY_COLORS: Record<string, string> = {
  Common: "#2e7d32",
  Uncommon: "#1565c0",
  Rare: "#6a1b9a",
  Unique: "#c9a227",
  Special: "#c62828",
};

/** CSS colour for a card rarity. */
export function rarityColor(rarity: string): string {
  return RARITY_COLORS[rarity] ?? "#3c3830";
}

/**
 * Returns inline style overrides for a cost badge based on cost delta.
 * Returns null when cost equals base (no override needed).
 */
export function costStyle(card: Card): Record<string, string> | null {
  if (card.baseCost == null) return null;
  if (card.cost > card.baseCost)
    return { background: "#2d0a0a", color: "#ef9a9a" }; // increased → red
  if (card.cost < card.baseCost)
    return { background: "#0a1e0a", color: "#81c784" }; // decreased → green
  return null;
}

/** Border colour for a card — EGO overrides to crimson regardless of rarity. */
export function cardBorderColor(card: Card): string {
  if (card.options?.some((o: string) => o.startsWith("Ego") || o === "EGO"))
    return "#c62828";
  return rarityColor(card.rarity ?? "");
}

export function buffIconUrl(b: Buff): string {
  return b.icon
    ? `/assets/buficons/${b.icon}.png`
    : "/assets/buficons/_default.png";
}

export function buffClass(b: Buff): Record<string, boolean> {
  return {
    "buff-tag--positive": b.positive === "Positive",
    "buff-tag--negative": b.positive === "Negative",
  };
}

export function isDead(unit: Unit): boolean {
  return unit.hp <= 0;
}

const ROMAN = [
  "0",
  "I",
  "II",
  "III",
  "IV",
  "V",
  "VI",
  "VII",
  "VIII",
  "IX",
  "X",
];
/**
 * Converts a number to a roman numeral.
 *
 * Supports 0-10, and returns the number as a string if out of range.
 */
export function toRoman(n: number): string {
  return ROMAN[n] ?? String(n);
}

export function cardTokenIconUrl(b: CardToken): string {
  return b.icon
    ? `/assets/cardicons/${b.icon}.png`
    : "/assets/buficons/_default.png";
}

export const DIE_TYPE_COLORS: Record<string, string> = {
  Atk: "#c62828",
  Def: "#4fc3f7",
  Standby: "#c9a227",
};

/** CSS colour for a die type label. */
export function dieTypeColor(type: string): string {
  return DIE_TYPE_COLORS[type] ?? "#888";
}

/** Sort speed dice (staggered first, then descending value) and pair with slotted cards. */
export function sortedSlots(
  unit: Unit,
): Array<{ die: SpeedDie; card: SlottedCardEntry | undefined }> {
  return [...(unit.speedDice ?? [])]
    .sort((a, b) => {
      if (a.staggered !== b.staggered) return a.staggered ? -1 : 1;
      return b.value - a.value;
    })
    .map((d) => ({
      die: d,
      card: (unit.slottedCards ?? []).find((sc) => sc.slot === d.slot),
    }));
}
