import type { Theme } from "~/types/game";

/**
 * Writes the runtime-sampled colours from a {@link Theme} block to the
 * document root as CSS custom properties so component `<style>` blocks can
 * read them via `var(--die-ally-fill, …)` etc. Idempotent — safe to call on
 * every hello + state-push receipt; absent or empty blocks leave the root's
 * declared defaults in place.
 *
 * Returning the set of written property names lets tests assert which vars
 * were touched without coupling to the live document.
 */
export function applyTheme(theme: Theme | undefined, root: HTMLElement): string[] {
  if (!theme) return [];
  const written: string[] = [];
  const fd = theme.factionDieColors;
  if (fd?.ally) {
    root.style.setProperty("--die-ally-fill", fd.ally);
    written.push("--die-ally-fill");
  }
  if (fd?.enemy) {
    root.style.setProperty("--die-enemy-fill", fd.enemy);
    written.push("--die-enemy-fill");
  }
  return written;
}
