/**
 * keywordHighlight.ts
 *
 * Pure string parser that splits a combat-card description into alternating
 * plain and keyword segments. A "keyword" is any bracketed run (e.g.
 * `[On Use]`, `[Clash Win]`, `[Counter]`) — the base game highlights these
 * in-place at the UI layer; we mirror that by splitting the text so the
 * display component can wrap each keyword in a styled span.
 *
 * Brackets are stripped from the emitted segment text, matching the base
 * game which drops the brackets and renders only the keyword word(s) in
 * the highlight colour. The surrounding whitespace around the bracketed
 * run is preserved intact on the neighbouring plain segments.
 */

export interface KeywordSegment {
  text: string;
  isKeyword: boolean;
}

const KEYWORD_PATTERN = /\[[^\]]+\]/g;

/**
 * Split `input` into an ordered list of plain / keyword segments.
 *
 * - Empty input → `[]` (so `v-for` renders nothing at all).
 * - No-bracket input → one plain segment with the full string.
 * - Leading or trailing keyword → no empty padding segments are emitted.
 * - Unclosed `[` (or stray `]`) → treated as plain text; the parser never
 *   produces a keyword segment for malformed bracket runs.
 */
export function splitKeywordSegments(input: string): KeywordSegment[] {
  if (input.length === 0) return [];

  const out: KeywordSegment[] = [];
  let cursor = 0;

  // reset lastIndex defensively — the regex is module-level so repeated
  // calls would otherwise inherit state from the previous invocation.
  KEYWORD_PATTERN.lastIndex = 0;

  for (let m = KEYWORD_PATTERN.exec(input); m !== null; m = KEYWORD_PATTERN.exec(input)) {
    if (m.index > cursor) {
      out.push({ text: input.slice(cursor, m.index), isKeyword: false });
    }
    // m[0] is the full `[keyword]` match; slice off the surrounding
    // brackets so only the keyword text is emitted (base-game behaviour).
    out.push({ text: m[0].slice(1, -1), isKeyword: true });
    cursor = m.index + m[0].length;
  }

  if (cursor < input.length) {
    out.push({ text: input.slice(cursor), isKeyword: false });
  }

  return out;
}
