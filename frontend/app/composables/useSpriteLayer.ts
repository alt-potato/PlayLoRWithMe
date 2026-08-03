/**
 * useSpriteLayer.ts
 *
 * Per-layer 404 tracking for a single composited sprite URL.
 *
 * AppearancePreview composites several independent sprite layers (fashion
 * body/front/skin composites, patron head composites), each needing its own
 * "did this PNG fail to load" flag so a missing sprite hides only its own
 * layer instead of showing a broken-image box. Earlier versions reset each
 * flag with a hand-listed `watch(...)` on every upstream ref the URL happened
 * to be built from. One of those lists silently dropped a dependency — the
 * fashion front-layer URL is built from both the book's file stem and the
 * gender variant suffix, but its reset watcher only listened for the file
 * stem — so a stale failure from one variant kept the layer hidden after
 * switching to a variant whose PNG actually existed.
 *
 * Resetting off the URL itself, rather than a hand-picked list of its
 * ingredients, makes that whole class of bug structurally impossible: any
 * change that produces a different URL is, by construction, observed here.
 */

import { ref, watch } from "vue";
import type { Ref } from "vue";

export interface SpriteLayer {
  /** The URL ref passed in, returned for callers that want a single handle. */
  url: Ref<string | null>;
  /** Whether the most recent load of `url` failed. */
  failed: Ref<boolean>;
  /** Record a load failure for the current `url`. */
  markFailed: () => void;
}

/** Tracks load-failure state for one sprite layer, keyed to its URL. */
export function useSpriteLayer(url: Ref<string | null>): SpriteLayer {
  const failed = ref(false);

  watch(url, () => {
    failed.value = false;
  });

  function markFailed(): void {
    failed.value = true;
  }

  return { url, failed, markFailed };
}
