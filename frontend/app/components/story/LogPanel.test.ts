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
  it("renders the name line only when showsSpeaker allows it", () => {
    // Monologue and choice suppression both route through this one helper, so
    // the template must not reimplement the condition.
    expect(source).toMatch(/v-if="showsSpeaker\(entry\)"/);
  });

  it("orders the name line as title-then-teller, matching the in-game log", () => {
    const nameLine = source.match(
      /<p v-if="showsSpeaker\(entry\)"[\s\S]*?<\/p>/,
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

  it("caps at the game's normal dialogue width as a ceiling, not a proportional scale", () => {
    const rootRule = source.match(/\.slog \{[\s\S]*?\n\}/);
    expect(rootRule, "expected a .slog style block").not.toBeNull();
    // StoryManager.origintextdialogsize.x — the normal dialogue box the player
    // reads against, NOT DialogLogManager.slotWidth (1500), which sizes the
    // log-history rows and is the wrong reference for this panel.
    expect(rootRule![0]).toMatch(/--story-log-max-width:\s*1277px/);
    expect(rootRule![0]).toMatch(/max-width:\s*var\(--story-log-max-width\)/);
    // A percentage width would reintroduce the rejected proportional scaling.
    expect(rootRule![0]).toMatch(/width:\s*100%/);
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

  it("crops the portrait onto the face rather than framing the whole bust", () => {
    // Plain `cover` fits the full bust and lets the hex's point cut through the
    // chest, because the art centres the face at roughly a third of its height.
    const image = source.match(/\.slog-portrait-img \{[\s\S]*?\n\}/);
    expect(image, "expected a .slog-portrait-img style block").not.toBeNull();
    expect(image![0]).toMatch(/scale:\s*var\(--story-log-portrait-zoom\)/);
    expect(image![0]).toMatch(
      /translate:\s*0 var\(--story-log-portrait-offset-y\)/,
    );
    // The zoomed image must be cropped by the frame, not spill past it.
    const frame = source.match(/\.slog-portrait-frame \{[\s\S]*?\n\}/);
    expect(frame![0]).toMatch(/overflow:\s*hidden/);
  });

  it("renders dialogue text in the serif display face, as the game does", () => {
    const contentRule = source.match(/\.slog-content \{[\s\S]*?\n\}/);
    expect(contentRule, "expected a .slog-content style block").not.toBeNull();
    expect(contentRule![0]).toMatch(/font-family:\s*var\(--font-display\)/);
  });

  it("keeps the battle overlay opaque, since a cutscene blocks combat input", () => {
    const overlay = source.match(/\.slog--overlay \{[\s\S]*?\n\}/);
    expect(overlay, "expected a .slog--overlay style block").not.toBeNull();
    expect(overlay![0]).toMatch(/background:\s*var\(--bg-card-2\)/);
    expect(overlay![0]).not.toMatch(/transparent/);
  });

  it("renders the name and the title in the same display face", () => {
    const title = source.match(/\.slog-title \{[\s\S]*?\n\}/);
    const teller = source.match(/\.slog-teller \{[\s\S]*?\n\}/);
    expect(title, "expected a .slog-title style block").not.toBeNull();
    expect(teller, "expected a .slog-teller style block").not.toBeNull();
    expect(title![0]).toMatch(/font-family:\s*var\(--font-display\)/);
    expect(teller![0]).toMatch(/font-family:\s*var\(--font-display\)/);
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
