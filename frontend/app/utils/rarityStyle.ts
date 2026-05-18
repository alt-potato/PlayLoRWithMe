/**
 * rarityStyle.ts
 *
 * Resolves a rarity-styled surface (card, key page, passive) to the inline
 * CSS-var object Vue's `:style` binding consumes. Components inline the four
 * `--rarity-*` vars on the surface's root; descendant CSS reads them via
 * `var(--rarity-color, var(--border))` etc. so the surface either picks up
 * the per-rarity vanilla token, the payload-supplied hex override, or the
 * pre-change default.
 *
 * Resolution order per slot:
 *   1. Payload override hex (`rarityColor` / `rarity*Color`), when present
 *      — this is the CustomRarityUtil-resolved value for custom rarities.
 *   2. Vanilla per-rarity token (`var(--rarity-<name>)`), when `rarity` maps
 *      to one of the vanilla enum names.
 *   3. Not emitted — the consuming CSS's `var(--name, <fallback>)` default
 *      handles the unrecognised case.
 */

import { RARITY_COLORS } from "~/composables/useBattleDisplay";

export interface RarityStyleInput {
  rarity?: string;
  rarityColor?: string;
  rarityRangeIconColor?: string;
  rarityAbilityColor?: string;
  rarityKeywordColor?: string;
}

/**
 * Resolves the four rarity-styled CSS vars for a card/key-page/passive
 * surface. Returns a `Record<string, string>` suitable for `:style` —
 * vars that have no resolved value are omitted entirely, so the CSS
 * fallbacks declared via `var(--name, <default>)` take effect.
 */
export function rarityStyle(input: RarityStyleInput): Record<string, string> {
  const out: Record<string, string> = {};

  // --rarity-color: explicit override wins, otherwise look up the vanilla token.
  if (input.rarityColor) {
    out["--rarity-color"] = input.rarityColor;
  } else if (input.rarity) {
    const vanilla = RARITY_COLORS[input.rarity];
    if (vanilla) out["--rarity-color"] = vanilla;
  }

  // The other three vars have no per-rarity vanilla mapping — they were
  // uniform across vanilla rarities. So only an explicit payload override
  // sets them; otherwise the consumer's `var(--name, <default>)` fallback
  // resolves to the global default from app.vue.
  if (input.rarityRangeIconColor)
    out["--rarity-range-icon-color"] = input.rarityRangeIconColor;
  if (input.rarityAbilityColor)
    out["--rarity-ability-color"] = input.rarityAbilityColor;
  if (input.rarityKeywordColor)
    out["--rarity-keyword-color"] = input.rarityKeywordColor;

  return out;
}
