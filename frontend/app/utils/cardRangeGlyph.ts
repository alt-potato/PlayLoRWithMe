/**
 * cardRangeGlyph.ts
 *
 * Shared mapping from a CardRange enum string to the glyph descriptor consumed
 * by CardRangeIcon.vue. Kept as a pure module so it can be covered by a unit
 * test without a DOM test-runner dependency.
 */

/** Id of a custom inline SVG glyph. */
export type CardRangeSvgId = "sword" | "gun" | "triangle-bolt" | "sword-plus";

/**
 * Discriminated glyph choice:
 * - `svg`:     render an inline SVG with the given id.
 * - `unicode`: render the given single-character symbol in a plain span.
 * - `fallback`: render the original range string as text (unknown value).
 */
export type CardRangeGlyph =
  | { kind: "svg"; id: CardRangeSvgId }
  | { kind: "unicode"; symbol: string }
  | { kind: "fallback" };

/** Descriptor for a CardRange: the resolved glyph plus the original string for title/aria. */
export interface CardRangeIconDescriptor {
  /** The raw range string, always preserved for `title` and `aria-label` exposure. */
  label: string;
  glyph: CardRangeGlyph;
}

/** Maps a CardRange enum value to the glyph + label descriptor for display. */
export function cardRangeIconDescriptor(range: string): CardRangeIconDescriptor {
  switch (range) {
    case "Near":
      return { label: range, glyph: { kind: "svg", id: "sword" } };
    case "Far":
      return { label: range, glyph: { kind: "svg", id: "gun" } };
    case "Instance":
      return { label: range, glyph: { kind: "svg", id: "triangle-bolt" } };
    case "Special":
      return { label: range, glyph: { kind: "svg", id: "sword-plus" } };
    case "FarArea":
      return { label: range, glyph: { kind: "unicode", symbol: "Σ" } };
    case "FarAreaEach":
      return { label: range, glyph: { kind: "unicode", symbol: "∀" } };
    default:
      return { label: range, glyph: { kind: "fallback" } };
  }
}
