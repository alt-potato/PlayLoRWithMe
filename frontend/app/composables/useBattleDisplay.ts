/**
 * useBattleDisplay.ts
 *
 * Shared pure display helpers used by BattleStage, EnemyUnit, and AllyUnit.
 * All exports are auto-imported by Nuxt; no import statements needed in .vue files.
 */

import type {
  AllyUnit,
  Buff,
  Card,
  CardToken,
  DeckCardPreview,
  SlottedCardEntry,
  SpeedDie,
  Unit,
} from "~/types/game";

/** Ally accent colours indexed by ally order. */
export const ALLY_COLORS = ["#4fc3f7", "#81c784", "#ffb74d", "#ce93d8", "#f48fb1"] as const;

/**
 * Long-press threshold (ms) shared between hand cards and slotted cards so the
 * gesture feels uniform — short enough that users discover detail-on-hold,
 * long enough that an ordinary tap doesn't trigger it.
 */
export const LONG_PRESS_MS = 500;

/** Builds a unitId → hex color map for allies, cycling through ALLY_COLORS. */
export function buildAllyColors(allies: { id: number }[]): Record<number, string> {
  const m: Record<number, string> = {};
  allies.forEach((a, i) => {
    m[a.id] = ALLY_COLORS[i % ALLY_COLORS.length]!;
  });
  return m;
}

/**
 * Resistance kind — `hp` cells use the health-red token, `bp` cells use the
 * stagger-yellow token. The brightness gradient (per tier, below) provides
 * the danger signal; hue stays in the same red/yellow palette as the rest
 * of the UI so the resistance strip doesn't introduce extra colour vocabulary.
 */
export type ResistKind = "hp" | "bp";
const RESIST_HUE: Record<ResistKind, string> = {
  hp: "var(--health-bar)",
  bp: "var(--stagger-bar)",
};

/**
 * Resistance tier display info, keyed by the wire enum value (`AtkResist.ToString()`).
 *
 * The enum names diverge from the in-game player-facing labels:
 *   enum Weak       → player "Fatal"        (2.0× damage)
 *   enum Vulnerable → player "Weak"         (1.5×)
 *   enum Normal     → player "Normal"       (1.0×)
 *   enum Endure     → player "Endured"      (0.5×)
 *   enum Resist     → player "Ineffective"  (0.25×)
 *   enum Immune     → player "Immune"       (0×)
 *
 * `symbol` is the compact display token; `label` is the accessible / hover text.
 *
 * `opacity`, `glow`, and `flat` mirror the in-game brightness gradient: Fatal
 * glows at full intensity, mid tiers dim progressively in the kind hue, and
 * Immune drops to a flat dark grey (`flat: true` overrides the kind hue). The
 * symbol carries the meaning textually, so dimming doesn't hurt legibility —
 * opacity is floor-clamped at ~0.45 so cells stay above the dark-panel
 * contrast threshold.
 */
const RESIST_TIER: Record<
  string,
  { symbol: string; label: string; opacity: number; glow?: boolean; flat?: boolean }
