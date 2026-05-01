/**
 * Frontend-side label resolution for multi-deck key pages. Labels track the
 * in-game English UI text — for Purple Tear those come from
 * `BattleEffectTextsXmlList.GetEffectTextName(keywordId)` against the four
 * `StanceSlash` / `StancePenetrate` / `StanceHit` / `StanceDefense` keyword
 * IDs declared on the corresponding `BattleUnitBuf_purple*` classes. The
 * keyword *IDs* are internal C# names (Penetrate/Hit/Defense); the
 * *localized text* is what the player sees in the buf list (Pierce / Blunt
 * / Guard).
 *
 * Lookup is keyed by `"<packageId>:<id>"` so workshop and base IDs never
 * collide. Unknown multi-deck key pages — including any future vanilla
 * additions and mod-authored books with the `BookOption.MultiDeck` flag —
 * fall back to generic "Deck 1–4" so the editor still functions.
 */

/** Label fallback when a multi-deck key page has no entry in `KNOWN_MULTI_DECK_LABELS`. */
export const FALLBACK_DECK_LABELS: readonly string[] = [
  "Deck 1",
  "Deck 2",
  "Deck 3",
  "Deck 4",
];

/**
 * Vetted stance/deck name overrides for known multi-deck key pages.
 * Order matches the engine's deck-index-to-stance mapping (the indices passed
 * to `unitData.GetDeckForBattle(idx)` from each `ChangeStance_*` method).
 *
 * Strings here are the player-facing English buf names — identical to what
 * `BattleEffectTextsXmlList.GetEffectTextName(keywordId)` returns in-game,
 * not the underlying C# keyword identifier.
 */
export const KNOWN_MULTI_DECK_LABELS: Record<string, readonly string[]> = {
  // The Purple Tear — PassiveAbility_250127. Indexed by the PurpleStance enum
  // (Slash, Penetrate, Hit, Defense). Keyword IDs StanceSlash / StancePenetrate
  // / StanceHit / StanceDefense localize to the in-game labels below.
  "0:250035": ["Slash", "Pierce", "Blunt", "Guard"],
};

/** Builds the lookup key from a key page's package id and numeric id. */
function keyFor(packageId: string | number | undefined, id: number | undefined): string | null {
  if (id == null) return null;
  // Empty / undefined packageId is treated as the base game ("0"), matching
  // the convention used elsewhere when a LorId has no workshop prefix.
  const pkg = packageId == null || packageId === "" ? "0" : String(packageId);
  return `${pkg}:${id}`;
}

/**
 * Returns the four deck labels for the given key page identity. Always
 * returns a length-4 array — call sites can trust the indexing without
 * additional bounds checks.
 */
export function resolveDeckLabels(
  packageId: string | number | undefined,
  id: number | undefined,
): readonly string[] {
  const k = keyFor(packageId, id);
  if (k && KNOWN_MULTI_DECK_LABELS[k]) return KNOWN_MULTI_DECK_LABELS[k];
  return FALLBACK_DECK_LABELS;
}
