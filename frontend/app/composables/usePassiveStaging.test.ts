/**
 * Tests for the Save/Cancel staging state machine behind the librarian
 * PassivesTab.
 *
 * The composable is deliberately I/O-free: it takes server truth as refs and
 * the four mutation callbacks as plain functions, so the whole state machine
 * runs under the default `node` vitest environment with no component mount,
 * no WebSocket, and no DOM. Every branch that the UI relies on — pending
 * diffs, duplicate detection, the cost cap, the unequip cascade, and commit
 * ordering — is exercised here because none of it is visible to a type check.
 */

import { describe, it, expect, vi } from "vitest";
import { nextTick, ref } from "vue";
import type { Ref } from "vue";
import type {
  AttributedPassive,
  AvailableKeyPage,
  LibrarianEntry,
  Passive,
} from "~/types/game";

import { usePassiveStaging } from "./usePassiveStaging";
import type { PassiveStagingActions } from "./usePassiveStaging";

// ---------------------------------------------------------------------------
// Fixture builders — only the fields the staging machine reads are meaningful;
// the rest carry representative defaults so the domain types are satisfied
// without burying the assertions in noise.
// ---------------------------------------------------------------------------

function passive(id: number, overrides: Partial<Passive> = {}): Passive {
  return {
    id: { id, packageId: 0 },
    name: `Passive ${id}`,
    cost: 1,
    ...overrides,
  };
}

function attributed(
  sourceInstanceId: number,
  p: Passive,
  sourceName = `Book ${sourceInstanceId}`,
): AttributedPassive {
  return { sourceInstanceId, passive: p, sourceName };
}

function librarian(overrides: Partial<LibrarianEntry> = {}): LibrarianEntry {
  return {
    floorIndex: 0,
    unitIndex: 0,
    name: "Tester",
    keyPage: { name: "Primary", instanceId: 1 },
    baseKeyPage: { name: "Base", instanceId: 1 },
    passives: [],
    decks: [],
    lockedBy: null,
    passiveSlotCount: 4,
    maxPassiveCost: 10,
    currentPassiveCost: 0,
    ...overrides,
  };
}

function keyPage(
  instanceId: number,
  overrides: Partial<AvailableKeyPage> = {},
): AvailableKeyPage {
  return {
    instanceId,
    name: `Book ${instanceId}`,
    speedMin: 1,
    speedMax: 6,
    bookId: { id: instanceId, packageId: "" },
    chapter: 1,
    bookIcon: "Chapter1",
    bookGroupName: "Chapter 1",
    hp: 40,
    breakGauge: 20,
    equipRangeType: "Melee",
    resistances: {},
    passives: [],
    ...overrides,
  };
}

/** Records every commit callback in invocation order for ordering assertions. */
function recordingActions(): {
  actions: PassiveStagingActions;
  calls: string[];
} {
  const calls: string[] = [];
  return {
    calls,
    actions: {
      equipSourceBook: async (id) => {
        calls.push(`equip:${id}`);
      },
      unequipSourceBook: async (id) => {
        calls.push(`unequip:${id}`);
      },
      attributePassive: async (sourceId, passiveId, packageId) => {
        calls.push(`attribute:${sourceId}:${passiveId}:${packageId}`);
      },
      removeAttributedPassive: async (sourceId, passiveId, packageId) => {
        calls.push(`remove:${sourceId}:${passiveId}:${packageId}`);
      },
    },
  };
}

function setup(
  lib: LibrarianEntry,
  pages: AvailableKeyPage[] = [],
): {
  staging: ReturnType<typeof usePassiveStaging>;
  libRef: Ref<LibrarianEntry>;
  pagesRef: Ref<AvailableKeyPage[]>;
  calls: string[];
} {
  const libRef = ref(lib) as Ref<LibrarianEntry>;
  const pagesRef = ref(pages) as Ref<AvailableKeyPage[]>;
  const { actions, calls } = recordingActions();
  const staging = usePassiveStaging({
    lib: libRef,
    availableKeyPages: pagesRef,
    actions,
  });
  return { staging, libRef, pagesRef, calls };
}

// ---------------------------------------------------------------------------