> = {
  Weak:       { symbol: "++", label: "Fatal (2.0×)",        opacity: 1.0,  glow: true },
  Vulnerable: { symbol: "+",  label: "Weak (1.5×)",         opacity: 0.95 },
  Normal:     { symbol: "·",  label: "Normal (1.0×)",       opacity: 0.7 },
  Endure:     { symbol: "−",  label: "Endured (0.5×)",      opacity: 0.55 },
  Resist:     { symbol: "−−", label: "Ineffective (0.25×)", opacity: 0.45 },
  Immune:     { symbol: "∅",  label: "Immune (0×)",         opacity: 0.55, flat: true },
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

/**
 * Inline style object for a resistance cell — sets `color`, `opacity`, and
 * optional `textShadow` (the in-game-style "glow" on Fatal). Intended for
 * `:style` binding on the cell wrapper so both the type icon and the symbol
 * dim/brighten in unison.
 *
 * `kind` selects the hue: `hp` cells use the health-red token, `bp` cells
 * use the stagger-yellow token. Immune ignores `kind` and renders as flat grey.
 */
export function resistStyle(
  val: string | undefined,
  kind: ResistKind,
): Record<string, string | number> {
  const tier = val ? RESIST_TIER[val] : undefined;
  if (!tier) return {};
  const hue = RESIST_HUE[kind];
  const style: Record<string, string | number> = {
    color: tier.flat ? "#666" : hue,
    opacity: tier.opacity,
  };
  if (tier.glow) style.textShadow = `0 0 6px ${hue}`;
  return style;
}

/** Compact symbol for a resistance tier (`++`, `+`, `·`, `−`, `−−`, `∅`). Empty string when unknown. */
export function resistSymbol(val: string | undefined): string {
  return (val && RESIST_TIER[val]?.symbol) ?? "";
}

/** Player-facing label for a resistance tier (e.g. "Fatal (2.0×)"). Used as accessible / hover text. */
export function resistLabel(val: string | undefined): string {
  return (val && RESIST_TIER[val]?.label) ?? "—";
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
  Common: "var(--rarity-common)",
  Uncommon: "var(--rarity-uncommon)",
  Rare: "var(--rarity-rare)",
  Unique: "var(--rarity-unique)",
  Special: "var(--rarity-special)",
};

/** CSS colour for a card rarity. */
export function rarityColor(rarity: string): string {
  return RARITY_COLORS[rarity] ?? "#3c3830";
}

/**
 * Returns an inline-style object that sets `--rarity-color` to the rarity colour,
 * or `{}` when no rarity is provided. Surfaces that opt into the rarity outline
 * (key page tiles, key page detail pane, passive-source tiles) read the variable
 * via `border-color: var(--rarity-color, ...)` so the outline appears only when
 * the wire payload carries `rarity`. Combat-context payloads omit the field, so
 * the surface falls back to its default border colour.
 *
 * This is the rarity-name-only shorthand; surfaces that also need to honour
 * payload-supplied hex overrides (CustomRarityUtil custom rarities) should
 * use {@link "~/utils/rarityStyle".rarityStyle} instead.
 */
export function rarityBorderStyle(
  rarity: string | undefined,
): Record<string, string> {
  return rarity ? { "--rarity-color": rarityColor(rarity) } : {};
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

/** Border colour for a card — EGO overrides to crimson regardless of rarity.
 *  A payload-supplied `rarityColor` (CustomRarityUtil custom rarity) wins over
 *  the vanilla-name lookup so custom-rarity cards show the modder-declared
 *  border colour instead of falling back to the unknown-rarity default. */
export function cardBorderColor(card: Card): string {
  if (card.options?.some((o: string) => o.startsWith("Ego") || o === "EGO"))
    return "#c62828";
  if (card.rarityColor) return card.rarityColor;
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

/**
 * Returns true when an enemy unit accepts target-taps at the unit level.
 *
 * Mirrors vanilla `BattleUnitModel.IsTargetable(null)` (the wire's
 * `targetable` field, false for `_isKnockout` / Justitia-style invincibility /
 * NotTargetable buffs) plus a defensive `!isDead` guard so the affordance
 * disappears the same frame the DEAD badge appears — closing the transient
 * window where `hp <= 0` is observable before `_isKnockout` flips.
 *
 * Does NOT consult per-selection state (BigBird_Eye fixed-targets); that lives
 * in `BattleCtx.isRestrictedTarget` because it depends on the currently
 * selected actor.
 */
export function isUnitTargetable(unit: Unit): boolean {
  return unit.targetable !== false && !isDead(unit);
}

/**
 * Returns true when a single enemy speed die accepts a target-tap.
 *
 * Mirrors vanilla `SpeedDiceUI.OnClickSpeedDice`'s only early-return:
 * `!view.model.speedDiceResult[idx].isControlable`. Per-die staggered
 * (`_bBreakedDice`) and the Stun lock overlay (`hasStun && breaked`) do NOT
 * gate the click in vanilla — `IsTargetableUnit`'s same-faction `isControlable`
 * check is guarded by `actor.faction == target.faction` and is skipped
 * entirely for player→enemy attacks.
 */
export function isDieTargetable(die: SpeedDie): boolean {
  return die.controllable !== false;
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

/**
 * Converts a DeckCardPreview to a minimal Card shape for HandCard rendering.
 * id/index are set to the list position — HandCard does not use them for actions.
 */
export function previewToCard(p: DeckCardPreview, i: number): Card {
  return {
    id: { id: i, packageId: 0 },
    index: i,
    name: p.name,
    cost: p.cost,
    range: p.range,
    rarity: p.rarity,
    dice: p.dice,
    abilityDesc: p.abilityDesc,
    // CustomRarityUtil overrides — propagate so HandCard tints the deck-preview
    // tile with the modder-declared colours instead of falling back to the
    // unknown-rarity default.
    rarityColor: p.rarityColor,
    rarityRangeIconColor: p.rarityRangeIconColor,
    rarityAbilityColor: p.rarityAbilityColor,
    rarityKeywordColor: p.rarityKeywordColor,
  };
}

/**
 * Speed-die numeral threshold above which the game replaces the digits with an
 * infinity glyph (e.g. The Strongest's speed-max passive). Matches the literal
 * `value >= 999` branch in `SpeedDiceUI.ChangeSprite`.
 */
export const SPEED_DIE_INFINITY_THRESHOLD = 999;

/**
 * Format a speed-die numeral for display. Mirrors vanilla: values at or above
 * the infinity threshold render as `∞` instead of the raw number.
 */
export function formatSpeedDieValue(value: number): string {
  if (value >= SPEED_DIE_INFINITY_THRESHOLD) return "∞";
  return String(value);
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
