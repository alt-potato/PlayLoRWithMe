/**
 * Covers the four activation paths of the mock fixture resolver:
 *   1. query param wins over localStorage and writes through
 *   2. empty query param clears localStorage and returns null
 *   3. both absent → null
 *   4. query absent but localStorage present → localStorage value
 */

import { describe, it, expect, beforeEach, vi } from "vitest";
import { resolveMockFixture } from "./resolveMockFixture";

const STORAGE_KEY = "plwm_mock_fixture";

interface Setup {
  store: Record<string, string>;
}

function stubEnv(search: string, storageValue: string | null): Setup {
  const store: Record<string, string> = {};
  if (storageValue !== null) store[STORAGE_KEY] = storageValue;

  const fakeLocation = { search };
  // resolveMockFixture reads the global `location`; stubbing `window` alone
  // is not enough because the check is `typeof window !== "undefined"` and
  // then the code reaches for `location.search` directly.
  vi.stubGlobal("window", { location: fakeLocation });
  vi.stubGlobal("location", fakeLocation);
  vi.stubGlobal("localStorage", {
    getItem: (k: string) => (k in store ? store[k] : null),
    setItem: (k: string, v: string) => {
      store[k] = v;
    },
    removeItem: (k: string) => {
      delete store[k];
    },
  });

  return { store };
}

describe("resolveMockFixture", () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
  });

  it("query param wins over localStorage and writes through", () => {
    const { store } = stubEnv("?mock=battle-sampler", "stale");
    expect(resolveMockFixture()).toBe("battle-sampler");
    expect(store[STORAGE_KEY]).toBe("battle-sampler");
  });

  it("empty query param clears localStorage and returns null", () => {
    const { store } = stubEnv("?mock=", "main-librarian");
    expect(resolveMockFixture()).toBeNull();
    expect(store[STORAGE_KEY]).toBeUndefined();
  });

  it("absent query + absent localStorage returns null", () => {
    stubEnv("", null);
    expect(resolveMockFixture()).toBeNull();
  });

  it("absent query + present localStorage returns localStorage value", () => {
    stubEnv("", "emotion-upgrade");
    expect(resolveMockFixture()).toBe("emotion-upgrade");
  });

  it("returns null in a non-browser environment", () => {
    // No globals stubbed — typeof window is 'undefined' in plain node.
    expect(resolveMockFixture()).toBeNull();
  });
});
