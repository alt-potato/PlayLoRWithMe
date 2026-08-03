/**
 * Tests for the manual unit-ordering / dead-to-bottom sort composable behind
 * the battle stage's ally and enemy rows.
 *
 * The composable is I/O-free -- it takes the live unit lists as computed refs
 * and exposes sorted views plus a couple of reorder mutators -- so the whole
 * reconciliation/sort state machine runs under the default `node` vitest
 * environment with no component mount and no DOM, following the pattern of
 * usePassiveStaging.test.ts and useDeckEditStaging.test.ts.
 */

import { describe, it, expect } from "vitest";
import { computed, nextTick, ref } from "vue";
import type { Ref } from "vue";
import type { Unit } from "~/types/game";

import { useBattleOrdering } from "./useBattleOrdering";

// ---------------------------------------------------------------------------
// Fixture builder -- only `id` and `hp` (isDead's input) matter to this
// composable; the rest carry representative defaults so the domain type is
// satisfied without burying the assertions in noise.
// ---------------------------------------------------------------------------

function unit(id: number, overrides: Partial<Unit> = {}): Unit {
  return {
    id,
    hp: 100,
    maxHp: 100,
    staggerGauge: 0,
    maxStaggerGauge: 50,
    staggerThreshold: 50,
    targetable: true,
    turnState: "WAIT_TURN",
    speedDice: [],
    slottedCards: [],
    passives: [],
    buffs: [],
    abnormalities: [],
    emotionLevel: 0,
    emotionCoins: { positive: 0, negative: 0, max: 0 },
    light: 0,
    maxLight: 0,
    reservedLight: 0,
    ...overrides,
  } as Unit;
}

/** Marks a fixture dead per useBattleDisplay's `isDead` (hp <= 0). */
function dead(id: number, overrides: Partial<Unit> = {}): Unit {
  return unit(id, { hp: 0, ...overrides });
}

function ids(units: Unit[]): number[] {
  return units.map((u) => u.id);
}

/** Sets up a fresh ordering instance backed by mutable ally/enemy source refs. */
function setup(initialAllies: Unit[] = [], initialEnemies: Unit[] = []) {
  const alliesSource = ref<Unit[]>(initialAllies) as Ref<Unit[]>;
  const enemiesSource = ref<Unit[]>(initialEnemies) as Ref<Unit[]>;
  const allies = computed(() => alliesSource.value);
  const enemies = computed(() => enemiesSource.value);

  const ordering = useBattleOrdering({ allies, enemies });

  return { ordering, alliesSource, enemiesSource };
}

// ---------------------------------------------------------------------------

describe("useBattleOrdering — dead-to-bottom sort", () => {
  it("sorts dead units to the bottom regardless of input order", () => {
    const { ordering } = setup([dead(1), unit(2), unit(3), dead(4)]);

    expect(ids(ordering.sortedAllies.value)).toEqual([2, 3, 1, 4]);
  });

  it("keeps a fully-living roster in its initial (manual) order", () => {
    const { ordering } = setup([unit(3), unit(1), unit(2)]);

    expect(ids(ordering.sortedAllies.value)).toEqual([3, 1, 2]);
  });

  it("sorts allies and enemies independently", () => {
    const { ordering } = setup([unit(1), dead(2)], [dead(10), unit(11)]);

    expect(ids(ordering.sortedAllies.value)).toEqual([1, 2]);
    expect(ids(ordering.sortedEnemies.value)).toEqual([11, 10]);
  });
});

describe("useBattleOrdering — manual order persistence across state pushes", () => {
  it("preserves a manual reorder across an update that does not change the unit set", async () => {
    const { ordering, alliesSource } = setup([unit(1), unit(2), unit(3)]);

    ordering.moveAlly(3, -1); // 3 <-> 2 among living units
    expect(ids(ordering.sortedAllies.value)).toEqual([1, 3, 2]);

    // a fresh state push with the same ids but new object identity / hp,
    // as happens on every WebSocket delta -- membership is unchanged so the
    // manual order must survive
    alliesSource.value = [
      unit(1, { hp: 90 }),
      unit(2, { hp: 80 }),
      unit(3, { hp: 70 }),
    ];
    await nextTick();

    expect(ids(ordering.sortedAllies.value)).toEqual([1, 3, 2]);
  });
});

