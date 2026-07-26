/**
 * usePassiveStaging.ts
 *
 * Save/Cancel staging state machine for librarian passive attribution.
 *
 * Passive edits are expensive to undo server-side (equipping a source book
 * marks it unavailable to every other librarian), so the UI stages them
 * locally and only commits on an explicit Save. This composable owns that
 * staged view, the diff against server truth, and the commit/cancel
 * transitions.
 *
 * It performs no I/O of its own: server truth arrives as refs and the four
 * mutations arrive as callbacks, which keeps the whole state machine testable
 * without a component, a socket, or a DOM.
 */

import { computed, nextTick, ref, watch } from "vue";
import type { Ref } from "vue";
import type {
  AttributedPassive,
  AvailableKeyPage,
  LibrarianEntry,
  Passive,
} from "~/types/game";

/** Server mutations issued when the user commits staged edits. */
export interface PassiveStagingActions {
  equipSourceBook: (bookInstanceId: number) => Promise<void>;
  unequipSourceBook: (bookInstanceId: number) => Promise<void>;
  attributePassive: (
    sourceInstanceId: number,
    passiveId: number,
    passivePackageId: string,
  ) => Promise<void>;
  removeAttributedPassive: (
    sourceInstanceId: number,
    passiveId: number,
    passivePackageId: string,
  ) => Promise<void>;
}

export interface PassiveStagingOptions {
  /** Server truth for the librarian being edited. */
  lib: Ref<LibrarianEntry>;
  /** Book inventory, used to resolve display names for staged sources. */
  availableKeyPages: Ref<AvailableKeyPage[]>;
  actions: PassiveStagingActions;
}

/** A row in the source key page summary; pending removals render struck. */
export interface SourceSummaryRow {
  id: number;
  pendingRemove: boolean;
}

/** Identity of a passive across key pages (a passive may exist only once). */
function passiveKey(p: Passive): string {
  return `${p.id.id}:${p.id.packageId}`;
}

/** Identity of an attribution, i.e. a passive as supplied by one source book. */
function attrKey(sourceId: number, p: Passive): string {
  return `${sourceId}:${passiveKey(p)}`;
}