describe("usePassiveStaging — initial state", () => {
  it("mirrors server truth and reports no pending changes", () => {
    const p1 = passive(1);
    const { staging } = setup(
      librarian({
        passives: [p1],
        attributedPassives: [attributed(10, p1)],
        sourceKeyPageIds: [10],
      }),
    );

    expect([...staging.sourceKeyPageIds.value]).toEqual([10]);
    expect(staging.attributedPassives.value).toHaveLength(1);
    expect(staging.isDirty.value).toBe(false);
    expect(staging.pendingAttrRemoves.value).toEqual([]);
  });

  it("tolerates a librarian with no passive fields at all", () => {
    const { staging } = setup(
      librarian({
        passiveSlotCount: undefined,
        maxPassiveCost: undefined,
        currentPassiveCost: undefined,
      }),
    );

    expect([...staging.sourceKeyPageIds.value]).toEqual([]);
    expect(staging.attributedPassives.value).toEqual([]);
    expect(staging.maxPassiveCost.value).toBe(0);
    expect(staging.stagedPassiveCost.value).toBe(0);
    expect(staging.emptySlotCount.value).toBe(0);
    expect(staging.hasEmptySlots()).toBe(false);
  });

  it("re-initializes staged state when the primary key page changes", async () => {
    const p1 = passive(1);
    const { staging, libRef } = setup(librarian({ sourceKeyPageIds: [10] }));

    staging.equipSource(11);
    expect(staging.isDirty.value).toBe(true);

    // the server wipes passives on a primary key-page swap, so staged edits
    // made against the old page must not survive it
    libRef.value = librarian({
      keyPage: { name: "Other", instanceId: 2 },
      passives: [p1],
      sourceKeyPageIds: [20],
    });
    await nextTick();

    expect([...staging.sourceKeyPageIds.value]).toEqual([20]);
    expect(staging.isDirty.value).toBe(false);
  });
});

describe("usePassiveStaging — innate and slot derivation", () => {
  it("subtracts one passive per attributed entry, keeping duplicates", () => {
    const p1 = passive(1);
    const p2 = passive(2);
    const { staging } = setup(
      librarian({
        // the key page lists p1 twice: one slot is innate, one is filled by
        // the attribution from book 10
        passives: [p1, p1, p2],
        attributedPassives: [attributed(10, p1)],
        sourceKeyPageIds: [10],
      }),
    );

    expect(staging.innatePassives.value.map((p) => p.id.id)).toEqual([1, 2]);
  });

  it("ignores an attribution with no matching key-page passive slot", () => {
    const p1 = passive(1);
    const { staging } = setup(
      librarian({
        // server truth can lag mid-edit: the attribution is recorded but the
        // key page's passive list has not caught up yet
        passives: [p1],
        attributedPassives: [attributed(10, passive(9))],
        sourceKeyPageIds: [10],
      }),
    );

    expect(staging.innatePassives.value.map((p) => p.id.id)).toEqual([1]);
  });

  it("keeps every passive innate when nothing is attributed", () => {
    const { staging } = setup(librarian({ passives: [passive(1), passive(2)] }));
    expect(staging.innatePassives.value).toHaveLength(2);
  });

  it("counts empty slots against innate plus staged attributions", () => {
    const p1 = passive(1);
    const { staging } = setup(
      librarian({ passives: [p1], passiveSlotCount: 3 }),
      [keyPage(10, { passives: [passive(5)] })],
    );

    expect(staging.emptySlotCount.value).toBe(2);
    expect(staging.hasEmptySlots()).toBe(true);

    staging.equipSource(10);
    staging.attributePassive(10, passive(5));
    expect(staging.emptySlotCount.value).toBe(1);

    staging.attributePassive(10, passive(6));
    expect(staging.emptySlotCount.value).toBe(0);
    expect(staging.hasEmptySlots()).toBe(false);
  });
});

describe("usePassiveStaging — duplicate detection", () => {
  it("flags a passive already present innately", () => {
    const p1 = passive(1);
    const { staging } = setup(librarian({ passives: [p1] }));
    expect(staging.hasDuplicate(passive(1))).toBe(true);
    expect(staging.hasDuplicate(passive(2))).toBe(false);
  });

  it("distinguishes same id from different packages", () => {
    const { staging } = setup(
      librarian({ passives: [passive(1, { id: { id: 1, packageId: 0 } })] }),
    );
    expect(staging.hasDuplicate(passive(1, { id: { id: 1, packageId: 7 } }))).toBe(
      false,
    );
  });

  it("flags a passive that is only staged, not yet committed", () => {
    const { staging } = setup(librarian(), [keyPage(10)]);
    expect(staging.hasDuplicate(passive(3))).toBe(false);
    staging.attributePassive(10, passive(3));
    expect(staging.hasDuplicate(passive(3))).toBe(true);
  });

  it("clears the flag once a committed attribution is staged for removal", () => {
    const p1 = passive(1);
    const ap = attributed(10, p1);
    const { staging } = setup(
      librarian({
        passives: [p1],
        attributedPassives: [ap],
        sourceKeyPageIds: [10],
      }),
    );

    // p1 occupies the attributed slot, so it reads as a duplicate ...
    expect(staging.hasDuplicate(p1)).toBe(true);
    staging.removeAttributed(ap);
    // ... until it is pending-removed, which frees it for re-attribution
    // from a different source before the user ever saves
    expect(staging.hasDuplicate(p1)).toBe(false);
  });
});

