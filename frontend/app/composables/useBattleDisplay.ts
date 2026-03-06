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
  WAIT_CARD: "READY",
  WAIT_TURN: "WAITING",
  DONE_ACTION: "DONE",
  BREAK: "STAGGERED",
  DEAD: "DEAD",
  MOVE: "MOVING",
  STAND_BY: "STANDBY",
};

/** Human-readable turn state label. */
export function turnLabel(val: string) {
  return TURNSTATE_LABELS[val] ?? val;
}

/** Maps TurnState enum string → badge background colour. */
export const TURNSTATE_COLORS: Record<string, string> = {
  WAIT_CARD: "#c9a227",
  ACTION_WAITING: "#c9a227",
  BREAK: "#e53935",
  ACTION_BREAK: "#e53935",
  DEAD: "#444",
  MOVE: "#888",
  STAND_BY: "#888",
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

/** Border colour for a card — EGO overrides to crimson regardless of rarity. */
export function cardBorderColor(card: any): string {
  if (card.options?.some((o: string) => o.startsWith("Ego") || o === "EGO"))
    return "#c62828";
  return rarityColor(card.rarity);
}

export const DIE_TYPE_COLORS: Record<string, string> = {
  Atk: "#c62828",
  Def: "#4fc3f7",
  Standby: "#786e5e",
};

/** CSS colour for a die type label. */
export function dieTypeColor(type: string): string {
  return DIE_TYPE_COLORS[type] ?? "#786e5e";
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