export function usePassiveStaging({
  lib,
  availableKeyPages,
  actions,
}: PassiveStagingOptions) {
  // ── Staged state ──────────────────────────────────────────────────────────

  const stagedSourceIds = ref<Set<number>>(new Set());
  const stagedAttributions = ref<AttributedPassive[]>([]);

  function initStaged() {
    stagedSourceIds.value = new Set(lib.value.sourceKeyPageIds ?? []);
    stagedAttributions.value = [...(lib.value.attributedPassives ?? [])];
  }

  initStaged();

  // Server resets passives when the primary key-page changes, so staged state
  // must follow.
  watch(
    () => lib.value.keyPage?.instanceId,
    () => {
      initStaged();
    },
  );

  // UI reads staged values, not server values.
  const sourceKeyPageIds = computed(() => stagedSourceIds.value);
  const attributedPassives = computed(() => stagedAttributions.value);

  const innatePassives = computed(() => {
    const all = lib.value.passives ?? [];
    const attr = lib.value.attributedPassives ?? [];
    if (!attr.length) return all;
    // Remove one matching passive per actually-attributed entry (handles duplicates).
    // Innate derivation uses the *actual* server-side attribution list, since the
    // server authoritatively decides which of the key-page's passive slots are
    // filled by attribution vs. innate.
    const remaining = [...all];
    for (const ap of attr) {
      const key = passiveKey(ap.passive);
      const idx = remaining.findIndex((p) => passiveKey(p) === key);
      if (idx >= 0) remaining.splice(idx, 1);
    }
    return remaining;
  });

  const passiveSlotCount = computed(() => lib.value.passiveSlotCount ?? 0);
  const maxPassiveCost = computed(() => lib.value.maxPassiveCost ?? 0);

  const emptySlotCount = computed(
    () =>
      passiveSlotCount.value -
      innatePassives.value.length -
      attributedPassives.value.length,
  );

  // ── Pending diff (staged vs server) ───────────────────────────────────────

  const actualSourceIds = computed(() => new Set(lib.value.sourceKeyPageIds ?? []));

  const actualAttrKeys = computed(
    () =>
      new Set(
        (lib.value.attributedPassives ?? []).map((ap) =>
          attrKey(ap.sourceInstanceId, ap.passive),
        ),
      ),
  );

  const pendingSourceAdds = computed(() => {
    const out = new Set<number>();
    for (const id of stagedSourceIds.value)
      if (!actualSourceIds.value.has(id)) out.add(id);
    return out;
  });
  const pendingSourceRemoves = computed(() => {
    const out = new Set<number>();
    for (const id of actualSourceIds.value)
      if (!stagedSourceIds.value.has(id)) out.add(id);
    return out;
  });
  const pendingAttrAdds = computed(() =>
    stagedAttributions.value.filter(
      (ap) => !actualAttrKeys.value.has(attrKey(ap.sourceInstanceId, ap.passive)),
    ),
  );
  const pendingAttrRemoves = computed(() => {
    const stagedKeys = new Set(
      stagedAttributions.value.map((ap) => attrKey(ap.sourceInstanceId, ap.passive)),
    );
    return (lib.value.attributedPassives ?? []).filter(
      (ap) => !stagedKeys.has(attrKey(ap.sourceInstanceId, ap.passive)),
    );
  });

  const isDirty = computed(
    () =>
      pendingSourceAdds.value.size > 0 ||
      pendingSourceRemoves.value.size > 0 ||
      pendingAttrAdds.value.length > 0 ||
      pendingAttrRemoves.value.length > 0,
  );

  /** Whether this key page is staged as a passive source. */
  function isStagedSource(instanceId: number): boolean {
    return stagedSourceIds.value.has(instanceId);
  }
  function isPendingSourceAdd(instanceId: number): boolean {
    return pendingSourceAdds.value.has(instanceId);
  }
  function isPendingAttrAdd(ap: AttributedPassive): boolean {
    return !actualAttrKeys.value.has(attrKey(ap.sourceInstanceId, ap.passive));
  }

  // Staged cost = server current minus the costs of pending removals plus the
  // costs of pending additions. Server `currentPassiveCost` already reflects the
  // actual attribution set; we adjust by the diff.
  const stagedPassiveCost = computed(() => {
    let cost = lib.value.currentPassiveCost ?? 0;
    for (const ap of pendingAttrRemoves.value) cost -= ap.passive.cost ?? 0;
    for (const ap of pendingAttrAdds.value) cost += ap.passive.cost ?? 0;
    return cost;
  });

  // Duplicate prevention: a passive (by id+packageId) can appear at most once
  // across innate + attributed. Check the staged view so users can re-attribute
  // a passive they just pending-removed from a different source.
  const stagedPassiveIds = computed(() => {
    const set = new Set<string>();
    for (const p of innatePassives.value) set.add(passiveKey(p));
    for (const ap of stagedAttributions.value) set.add(passiveKey(ap.passive));
    return set;
  });

  function hasDuplicate(p: Passive): boolean {
    return stagedPassiveIds.value.has(passiveKey(p));
  }

  /** Whether the cost cap would be exceeded by attributing a passive with this cost. */
  function wouldExceedCost(passiveCost: number): boolean {
    return stagedPassiveCost.value + passiveCost > maxPassiveCost.value;
  }

  function hasEmptySlots(): boolean {
    return emptySlotCount.value > 0;
  }

  /** Count of passives attributed from each source (staged view). */
  const sourcePassiveCounts = computed(() => {
    const map = new Map<number, number>();
    for (const ap of attributedPassives.value) {
      map.set(ap.sourceInstanceId, (map.get(ap.sourceInstanceId) ?? 0) + 1);
    }
    return map;
  });

  /** Source summary rows: staged sources first, then pending-remove sources. */
  const sourceSummaryRows = computed((): SourceSummaryRow[] => {
    const rows: SourceSummaryRow[] = [];
    for (const id of sourceKeyPageIds.value) rows.push({ id, pendingRemove: false });
    for (const id of pendingSourceRemoves.value) rows.push({ id, pendingRemove: true });
    return rows;
  });

  // ── Stagers ───────────────────────────────────────────────────────────────

  const actionError = ref<string | null>(null);
  const saveBusy = ref(false);

  function equipSource(instanceId: number) {
    const next = new Set(stagedSourceIds.value);
    next.add(instanceId);
    stagedSourceIds.value = next;
  }

  function unequipSource(instanceId: number) {
    const next = new Set(stagedSourceIds.value);
    next.delete(instanceId);
    stagedSourceIds.value = next;
    // Cascade: drop any staged attributions from this source.
    stagedAttributions.value = stagedAttributions.value.filter(
      (ap) => ap.sourceInstanceId !== instanceId,
    );
  }

  /** Undo a pending source removal by re-adding it to the staged set. */
  function undoUnequipSource(instanceId: number) {
    equipSource(instanceId);
  }

  function attributePassive(sourceInstanceId: number, p: Passive) {
    const sourceName = availableKeyPages.value.find(
      (kp) => kp.instanceId === sourceInstanceId,
    )?.name;
    stagedAttributions.value = [
      ...stagedAttributions.value,
      { sourceInstanceId, passive: p, sourceName },
    ];
  }

  function removeAttributed(ap: AttributedPassive) {
    const key = attrKey(ap.sourceInstanceId, ap.passive);
    const idx = stagedAttributions.value.findIndex(
      (x) => attrKey(x.sourceInstanceId, x.passive) === key,
    );
    if (idx >= 0) {
      const next = [...stagedAttributions.value];
      next.splice(idx, 1);
      stagedAttributions.value = next;
    }
  }

  /** Restore a pending-remove attribution to the staged set. */
  function undoRemoveAttributed(ap: AttributedPassive) {
    stagedAttributions.value = [...stagedAttributions.value, ap];
  }

  // ── Save / Cancel ─────────────────────────────────────────────────────────

  async function saveChanges() {
    if (!isDirty.value) return;
    saveBusy.value = true;
    actionError.value = null;
    try {
      // Order matters: drop attributions before unequipping their source, and
      // equip new sources before attributing from them.
      for (const ap of pendingAttrRemoves.value) {
        await actions.removeAttributedPassive(
          ap.sourceInstanceId,
          ap.passive.id.id,
          // EntryId.packageId is numeric on the wire, but the C# handler reads
          // passivePackageId as a string (JsonReader stringifies all scalars
          // anyway, so this just makes the contract explicit).
          String(ap.passive.id.packageId),
        );
      }
      for (const id of pendingSourceRemoves.value) {
        await actions.unequipSourceBook(id);
      }
      for (const id of pendingSourceAdds.value) {
        await actions.equipSourceBook(id);
      }
      for (const ap of pendingAttrAdds.value) {
        await actions.attributePassive(
          ap.sourceInstanceId,
          ap.passive.id.id,
          String(ap.passive.id.packageId),
        );
      }
    } catch (e) {
      actionError.value = String(e);
    } finally {
      saveBusy.value = false;
    }
    // Re-sync staged state to whatever the server actually accepted.
    await nextTick();
    initStaged();
  }

  function cancelChanges() {
    initStaged();
    actionError.value = null;
  }

  return {
    // staged view
    sourceKeyPageIds,
    attributedPassives,
    innatePassives,
    emptySlotCount,
    maxPassiveCost,
    stagedPassiveCost,
    sourcePassiveCounts,
    sourceSummaryRows,
    // pending diff
    pendingAttrRemoves,
    isDirty,
    isStagedSource,
    isPendingSourceAdd,
    isPendingAttrAdd,
    hasDuplicate,
    wouldExceedCost,
    hasEmptySlots,
    // transitions
    equipSource,
    unequipSource,
    undoUnequipSource,
    attributePassive,
    removeAttributed,
    undoRemoveAttributed,
    saveChanges,
    cancelChanges,
    actionError,
    saveBusy,
  };
}