describe("usePassiveStaging — cost accounting", () => {
  it("starts from the server cost and adjusts by the staged diff", () => {
    const p1 = passive(1, { cost: 3 });
    const ap = attributed(10, p1);
    const { staging } = setup(
      librarian({
        passives: [p1],
        attributedPassives: [ap],
        sourceKeyPageIds: [10],
        currentPassiveCost: 3,
      }),
      [keyPage(10)],
    );

    expect(staging.stagedPassiveCost.value).toBe(3);

    staging.attributePassive(10, passive(2, { cost: 4 }));
    expect(staging.stagedPassiveCost.value).toBe(7);

    staging.removeAttributed(ap);
    expect(staging.stagedPassiveCost.value).toBe(4);
  });

  it("treats a passive with no cost field as free", () => {
    const { staging } = setup(librarian({ currentPassiveCost: 2 }), [keyPage(10)]);
    staging.attributePassive(10, passive(1, { cost: undefined }));
    expect(staging.stagedPassiveCost.value).toBe(2);
  });

  it("allows reaching the cap exactly and rejects exceeding it", () => {
    const { staging } = setup(
      librarian({ maxPassiveCost: 5, currentPassiveCost: 3 }),
    );
    expect(staging.wouldExceedCost(2)).toBe(false);
    expect(staging.wouldExceedCost(3)).toBe(true);
  });
});

