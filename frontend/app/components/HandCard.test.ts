/**
 * Regression guards for HandCard's click hit-area.
 *
 * The component itself is template-and-style-heavy with no extractable
 * logic worth isolating; the bug it carries is purely template-level
 * (`@click.stop` on `.hcard-detail` blocked clicks on the right pane from
 * reaching the root selection handler). A full component-mount test would
 * require pulling in `@vue/test-utils` and stubbing every auto-imported
 * helper / child component the SFC consumes, which is heavier than the
 * fix warrants.
 *
 * Instead we snapshot the relevant template fragments and assert they
 * do not regress: the root binds `@click` to `handleClick`, and the
 * detail pane does NOT stop click propagation. The same logic exists in
 * the existing `combat-card-display` spec scenarios — these tests give the
 * spec teeth at build time.
 */

import { describe, it, expect } from "vitest";
import { readFileSync } from "fs";
import { resolve } from "path";

const source = readFileSync(
  resolve(__dirname, "HandCard.vue"),
  "utf-8",
);

describe("HandCard hit area", () => {
  it("binds @click to the root .hcard so the whole card surface is the tap target", () => {
    // Root element is the first element with class="hcard"; @click must live
    // on it so descendant taps bubble up to handleClick.
    const rootOpen = source.match(/<div\s+ref="cardEl"\s+class="hcard"[\s\S]*?>/);
    expect(rootOpen, "expected a root <div ref=\"cardEl\" class=\"hcard\"> block").not.toBeNull();
    expect(rootOpen![0]).toMatch(/@click="handleClick"/);
  });

  it("does not stop click propagation on the detail pane (any of @click.stop / @click.self)", () => {
    // The .hcard-detail block is the always-visible right pane in full mode
    // and the hover overlay in compact mode. Either modifier would swallow
    // clicks before they reach handleClick on the root.
    const detailOpen = source.match(/<div\s+class="hcard-detail"[^>]*>/);
    expect(detailOpen, "expected an opening <div class=\"hcard-detail\"> tag").not.toBeNull();
    expect(detailOpen![0]).not.toMatch(/@click[.\w]*/);
  });

  it("retains the long-press gesture binding on the root (mousedown / touchstart)", () => {
    // Long-press uses press-start events on the root, independent of click;
    // removing @click.stop on the detail pane must not affect it.
    expect(source).toMatch(/@mousedown="onPressStart"/);
    expect(source).toMatch(/@touchstart\.passive="onPressStart"/);
  });
});
