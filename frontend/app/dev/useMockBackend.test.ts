/**
 * Smoke-tests the mock-mode composable:
 *   - known fixtures resolve synchronously into `gameState`
 *   - unknown fixture names surface as a console error and leave `gameState` null
 *   - every action handler resolves `{ok: true}` and logs the payload
 *
 * The real fixture registry is mocked to an isolated table so the test does
 * not depend on the shape of production fixtures.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { effectScope, nextTick } from "vue";

vi.mock("./fixtures", () => ({
  FIXTURE_LOADERS: {
    "test-fixture": () => ({ scene: "title" }),
    "disconnected-fixture": () => ({ scene: "title" }),
  },
  FIXTURE_STATUS_OVERRIDES: {
    "disconnected-fixture": "disconnected",
  },
}));

import { useMockBackend } from "./useMockBackend";

describe("useMockBackend", () => {
  let errSpy: ReturnType<typeof vi.spyOn>;
  let logSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    errSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    logSpy = vi.spyOn(console, "log").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("loads a registered fixture into gameState", async () => {
    const scope = effectScope();
    const backend = scope.run(() => useMockBackend("test-fixture"))!;
    await nextTick();
    expect(backend.gameState.value).toEqual({ scene: "title" });
    expect(backend.status.value).toBe("connected");
    expect(backend.session.value?.sessionId).toBe("mock-session");
    scope.stop();
  });

  it("applies the per-fixture status override (e.g. disconnected) when present", async () => {
    const scope = effectScope();
    const backend = scope.run(() => useMockBackend("disconnected-fixture"))!;
    await nextTick();
    expect(backend.gameState.value).toEqual({ scene: "title" });
    expect(backend.status.value).toBe("disconnected");
    scope.stop();
  });

  it("logs a console error and leaves gameState null for an unknown fixture", async () => {
    const scope = effectScope();
    const backend = scope.run(() => useMockBackend("no-such-fixture"))!;
    await nextTick();
    expect(backend.gameState.value).toBeNull();
    expect(errSpy).toHaveBeenCalledWith(
      expect.stringContaining(`unknown fixture "no-such-fixture"`),
    );
    scope.stop();
  });

  it("action handlers resolve {ok: true} and log the payload", async () => {
    const scope = effectScope();
    const backend = scope.run(() => useMockBackend("test-fixture"))!;

    const sendResult = await backend.sendAction({
      type: "playCard",
      unitId: 1,
      cardIndex: 0,
      diceSlot: 0,
    });
    expect(sendResult).toEqual({ ok: true });

    const claimResult = await backend.claimUnit(42);
    expect(claimResult).toEqual({ ok: true });

    const renameResult = await backend.renamePlayer("  Ada  ");
    expect(renameResult).toEqual({ ok: true });

    // every handler logs with the [mock] action: prefix
    expect(logSpy).toHaveBeenCalledWith(expect.stringMatching(/^\[mock\] action: /));
    // and the trimmed name is in the logged payload
    expect(logSpy).toHaveBeenCalledWith(expect.stringContaining('"name":"Ada"'));

    scope.stop();
  });
});
