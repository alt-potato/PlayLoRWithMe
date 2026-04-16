/**
 * useBattleOrdering.ts
 *
 * Manages manual unit display order with dead-to-bottom sorting.
 * Allies and enemies can be reordered independently; new/removed units are
 * synced automatically via watchers.
 */

import type { Ref, ComputedRef } from "vue";
import type { AllyUnit, Unit } from "~/types/game";

interface BattleOrderingOptions {
  /** Reactive allies array from the game state. */
  allies: ComputedRef<(Unit | AllyUnit)[]>;
  /** Reactive enemies array from the game state. */
  enemies: ComputedRef<(Unit | AllyUnit)[]>;
}

export function useBattleOrdering({ allies, enemies }: BattleOrderingOptions) {
  const allyOrder = ref<number[]>([]);
  const enemyOrder = ref<number[]>([]);

  /**
   * Reconciles the manual order array with the current unit list,
   * preserving existing order and appending newcomers at the end.
   */
  function syncOrder(
    order: Ref<number[]>,
    units: (Unit | AllyUnit)[] | undefined,
  ) {
    if (!units) return;
    const ids = units.map((u) => u.id);
    order.value = [
      ...order.value.filter((id) => ids.includes(id)),
      ...ids.filter((id) => !order.value.includes(id)),
    ];
  }

  watch(allies, (u) => syncOrder(allyOrder, u), { immediate: true });
  watch(enemies, (u) => syncOrder(enemyOrder, u), { immediate: true });

  /** Creates a computed that sorts units by dead-to-bottom then manual order. */
  function makeSorted(units: Ref<(Unit | AllyUnit)[]>, order: Ref<number[]>) {
    return computed(() =>
      [...units.value].sort((a, b) => {
        const ad = isDead(a) ? 1 : 0,
          bd = isDead(b) ? 1 : 0;
        if (ad !== bd) return ad - bd;
        return order.value.indexOf(a.id) - order.value.indexOf(b.id);
      }),
    );
  }

  const sortedAllies = makeSorted(allies, allyOrder);
  const sortedEnemies = makeSorted(enemies, enemyOrder);

  /** Swaps a living unit with its neighbour in the given direction. */
  function moveUnit(
    order: Ref<number[]>,
    sorted: (Unit | AllyUnit)[],
    unitId: number,
    dir: -1 | 1,
  ) {
    const living = sorted.filter((u) => !isDead(u));
    const di = living.findIndex((u) => u.id === unitId);
    const ni = di + dir;
    if (ni < 0 || ni >= living.length) return;
    const arr = [...order.value];
    const ia = arr.indexOf(unitId),
      ib = arr.indexOf(living[ni]!.id);
    if (ia < 0 || ib < 0) return;
    [arr[ia], arr[ib]] = [arr[ib]!, arr[ia]!];
    order.value = arr;
  }

  /** Returns true when the unit can be moved up (toward index 0) among living units. */
  function canMoveUp(sorted: (Unit | AllyUnit)[], unit: Unit | AllyUnit) {
    if (isDead(unit)) return false;
    return (
      sorted.filter((u) => !isDead(u)).findIndex((u) => u.id === unit.id) > 0
    );
  }

  /** Returns true when the unit can be moved down among living units. */
  function canMoveDown(sorted: (Unit | AllyUnit)[], unit: Unit | AllyUnit) {
    if (isDead(unit)) return false;
    const living = sorted.filter((u) => !isDead(u));
    const i = living.findIndex((u) => u.id === unit.id);
    return i >= 0 && i < living.length - 1;
  }

  function moveAlly(unitId: number, dir: -1 | 1) {
    moveUnit(allyOrder, sortedAllies.value, unitId, dir);
  }
  function moveEnemy(unitId: number, dir: -1 | 1) {
    moveUnit(enemyOrder, sortedEnemies.value, unitId, dir);
  }

  return {
    sortedAllies,
    sortedEnemies,
    moveAlly,
    moveEnemy,
    canMoveUp,
    canMoveDown,
  };
}
