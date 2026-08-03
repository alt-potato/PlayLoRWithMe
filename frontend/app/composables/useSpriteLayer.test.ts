/**
 * Tests for useSpriteLayer, the per-sprite-layer 404 tracker used by
 * AppearancePreview.vue.
 *
 * Runs under the default `node` vitest environment: the composable is pure
 * Vue reactivity (ref/watch) with no component, DOM, or I/O, so it can be
 * exercised directly.
 */

import { describe, it, expect } from "vitest";
import { nextTick, ref } from "vue";

import { useSpriteLayer } from "./useSpriteLayer";

describe("useSpriteLayer", () => {
  it("starts with failed = false", () => {
    const layer = useSpriteLayer(ref("/a.png"));
    expect(layer.failed.value).toBe(false);
  });

  it("marks failed on markFailed()", () => {
    const layer = useSpriteLayer(ref("/a.png"));
    layer.markFailed();
    expect(layer.failed.value).toBe(true);
  });

  it("returns the same url ref that was passed in", () => {
    const url = ref<string | null>("/a.png");
    const layer = useSpriteLayer(url);
    expect(layer.url).toBe(url);
  });

  it("resets failed when the url changes to a different value", async () => {
    const url = ref("/fashionbodies_front/12.png");
    const layer = useSpriteLayer(url);
    layer.markFailed();
    expect(layer.failed.value).toBe(true);

    url.value = "/fashionbodies_front/13.png";
    await nextTick();

    expect(layer.failed.value).toBe(false);
  });

  it("does not reset failed on a no-op write (same value)", async () => {
    const url = ref("/a.png");
    const layer = useSpriteLayer(url);
    layer.markFailed();

    url.value = "/a.png"; // same string, no actual change
    await nextTick();

    expect(layer.failed.value).toBe(true);
  });

  // Regression test for the AppearancePreview bug: fashionFrontUrl interpolates
  // both the file stem and the gender variant suffix, but the failed flag used
  // to be reset only when the file stem changed. A variant-only change left a
  // stale failure in place even though the URL — and the PNG it points at —
  // had changed. Resetting off the url ref itself (as useSpriteLayer does)
  // fixes this generically: any change to the derived URL is observed,
  // regardless of which of its ingredients moved.
  it("clears a stale failure when only a variant suffix embedded in the url changes", async () => {
    const fileStem = ref("12");
    const variantSuffix = ref("_f");
    const url = ref(`/assets/fashionbodies_front/${fileStem.value}${variantSuffix.value}.png`);
    const layer = useSpriteLayer(url);

    // The "_f" variant PNG failed to load (e.g. that variant wasn't extracted).
    layer.markFailed();
    expect(layer.failed.value).toBe(true);

    // File stem is unchanged; only the gender variant toggles. The URL still
    // changes, pointing at a PNG that may well exist.
    variantSuffix.value = "_m";
    url.value = `/assets/fashionbodies_front/${fileStem.value}${variantSuffix.value}.png`;
    await nextTick();

    expect(layer.failed.value).toBe(false);
  });
});
