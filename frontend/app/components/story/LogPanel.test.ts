/**
 * Regression guards for LogPanel's template and styling rules.
 *
 * The panel's decision logic lives in `utils/storyLog.ts` and is unit-tested
 * directly. What remains here is template- and CSS-level: which classes a row
 * gets, that content preserves newlines, that a failed portrait is handled, and
 * that the width cap is a ceiling rather than a proportional scale. Those cannot
 * regress silently without a mount test, and `@vue/test-utils` is not a project
 * dependency — so, following the precedent set by `HandCard.test.ts`, we assert
 * against the SFC source instead.
 */

import { describe, it, expect } from "vitest";
import { readFileSync } from "fs";
import { resolve } from "path";

const source = readFileSync(resolve(__dirname, "LogPanel.vue"), "utf-8");

describe("LogPanel rendering rules", () => {
  it("reserves the name row for spoken lines and fills it only when there is a speaker", () => {
    // Two separate conditions on purpose: the row is reserved for every spoken
    // line (so monologue content stays aligned with the rest), while the name
    // text inside it is gated on there being a speaker to show.
    expect(source).toMatch(/v-if="reservesNameRow\(entry\)"/);
    expect(source).toMatch(/v-if="showsSpeaker\(entry\)"/);
    const nameRule = source.match(/\.slog-name \{[\s\S]*?\n\}/);
    expect(nameRule, "expected a .slog-name style block").not.toBeNull();
    // Without a min-height the reserved row would collapse when empty.
    expect(nameRule![0]).toMatch(/min-height:/);
  });

  it("runs the name rule from the hexagon's edge to the end of the name", () => {
    const nameRule = source.match(/\.slog-name \{[\s\S]*?\n\}/);
    expect(nameRule![0]).toMatch(/border-bottom:[^;]*var\(--gold-dim\)/);
    // Negative start margin reaches back across the row gap to meet the hexagon;
    // fit-content stops the rule at the name instead of ruling off the passage.
    expect(nameRule![0]).toMatch(
      /margin:[^;]*calc\(-1 \* var\(--story-log-portrait-gap\)\)/,
    );
    expect(nameRule![0]).toMatch(/width:\s*fit-content/);
  });

  it("drops the rule on rows with no name while keeping the row reserved", () => {
    expect(source).toMatch(/'slog-name--empty': !showsSpeaker\(entry\)/);
    const emptyRule = source.match(/\.slog-name--empty \{[\s\S]*?\n\}/);
    expect(emptyRule, "expected a .slog-name--empty style block").not.toBeNull();
    // Transparent, not `none`: removing the border would change the box height
    // and break the alignment the reserved row exists to preserve.
    expect(emptyRule![0]).toMatch(/border-bottom-color:\s*transparent/);
  });

  it("renders place captions as a centred interstitial, not a speaker row", () => {
    expect(source).toMatch(/'slog-row--place': isPlaceEntry\(entry\)/);
    const placeRule = source.match(/\.slog-row--place \{[\s\S]*?\n\}/);
    expect(placeRule, "expected a .slog-row--place style block").not.toBeNull();
  });

  it("orders the name line as title-then-teller, matching the in-game log", () => {
    const nameLine = source.match(
      /<p\s+v-if="reservesNameRow\(entry\)"[\s\S]*?<\/p>/,
    );
    expect(nameLine, "expected a .slog-name block").not.toBeNull();
    expect(nameLine![0].indexOf("slog-title")).toBeLessThan(
      nameLine![0].indexOf("slog-teller"),
    );
  });

  it("accents choice rows and distinguishes the red variant", () => {
    expect(source).toMatch(/'slog-row--choice': isChoiceEntry\(entry\)/);
    expect(source).toMatch(
      /'slog-row--red': isChoiceEntry\(entry\) && entry\.choiceIsRed/,
    );
  });

  it("renders content with pre-line so mirrored newlines survive", () => {
    const contentRule = source.match(/\.slog-content \{[\s\S]*?\}/);
    expect(contentRule, "expected a .slog-content style block").not.toBeNull();
    expect(contentRule![0]).toMatch(/white-space:\s*pre-line/);
  });

  it("keeps the empty hex frame when a portrait is missing or fails to load", () => {
    // The in-game log disables only the image and leaves its frame standing, so
    // the frame is gated on the row type while the image is gated on the asset.
    expect(source).toMatch(/v-if="showsPortraitFrame\(entry\)"/);
    expect(source).toMatch(/v-if="visiblePortrait\(entry\)"/);
    expect(source).toMatch(/@error="onPortraitError\(entry\)"/);
  });

  it("sizes the column from the reading measure so text fills it edge to edge", () => {
    const rootRule = source.match(/\.slog \{[\s\S]*?\n\}/);
    expect(rootRule, "expected a .slog style block").not.toBeNull();
    // The column is measure + portrait + gap, so a row's text spans the column
    // exactly rather than trailing off inside a much wider panel.
    expect(rootRule![0]).toMatch(/--story-log-column:\s*calc\(/);
    expect(rootRule![0]).toMatch(/--story-log-measure/);
    expect(rootRule![0]).toMatch(/--story-log-portrait-w/);
    // `ch` must resolve against the font the dialogue is actually set in, or the
    // column and the text measure would be computed from different advances.
    expect(rootRule![0]).toMatch(/font-family:\s*var\(--font-serif\)/);
  });

  it("puts the scrollbar at the page edge by centring a column inside a full-width scroller", () => {
    const inner = source.match(/\.slog-inner \{[\s\S]*?\n\}/);
    expect(inner, "expected a .slog-inner style block").not.toBeNull();
    expect(inner![0]).toMatch(/max-width:\s*var\(--story-log-column\)/);
    expect(inner![0]).toMatch(/margin:\s*0 auto/);
    // The scroller itself must stay full width — bounding it there would move the
    // scrollbar back alongside the text.
    const scroll = source.match(/\.slog-scroll \{[\s\S]*?\n\}/);
    expect(scroll![0]).toMatch(/overflow-y:\s*auto/);
    expect(scroll![0]).not.toMatch(/max-width/);
  });

  it("frames portraits in a pointy-top hexagon, as the in-game log does", () => {
    // Two clipped layers: a real CSS border would be cut away by clip-path, so
    // the outer element's background stands in as the outline.
    const outline = source.match(/\.slog-portrait \{[\s\S]*?\n\}/);
    const frame = source.match(/\.slog-portrait-frame \{[\s\S]*?\n\}/);
    expect(outline, "expected a .slog-portrait style block").not.toBeNull();
    expect(frame, "expected a .slog-portrait-frame style block").not.toBeNull();
    expect(outline![0]).toMatch(/clip-path:\s*var\(--hex-pointy\)/);
    expect(frame![0]).toMatch(/clip-path:\s*var\(--hex-pointy\)/);
    expect(outline![0]).not.toMatch(/border:/);
  });

  it("scales portraits on height alone so every character reads at one size", () => {
    // The art is tightly cropped per character, so native aspects vary widely.
    // `object-fit: cover` scaled the narrow ones by width and zoomed their heads
    // past the wide ones'; fitting height alone normalises them.
    const image = source.match(/\.slog-portrait-img \{[\s\S]*?\n\}/);
    expect(image, "expected a .slog-portrait-img style block").not.toBeNull();
    expect(image![0]).toMatch(/height:\s*100%/);
    expect(image![0]).toMatch(/width:\s*auto/);
    expect(image![0]).not.toMatch(/object-fit:\s*cover/);
  });

  it("crops the portrait onto the face rather than framing the whole bust", () => {
    // The art centres the face at roughly a third of its height, so an unbiased
    // fit puts the hexagon's lower point through the chest.
    const image = source.match(/\.slog-portrait-img \{[\s\S]*?\n\}/);
    expect(image, "expected a .slog-portrait-img style block").not.toBeNull();
    expect(image![0]).toMatch(/scale:\s*var\(--story-log-portrait-zoom\)/);
    expect(image![0]).toMatch(
      /translate:\s*var\(--story-log-portrait-offset-x\) var\(--story-log-portrait-offset-y\)/,
    );
    // The zoomed image must be cropped by the frame, not spill past it.
    const frame = source.match(/\.slog-portrait-frame \{[\s\S]*?\n\}/);
    expect(frame![0]).toMatch(/overflow:\s*hidden/);
  });

  it("renders dialogue in a reading serif, not the small-caps display face", () => {
    // Cinzel is Trajan-derived: its lowercase reads as small caps, which is fine
    // for short headings and wrong for paragraphs of dialogue.
    const contentRule = source.match(/\.slog-content \{[\s\S]*?\n\}/);
    expect(contentRule, "expected a .slog-content style block").not.toBeNull();
    expect(contentRule![0]).toMatch(/font-family:\s*var\(--font-serif\)/);
    for (const rule of [/\.slog-teller \{[\s\S]*?\n\}/, /\.slog-title \{[\s\S]*?\n\}/]) {
      expect(source.match(rule)![0]).toMatch(/font-family:\s*var\(--font-serif\)/);
    }
  });

  it("bounds the dialogue to a reading measure, not just the panel width", () => {
    // The 1277px panel cap sits near a typical laptop viewport, so on its own it
    // left lines running the full width of the screen.
    const contentRule = source.match(/\.slog-content \{[\s\S]*?\n\}/);
    expect(contentRule![0]).toMatch(/max-width:\s*var\(--story-log-measure\)/);
    expect(contentRule![0]).toMatch(/overflow-wrap:\s*break-word/);
  });

  it("fills the height it is given so it scrolls internally and reaches the page bottom", () => {
    // Unbounded, the panel grows to fit and the page scrolls instead — which
    // silently breaks auto-scroll, since scrollTop does nothing on an element
    // that is not itself scrolling.
    expect(source).toMatch(
      /\.slog:not\(\.slog--overlay\) \{[\s\S]*?flex:\s*1[\s\S]*?min-height:\s*0/,
    );
  });

  it("re-sticks on content height changes, not only on a new entry", () => {
    // A rewrapped line or a late web font grows the column after the entry
    // watcher has run, leaving the newest line below the fold.
    expect(source).toMatch(/new ResizeObserver/);
    expect(source).toMatch(/contentObserver\?\.disconnect\(\)/);
  });

  it("disables scroll anchoring, which would hold the view against the stick", () => {
    const scroll = source.match(/\.slog-scroll \{[\s\S]*?\n\}/);
    expect(scroll![0]).toMatch(/overflow-anchor:\s*none/);
  });

  it("keeps the battle overlay opaque, since a cutscene blocks combat input", () => {
    const overlay = source.match(/\.slog--overlay \{[\s\S]*?\n\}/);
    expect(overlay, "expected a .slog--overlay style block").not.toBeNull();
    expect(overlay![0]).toMatch(/background:\s*var\(--bg-card-2\)/);
    expect(overlay![0]).not.toMatch(/transparent/);
  });

  it("renders the name and the title in the same face, differing only in size", () => {
    // Vanilla composes both from one string and varies only the size tag, so a
    // differing typeface would not mirror it. Which face is asserted separately.
    const title = source.match(/\.slog-title \{[\s\S]*?\n\}/);
    const teller = source.match(/\.slog-teller \{[\s\S]*?\n\}/);
    expect(title, "expected a .slog-title style block").not.toBeNull();
    expect(teller, "expected a .slog-teller style block").not.toBeNull();
    const prop = (rule: string, name: string) =>
      rule.match(new RegExp(`${name}:\\s*([^;]+);`))?.[1];
    expect(prop(title![0], "font-family")).toBe(prop(teller![0], "font-family"));
    // Colour too: one Text component draws both in game, so only size differs.
    expect(prop(title![0], "color")).toBe(prop(teller![0], "color"));
    expect(title![0]).toMatch(/font-size:/);
    expect(teller![0]).toMatch(/font-size:/);
  });

  it("exposes a collapse control only in the overlay variant", () => {
    expect(source).toMatch(/<header v-if="collapsible"/);
  });

  it("does not offer any control over cutscene playback", () => {
    // The panel is a read-only mirror; the host drives the cutscene. The collapse
    // toggle is the only interactive control, and it must stay purely local — no
    // action dispatch reaches the server from here.
    const template = source.slice(source.indexOf("<template>"));
    const buttons = template.match(/<button\b/g) ?? [];
    expect(buttons).toHaveLength(1);
    expect(template).toMatch(/<button[^>]*class="slog-toggle"/);
    expect(source).not.toMatch(/sendAction/);
  });
});
