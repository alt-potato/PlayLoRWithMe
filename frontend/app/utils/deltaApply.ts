import { z } from "zod/mini";
import type { GameState, AllyUnit, Unit } from "~/types/game";
import { GameStateSchema } from "~/types/game";

/**
 * Merges a delta payload (from a {"type":"delta"} WebSocket message) into
 * the last-known full GameState. Only fields present in the delta are updated;
 * unchanged fields are copied from the base state by reference.
 *
 * Array merging strategy for allies/enemies:
 *   - Units present in the delta replace their counterpart in the base (by id).
 *   - Units listed in _removed_allies/_removed_enemies are dropped.
 *   - Units absent from the delta are carried over unchanged.
 *   - New units (id not in base) are appended in the order they appear in the delta.
 */
export function applyDelta(base: GameState, delta: Record<string, unknown>): GameState {
  // Shallow copy: unchanged nested objects (e.g. individual units) are shared
  // by reference with the previous state. This is safe because nothing mutates
  // units in-place — Vue replaces gameState.value atomically on each delta.
  const result: Record<string, unknown> = { ...base };

  for (const [key, val] of Object.entries(delta)) {
    if (key === "allies" || key === "enemies") continue;
    if (key === "_removed_allies" || key === "_removed_enemies") continue;
    result[key] = val;
  }

  // Coerce wire-supplied list fields through Array.isArray so a malformed delta
  // (e.g. a non-array _removed_allies) degrades to "no change" instead of
  // throwing on .length / new Set(...) in production.
  const removedAllies = asNumberArray(delta["_removed_allies"]);
  const removedEnemies = asNumberArray(delta["_removed_enemies"]);

  if ("allies" in delta || removedAllies.length) {
    result["allies"] = mergeUnits(
      asUnitArray(base.allies),
      asUnitArray(delta["allies"]),
      removedAllies,
    ) as AllyUnit[];
  }

  if ("enemies" in delta || removedEnemies.length) {
    result["enemies"] = mergeUnits(
      asUnitArray(base.enemies),
      asUnitArray(delta["enemies"]),
      removedEnemies,
    );
  }

  // dev-only contract check: catches server-side regressions that slip an
  // invalid enum value or wrong shape through a delta patch. Tree-shaken from
  // production by `import.meta.dev`. The cast below is still justified in prod
  // because upstream validation in `useWebSocket` already guards the `state`
  // message, so incremental delta patches from a conforming server stay valid.
  if (import.meta.dev) {
    const parsed = z.safeParse(GameStateSchema, result);
    if (!parsed.success) {
      console.error("[wire-contract] applyDelta produced invalid GameState", z.formatError(parsed.error));
    }
  }
  return result as unknown as GameState;
}

/** Returns the value as a number[] when it is an array, otherwise an empty array. */
function asNumberArray(v: unknown): number[] {
  return Array.isArray(v) ? (v as number[]) : [];
}

/** Returns the value as a Unit[] when it is an array, otherwise an empty array. */
function asUnitArray(v: unknown): Unit[] {
  return Array.isArray(v) ? (v as Unit[]) : [];
}

function mergeUnits(base: Unit[], changed: Unit[], removed: number[]): Unit[] {
  const removedSet = new Set(removed);
  const changedMap = new Map(changed.map((u) => [u.id, u]));

  // Rebuild in original order, applying changes and drops.
  const result: Unit[] = [];
  const seen = new Set<number>();

  for (const u of base) {
    if (removedSet.has(u.id)) continue;
    result.push(changedMap.get(u.id) ?? u);
    seen.add(u.id);
  }

  // Append genuinely new units (id not present in base).
  for (const u of changed) {
    if (!seen.has(u.id)) result.push(u);
  }

  return result;
}
