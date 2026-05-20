import type { Theme } from "~/types/game";
import { deriveDieColors } from "./color";

/**
 * Writes the runtime-sampled colours from a {@link Theme} block to the
 * document root as CSS custom properties so component `<style>` blocks can
 * read them. Each faction tint is split into a rendered background and numeral
 * pair via {@link deriveDieColors} (the mod samples only the single tint; the
 * game's sprite-tinting produces the dark-background / bright-numeral split we
 * reproduce here). Idempotent — safe to call on every hello + state-push
 * receipt; absent, empty, or malformed entries leave the root's declared
 * defaults in place.
 *
 * Returning the set of written property names lets tests assert which vars
 * were touched without coupling to the live document.
 */
export function applyTheme(theme: Theme | undefined, root: HTMLElement): string[] {
  if (!theme) return [];
  const written: string[] = [];
  const fd = theme.factionDieColors;

  const apply = (faction: "ally" | "enemy", tint: string | undefined) => {
    if (!tint) return;
    const derived = deriveDieColors(tint);
    if (!derived) return;
    root.style.setProperty(`--die-${faction}-bg`, derived.background);
    written.push(`--die-${faction}-bg`);
    root.style.setProperty(`--die-${faction}-num`, derived.numeral);
    written.push(`--die-${faction}-num`);
  };

  apply("ally", fd?.ally);
  apply("enemy", fd?.enemy);
  return written;
}