describe("useBattleOrdering — reconciliation", () => {
  it("drops a unit that disappears from the live list", async () => {
    const { ordering, alliesSource } = setup([unit(1), unit(2), unit(3)]);

    alliesSource.value = [unit(1), unit(3)];
    await nextTick();

    expect(ids(ordering.sortedAllies.value)).toEqual([1, 3]);
  });

  it("appends a newly-appearing unit at the end of the manual order", async () => {
    const { ordering, alliesSource } = setup([unit(2), unit(1)]);

    ordering.moveAlly(1, -1); // manual order becomes [1, 2]
    expect(ids(ordering.sortedAllies.value)).toEqual([1, 2]);

    alliesSource.value = [unit(2), unit(1), unit(3)];
    await nextTick();

    // newcomer 3 is appended after the existing manual order, not inserted
    // at its position in the incoming array
    expect(ids(ordering.sortedAllies.value)).toEqual([1, 2, 3]);
  });

  it("re-adds a returning unit at the end, not at its old position", async () => {
    const { ordering, alliesSource } = setup([unit(1), unit(2), unit(3)]);

    alliesSource.value = [unit(1), unit(3)]; // 2 drops out
    await nextTick();
    alliesSource.value = [unit(1), unit(2), unit(3)]; // 2 comes back
    await nextTick();

    // 2 lost its original slot and is re-appended at the end
    expect(ids(ordering.sortedAllies.value)).toEqual([1, 3, 2]);
  });
});

describe("useBattleOrdering — stability when a unit dies", () => {
  it("moves a newly-dead unit to the bottom without scrambling the relative order of the living", async () => {
    const { ordering, alliesSource } = setup([unit(1), unit(2), unit(3), unit(4)]);
    expect(ids(ordering.sortedAllies.value)).toEqual([1, 2, 3, 4]);

    // unit 2 dies; the manual order array itself is untouched by this --
    // only the sort's dead-to-bottom rule should reshuffle the *view*
    alliesSource.value = [unit(1), dead(2), unit(3), unit(4)];
    await nextTick();

    expect(ids(ordering.sortedAllies.value)).toEqual([1, 3, 4, 2]);
  });

  it("keeps multiple simultaneous deaths in their original relative order at the bottom", async () => {
    const { ordering, alliesSource } = setup([unit(1), unit(2), unit(3), unit(4)]);

    alliesSource.value = [dead(1), unit(2), dead(3), unit(4)];
    await nextTick();

    expect(ids(ordering.sortedAllies.value)).toEqual([2, 4, 1, 3]);
  });
});

describe("useBattleOrdering — moveUnit / canMoveUp / canMoveDown", () => {
  it("swaps a living unit with its upward neighbour among living units only", () => {
    const { ordering } = setup([unit(1), dead(2), unit(3)]);
    // sorted view is [1, 3, 2] (dead 2 pushed to bottom); living = [1, 3]
    expect(ids(ordering.sortedAllies.value)).toEqual([1, 3, 2]);

    ordering.moveAlly(3, -1);

    expect(ids(ordering.sortedAllies.value)).toEqual([3, 1, 2]);
  });

  it("does nothing when moving the first living unit further up", () => {
    const { ordering } = setup([unit(1), unit(2)]);

    ordering.moveAlly(1, -1);

    expect(ids(ordering.sortedAllies.value)).toEqual([1, 2]);
  });

  it("does nothing when moving the last living unit further down", () => {
    const { ordering } = setup([unit(1), unit(2)]);

    ordering.moveAlly(2, 1);

    expect(ids(ordering.sortedAllies.value)).toEqual([1, 2]);
  });

  it("does nothing when asked to move a dead unit", () => {
    const { ordering } = setup([unit(1), dead(2), unit(3)]);

    ordering.moveAlly(2, -1);

    expect(ids(ordering.sortedAllies.value)).toEqual([1, 3, 2]);
  });

  it("moveEnemy operates on the independent enemy order", () => {
    const { ordering } = setup([], [unit(10), unit(11)]);

    ordering.moveEnemy(11, -1);

    expect(ids(ordering.sortedEnemies.value)).toEqual([11, 10]);
  });

  it("canMoveUp/canMoveDown report boundary and dead-unit restrictions", () => {
    const { ordering } = setup([unit(1), unit(2), dead(3)]);
    const sorted = ordering.sortedAllies.value; // [1, 2, 3]
    const [u1, u2, u3] = sorted;

    expect(ordering.canMoveUp(sorted, u1!)).toBe(false); // first living
    expect(ordering.canMoveDown(sorted, u1!)).toBe(true);

    expect(ordering.canMoveUp(sorted, u2!)).toBe(true);
    expect(ordering.canMoveDown(sorted, u2!)).toBe(false); // last living

    expect(ordering.canMoveUp(sorted, u3!)).toBe(false); // dead
    expect(ordering.canMoveDown(sorted, u3!)).toBe(false); // dead
  });
});
