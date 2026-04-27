/**
 * Tests for applyDelta: unit-merge behaviour and the dev-mode contract check.
 *
 * The dev-mode test relies on `import.meta.dev` being truthy — this is
 * guaranteed by the `define` entry in vitest.config.ts, which replicates the
 * flag Nuxt injects at dev/build time.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { applyDelta } from "./deltaApply";
import { FIXTURE_LOADERS } from "~/dev/fixtures";
import type { GameState } from "~/types/game";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Returns a deep-cloned valid battle GameState from the battle-sampler fixture. */
function battleBase(): GameState {
  return FIXTURE_LOADERS["battle-sampler"]();
}

// ---------------------------------------------------------------------------
// Basic merge behaviour
// ---------------------------------------------------------------------------

describe("applyDelta – scalar field merge", () => {
  it("carries over unchanged fields from the base state", () => {
    const base = battleBase();
    const result = applyDelta(base, { phase: "NewPhase" });
    expect(result.scene).toBe(base.scene);
    expect(result.phase).toBe("NewPhase");
  });

  it("leaves state unchanged for an empty delta", () => {
    const base = battleBase();
    const result = applyDelta(base, {});
    expect(result).toMatchObject({ scene: base.scene, phase: base.phase });
  });
});

// ---------------------------------------------------------------------------
// Ally/enemy unit merging
// ---------------------------------------------------------------------------

describe("applyDelta – unit merging", () => {
  // An id that does not exist in the battle-sampler fixture, used to test
  // appending a genuinely new ally entry.
  const SYNTHETIC_ALLY_ID = 9999;

  it("replaces an ally by id when the delta contains an updated version", () => {
    const base = battleBase();
    const firstAllyId = base.allies![0].id;
    const result = applyDelta(base, {
      allies: [{ ...base.allies![0], hp: 1 }],
    });
    const updated = result.allies!.find((a) => a.id === firstAllyId);
    expect(updated?.hp).toBe(1);
  });

  it("removes an ally listed in _removed_allies", () => {
    const base = battleBase();
    const firstAllyId = base.allies![0].id;
    const result = applyDelta(base, { _removed_allies: [firstAllyId] });
    expect(result.allies!.find((a) => a.id === firstAllyId)).toBeUndefined();
  });

  it("appends a new ally not present in the base", () => {
    const base = battleBase();
    const newAlly = { ...base.allies![0], id: SYNTHETIC_ALLY_ID };
    const result = applyDelta(base, { allies: [newAlly] });
    expect(result.allies!.some((a) => a.id === SYNTHETIC_ALLY_ID)).toBe(true);
  });

  it("removes an enemy listed in _removed_enemies", () => {
    const base = battleBase();
    // The battle-sampler fixture must contain at least one enemy for this test
    // to exercise the removal branch. Fail loudly rather than silently pass.
    expect(base.enemies?.length).toBeGreaterThan(0);
    const firstEnemyId = base.enemies![0].id;
    const result = applyDelta(base, { _removed_enemies: [firstEnemyId] });
    expect(result.enemies!.find((e) => e.id === firstEnemyId)).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// Dev-mode contract check (task 3.3)
// ---------------------------------------------------------------------------

describe("applyDelta – dev-mode contract check", () => {
  // A string that is not a valid BattleUnitTurnState enum value, used to
  // produce a merged state that deliberately fails GameStateSchema validation.
  const INVALID_TURN_STATE = "BOGUS";

  let errSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    errSpy = vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("calls console.error with [wire-contract] prefix when a delta injects an invalid enum value into an ally", () => {
    const base = battleBase();

    // Inject an invalid turnState into the first ally via a delta.
    // INVALID_TURN_STATE is not a member of BattleUnitTurnStateSchema, so the
    // merged result fails GameStateSchema.safeParse and triggers the error log.
    const badAlly = { ...base.allies![0], turnState: INVALID_TURN_STATE };
    const result = applyDelta(base, { allies: [badAlly] });

    expect(errSpy).toHaveBeenCalledWith(
      "[wire-contract] applyDelta produced invalid GameState",
      expect.anything(),
    );

    // The merged result is still returned regardless of the validation failure.
    expect(result.allies!.find((a) => a.id === badAlly.id)?.turnState).toBe(INVALID_TURN_STATE);
  });

  it("does not call console.error when the merged result is valid", () => {
    const base = battleBase();
    applyDelta(base, { phase: "ApplyLibrarianCardPhase" });
    expect(errSpy).not.toHaveBeenCalled();
  });
});