describe("usePassiveStaging — staging operations", () => {
  it("marks a newly staged source as a pending add", () => {
    const { staging } = setup(librarian({ sourceKeyPageIds: [10] }));

    expect(staging.isStagedSource(10)).toBe(true);
    expect(staging.isPendingSourceAdd(10)).toBe(false);

    staging.equipSource(11);
    expect(staging.isStagedSource(11)).toBe(true);
    expect(staging.isPendingSourceAdd(11)).toBe(true);
    expect(staging.isDirty.value).toBe(true);
  });

  it("drops staged attributions from a source that gets unequipped", () => {
    const p1 = passive(1);
    const p2 = passive(2);
    const { staging } = setup(
      librarian({
        passives: [p1, p2],
        attributedPassives: [attributed(10, p1), attributed(11, p2)],
        sourceKeyPageIds: [10, 11],
      }),
    );

    staging.unequipSource(10);

    expect(staging.isStagedSource(10)).toBe(false);
    expect(
      staging.attributedPassives.value.map((ap) => ap.sourceInstanceId),
    ).toEqual([11]);
    // the dropped attribution is now a pending remove, not silently lost
    expect(staging.pendingAttrRemoves.value.map((ap) => ap.passive.id.id)).toEqual(
      [1],
    );
  });

  it("undoes a pending source removal, restoring it as a non-pending source", () => {
    const { staging } = setup(librarian({ sourceKeyPageIds: [10] }));

    staging.unequipSource(10);
    expect(staging.sourceSummaryRows.value).toEqual([
      { id: 10, pendingRemove: true },
    ]);

    staging.undoUnequipSource(10);
    expect(staging.sourceSummaryRows.value).toEqual([
      { id: 10, pendingRemove: false },
    ]);
    expect(staging.isDirty.value).toBe(false);
  });

  it("does not restore cascaded attributions when a source unequip is undone", () => {
    const p1 = passive(1);
    const { staging } = setup(
      librarian({
        passives: [p1],
        attributedPassives: [attributed(10, p1)],
        sourceKeyPageIds: [10],
      }),
    );

    staging.unequipSource(10);
    staging.undoUnequipSource(10);

    expect(staging.attributedPassives.value).toEqual([]);
    expect(staging.isDirty.value).toBe(true);
  });

  it("stages an attribution with the source name from the inventory", () => {
    const { staging } = setup(librarian(), [keyPage(10, { name: "Gaze Office" })]);

    staging.attributePassive(10, passive(1));

    const [ap] = staging.attributedPassives.value;
    expect(ap?.sourceName).toBe("Gaze Office");
    expect(staging.isPendingAttrAdd(ap!)).toBe(true);
  });

  it("does not flag an already-committed attribution as a pending add", () => {
    const p1 = passive(1);
    const ap = attributed(10, p1);
    const { staging } = setup(
      librarian({
        passives: [p1],
        attributedPassives: [ap],
        sourceKeyPageIds: [10],
      }),
    );

    expect(staging.isPendingAttrAdd(ap)).toBe(false);
  });

  it("leaves the source name undefined when the page is not in the inventory", () => {
    const { staging } = setup(librarian(), []);
    staging.attributePassive(99, passive(1));
    expect(staging.attributedPassives.value[0]?.sourceName).toBeUndefined();
  });

  it("removes only the matching staged attribution", () => {
    const p1 = passive(1);
    const p2 = passive(2);
    const { staging } = setup(
      librarian({
        passives: [p1, p2],
        attributedPassives: [attributed(10, p1), attributed(10, p2)],
        sourceKeyPageIds: [10],
      }),
    );

    staging.removeAttributed(attributed(10, p1));

    expect(
      staging.attributedPassives.value.map((ap) => ap.passive.id.id),
    ).toEqual([2]);
  });

  it("ignores removal of an attribution that is not staged", () => {
    const p1 = passive(1);
    const { staging } = setup(
      librarian({
        passives: [p1],
        attributedPassives: [attributed(10, p1)],
        sourceKeyPageIds: [10],
      }),
    );

    staging.removeAttributed(attributed(99, passive(42)));

    expect(staging.attributedPassives.value).toHaveLength(1);
    expect(staging.isDirty.value).toBe(false);
  });

  it("undoes a pending attribution removal", () => {
    const p1 = passive(1);
    const ap = attributed(10, p1);
    const { staging } = setup(
      librarian({
        passives: [p1],
        attributedPassives: [ap],
        sourceKeyPageIds: [10],
      }),
    );

    staging.removeAttributed(ap);
    expect(staging.isDirty.value).toBe(true);

    staging.undoRemoveAttributed(ap);
    expect(staging.isDirty.value).toBe(false);
    expect(staging.pendingAttrRemoves.value).toEqual([]);
  });

  it("counts staged attributions per source and lists removals last", () => {
    const p1 = passive(1);
    const p2 = passive(2);
    const { staging } = setup(
      librarian({
        passives: [p1, p2],
        attributedPassives: [attributed(10, p1), attributed(10, p2)],
        sourceKeyPageIds: [10, 11],
      }),
    );

    expect(staging.sourcePassiveCounts.value.get(10)).toBe(2);
    expect(staging.sourcePassiveCounts.value.get(11)).toBeUndefined();

    staging.unequipSource(10);
    expect(staging.sourceSummaryRows.value).toEqual([
      { id: 11, pendingRemove: false },
      { id: 10, pendingRemove: true },
    ]);
  });
});

describe("usePassiveStaging — cancel", () => {
  it("restores pristine state after arbitrary staged edits", () => {
    const p1 = passive(1);
    const { staging } = setup(
      librarian({
        passives: [p1],
        attributedPassives: [attributed(10, p1)],
        sourceKeyPageIds: [10],
      }),
      [keyPage(11)],
    );

    staging.unequipSource(10);
    staging.equipSource(11);
    staging.attributePassive(11, passive(2));
    expect(staging.isDirty.value).toBe(true);

    staging.cancelChanges();

    expect([...staging.sourceKeyPageIds.value]).toEqual([10]);
    expect(
      staging.attributedPassives.value.map((ap) => ap.passive.id.id),
    ).toEqual([1]);
    expect(staging.isDirty.value).toBe(false);
    expect(staging.actionError.value).toBeNull();
  });

  it("clears a previous save error", async () => {
    const libRef = ref(librarian({ sourceKeyPageIds: [] })) as Ref<LibrarianEntry>;
    const staging = usePassiveStaging({
      lib: libRef,
      availableKeyPages: ref([]) as Ref<AvailableKeyPage[]>,
      actions: {
        equipSourceBook: () => Promise.reject(new Error("boom")),
        unequipSourceBook: async () => {},
        attributePassive: async () => {},
        removeAttributedPassive: async () => {},
      },
    });

    staging.equipSource(11);
    await staging.saveChanges();
    expect(staging.actionError.value).toContain("boom");

    staging.cancelChanges();
    expect(staging.actionError.value).toBeNull();
  });
});

