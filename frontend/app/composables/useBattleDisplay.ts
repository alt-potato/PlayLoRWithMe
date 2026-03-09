/**
 * useBattleDisplay.ts
 *
 * Shared pure display helpers used by BattleView, EnemyUnit, and AllyUnit.
 * All exports are auto-imported by Nuxt; no import statements needed in .vue files.
 */

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
  incoming: "#c62828",
  clash: "#c9a227",
  outgoing: "#4fc3f7",
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
export function resistColor(val: string) {
  return RESIST_COLORS[val] ?? "#555";
}

/** CSS colour for a turn-state badge. */
export function turnColor(val: string) {
  return TURNSTATE_COLORS[val] ?? "#888";
}

/** HP bar fill percentage (0-100), clamped. */
export function hpPct(unit: any) {
  return unit.maxHp > 0 ? Math.min(100, (unit.hp / unit.maxHp) * 100) : 0;
}

/** Stagger gauge fill percentage (0-100), clamped. */
export function sgPct(unit: any) {
  return unit.maxStaggerGauge > 0
    ? Math.min(100, (unit.staggerGauge / unit.maxStaggerGauge) * 100)
    : 0;
}

/** CSS colour for the stagger gauge bar (red when broken, orange when low, blue otherwise). */
export function sgColor(unit: any) {
  const pct = sgPct(unit);
  if (pct <= 0) return "#e53935";
  if (pct < 30) return "#ff9800";
  return "#1976d2";
}

/**
 * Renders emotion coin state as unicode dots:
 *   ● positive coins, ○ negative coins, · empty slots.
 */
export function coinDots(unit: any): string {
  const coins = unit.emotionCoins;
  if (!coins) return "";
  return (
    "●".repeat(coins.positive) +
    "○".repeat(coins.negative) +
    "·".repeat(Math.max(0, coins.max - coins.positive - coins.negative))
  );
}

/** Returns true if any slotted card already occupies the given dice slot. */
export function isSlotFilled(unit: any, slot: number): boolean {
  return unit.slottedCards?.some((sc: any) => sc.slot === slot) ?? false;
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
export function costStyle(card: any): Record<string, string> | null {
  if (card.baseCost == null) return null;
  if (card.cost > card.baseCost)
    return { background: "#2d0a0a", color: "#ef9a9a" }; // increased → red
  if (card.cost < card.baseCost)
    return { background: "#0a1e0a", color: "#81c784" }; // decreased → green
  return null;
}

/** Border colour for a card — EGO overrides to crimson regardless of rarity. */
export function cardBorderColor(card: any): string {
  if (card.options?.some((o: string) => o.startsWith("Ego") || o === "EGO"))
    return "#c62828";
  return rarityColor(card.rarity);
}

export function buffIconUrl(b: any): string {
  return b.icon
    ? `/assets/buficons/${b.icon}.png`
    : "/assets/buficons/_default.png";
}

export function buffClass(b: any): Record<string, boolean> {
  return {
    "buff-tag--positive": b.positive === "Positive",
    "buff-tag--negative": b.positive === "Negative",
  };
}

export function isDead(unit: any): boolean {
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

export function cardTokenIconUrl(b: any): string {
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
export function sortedSlots(unit: any): Array<{ die: any; card: any }> {
  return [...(unit.speedDice ?? [])]
    .sort((a: any, b: any) => {
      if (a.staggered !== b.staggered) return a.staggered ? -1 : 1;
      return b.value - a.value;
    })
    .map((d: any) => ({
      die: d,
      card:
        (unit.slottedCards ?? []).find((sc: any) => sc.slot === d.slot) ?? null,
    }));
}
