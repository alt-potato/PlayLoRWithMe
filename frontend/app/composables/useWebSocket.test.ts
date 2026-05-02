/**
 * @vitest-environment happy-dom
 *
 * Storage helpers and the on-connect restore decision for the player's
 * persisted display name. The composable itself is not tested here — it
 * opens a real WebSocket on call and the integration is verified via the
 * manual smoke step in the change's tasks.md. These tests cover the
 * pure pieces that drive the feature's branching.
 *
 * Runs under the happy-dom environment (per-file, opted in above), so
 * `localStorage` is a real Storage-spec implementation rather than a hand
 * stub — the throw-tests temporarily replace methods on it to simulate
 * private-mode / quota-exceeded behaviour.
 */

import { describe, it, expect, beforeEach } from "vitest";
import type { PlayerInfo } from "~/types/game";

import {
  loadStoredDisplayName,
  saveStoredDisplayName,
  pickDisplayNameRestore,
} from "./useWebSocket";

const STORAGE_KEY = "plwm_display_name";

describe("loadStoredDisplayName", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("returns the stored value when present", () => {
    localStorage.setItem(STORAGE_KEY, "Ada");
    expect(loadStoredDisplayName()).toBe("Ada");
  });

  it("returns an empty string when no value is stored", () => {
    expect(loadStoredDisplayName()).toBe("");
  });

  it("returns an empty string when localStorage throws", () => {
    // Simulate a hostile storage environment (private mode with quota
    // exceeded, disabled by policy, etc.). The helper must never let
    // the failure propagate into the WebSocket connect path.
    const original = localStorage.getItem.bind(localStorage);
    localStorage.getItem = () => {
      throw new Error("storage disabled");
    };
    try {
      expect(loadStoredDisplayName()).toBe("");
    } finally {
      localStorage.getItem = original;
    }
  });
});

describe("saveStoredDisplayName", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("writes the value to localStorage", () => {
    saveStoredDisplayName("Ada");
    expect(localStorage.getItem(STORAGE_KEY)).toBe("Ada");
  });

  it("swallows errors when localStorage throws", () => {
    // QuotaExceededError or storage-disabled environments must not break
    // the rename UI flow. The save call must return normally.
    const original = localStorage.setItem.bind(localStorage);
    localStorage.setItem = () => {
      throw new Error("quota exceeded");
    };
    try {
      expect(() => saveStoredDisplayName("Ada")).not.toThrow();
    } finally {
      localStorage.setItem = original;
    }
  });

  it("round-trips through load", () => {
    saveStoredDisplayName("Bea");
    expect(loadStoredDisplayName()).toBe("Bea");
  });
});

describe("pickDisplayNameRestore", () => {
  // Helper to keep tests focused on the rename decision rather than fixture
  // shape — only the fields the helper actually reads are populated.
  function player(sessionId: string, name: string): PlayerInfo {
    return { sessionId, name, units: [] };
  }

  it("returns null when no name is stored", () => {
    const result = pickDisplayNameRestore("", "abc", [player("abc", "Player 1")]);
    expect(result).toBeNull();
  });

  it("returns null when the server already has the stored name (resumed session)", () => {
    const result = pickDisplayNameRestore("Ada", "abc", [player("abc", "Ada")]);
    expect(result).toBeNull();
  });

  it("returns the stored name when the server's view differs (fresh session)", () => {
    const result = pickDisplayNameRestore("Ada", "abc", [player("abc", "Player 3")]);
    expect(result).toBe("Ada");
  });

  it("returns null when this session is not in the player list", () => {
    // Defensive — the server should always include the connecting session
    // in playerList, but the helper must not crash if it doesn't.
    const result = pickDisplayNameRestore("Ada", "abc", [player("xyz", "Bea")]);
    expect(result).toBeNull();
  });

  it("ignores other players' names", () => {
    const result = pickDisplayNameRestore("Ada", "abc", [
      player("xyz", "Ada"),
      player("abc", "Player 7"),
    ]);
    expect(result).toBe("Ada");
  });
});
