/**
 * Storage helpers and the on-connect restore decision for the player's
 * persisted display name. The composable itself is not tested here — it
 * opens a real WebSocket on call and the integration is verified via the
 * manual smoke step in the change's tasks.md. These tests cover the
 * pure pieces that drive the feature's branching.
 */

import { describe, it, expect, vi, beforeEach } from "vitest";
import type { PlayerInfo } from "~/types/game";

// Vitest defaults to a Node environment (no `localStorage`). Stub a minimal
// in-memory Storage-shaped object before importing the module under test so
// the helpers' `try`/`catch` blocks see real successes and real failures
// (rather than always-throwing access errors that mask the happy path).
const memStore: Record<string, string> = {};
const stubStorage: Storage = {
  get length() {
    return Object.keys(memStore).length;
  },
  clear: () => {
    for (const k of Object.keys(memStore)) delete memStore[k];
  },
  getItem: (key) => (key in memStore ? memStore[key]! : null),
  key: (i) => Object.keys(memStore)[i] ?? null,
  removeItem: (key) => {
    delete memStore[key];
  },
  setItem: (key, value) => {
    memStore[key] = String(value);
  },
};
vi.stubGlobal("localStorage", stubStorage);

const {
  loadStoredDisplayName,
  saveStoredDisplayName,
  pickDisplayNameRestore,
} = await import("./useWebSocket");

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
    const original = stubStorage.getItem;
    stubStorage.getItem = () => {
      throw new Error("storage disabled");
    };
    try {
      expect(loadStoredDisplayName()).toBe("");
    } finally {
      stubStorage.getItem = original;
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
    const original = stubStorage.setItem;
    stubStorage.setItem = () => {
      throw new Error("quota exceeded");
    };
    try {
      expect(() => saveStoredDisplayName("Ada")).not.toThrow();
    } finally {
      stubStorage.setItem = original;
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
