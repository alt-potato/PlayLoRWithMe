import type { GameState, AllyUnit, Unit } from "~/types/game";

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
  const result: Record<string, unknown> = { ...base };

  for (const [key, val] of Object.entries(delta)) {
    if (key === "allies" || key === "enemies") continue;
    if (key === "_removed_allies" || key === "_removed_enemies") continue;
    result[key] = val;
  }

  const removedAllies = (delta["_removed_allies"] as number[] | undefined) ?? [];
  const removedEnemies = (delta["_removed_enemies"] as number[] | undefined) ?? [];

  if ("allies" in delta || removedAllies.length) {
    result["allies"] = mergeUnits(
      (base.allies ?? []) as Unit[],
      (delta["allies"] as Unit[] | undefined) ?? [],
      removedAllies,
    ) as AllyUnit[];
  }

  if ("enemies" in delta || removedEnemies.length) {
    result["enemies"] = mergeUnits(
      (base.enemies ?? []) as Unit[],
      (delta["enemies"] as Unit[] | undefined) ?? [],
      removedEnemies,
    );
  }

  return result as unknown as GameState;
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