describe("usePassiveStaging — commit", () => {
  it("issues removals, unequips, equips, then attributions in that order", async () => {
    const p1 = passive(1);
    const p2 = passive(2);
    const p3 = passive(3, { id: { id: 3, packageId: 4 } });
    const { staging, calls } = setup(
      librarian({
        passives: [p1, p2],
        attributedPassives: [attributed(10, p1), attributed(11, p2)],
        sourceKeyPageIds: [10, 11],
        passiveSlotCount: 6,
      }),
      [keyPage(10), keyPage(11), keyPage(12)],
    );

    // unequipping 11 cascades its attribution into a pending removal
    staging.unequipSource(11);
    staging.equipSource(12);
    staging.attributePassive(12, p3);

    await staging.saveChanges();

    expect(calls).toEqual([
      "remove:11:2:0",
      "unequip:11",
      "equip:12",
      "attribute:12:3:4",
    ]);
  });

  it("does nothing when there are no staged changes", async () => {
    const { staging, calls } = setup(librarian({ sourceKeyPageIds: [10] }));

    await staging.saveChanges();

    expect(calls).toEqual([]);
    expect(staging.saveBusy.value).toBe(false);
  });

  it("flags saveBusy while callbacks are in flight", async () => {
    let release: (() => void) | undefined;
    const libRef = ref(librarian()) as Ref<LibrarianEntry>;
    const staging = usePassiveStaging({
      lib: libRef,
      availableKeyPages: ref([]) as Ref<AvailableKeyPage[]>,
      actions: {
        equipSourceBook: () =>
          new Promise<void>((resolve) => {
            release = resolve;
          }),
        unequipSourceBook: async () => {},
        attributePassive: async () => {},
        removeAttributedPassive: async () => {},
      },
    });

    staging.equipSource(11);
    const pending = staging.saveChanges();
    await nextTick();
    expect(staging.saveBusy.value).toBe(true);

    release?.();
    await pending;
    expect(staging.saveBusy.value).toBe(false);
  });

  it("re-syncs staged state to whatever the server accepted", async () => {
    const { staging, libRef } = setup(librarian({ sourceKeyPageIds: [10] }), [
      keyPage(11),
    ]);

    staging.equipSource(11);
    // the server accepts the equip, so the post-save resync must clear dirty
    libRef.value = librarian({ sourceKeyPageIds: [10, 11] });

    await staging.saveChanges();

    expect([...staging.sourceKeyPageIds.value]).toEqual([10, 11]);
    expect(staging.isDirty.value).toBe(false);
  });

  it("reverts staged state when the server rejects the change", async () => {
    const p1 = passive(1);
    const libRef = ref(
      librarian({
        passives: [p1],
        attributedPassives: [attributed(10, p1)],
        sourceKeyPageIds: [10],
      }),
    ) as Ref<LibrarianEntry>;
    const removeAttributedPassive = vi.fn(() =>
      Promise.reject(new Error("locked by another player")),
    );
    const staging = usePassiveStaging({
      lib: libRef,
      availableKeyPages: ref([]) as Ref<AvailableKeyPage[]>,
      actions: {
        equipSourceBook: async () => {},
        unequipSourceBook: async () => {},
        attributePassive: async () => {},
        removeAttributedPassive,
      },
    });

    staging.removeAttributed(attributed(10, p1));
    await staging.saveChanges();

    expect(removeAttributedPassive).toHaveBeenCalledTimes(1);
    expect(staging.actionError.value).toContain("locked by another player");
    expect(staging.saveBusy.value).toBe(false);
    // the failed edit is rolled back to server truth
    expect(staging.attributedPassives.value).toHaveLength(1);
    expect(staging.isDirty.value).toBe(false);
  });

  it("aborts the remaining callbacks after the first failure", async () => {
    const equipSourceBook = vi.fn(async () => {});
    const libRef = ref(librarian({ sourceKeyPageIds: [10] })) as Ref<LibrarianEntry>;
    const staging = usePassiveStaging({
      lib: libRef,
      availableKeyPages: ref([]) as Ref<AvailableKeyPage[]>,
      actions: {
        equipSourceBook,
        unequipSourceBook: () => Promise.reject(new Error("nope")),
        attributePassive: async () => {},
        removeAttributedPassive: async () => {},
      },
    });

    staging.unequipSource(10);
    staging.equipSource(11);
    await staging.saveChanges();

    expect(equipSourceBook).not.toHaveBeenCalled();
    expect(staging.actionError.value).toContain("nope");
  });
});
