/**
 * useBattleActions.ts
 *
 * Encapsulates battle action dispatch and the slot-first interaction flow.
 * Returns handler functions compatible with BattleCtx.
 */

import type { Ref } from "vue";
import type {
  AllyUnit,
  ClientAction,
  GameState,
  ActionResult,
} from "~/types/game";

interface SelectingSlot {
  unitId: number;
  diceSlot: number;
}

interface SelectingTargetFor {
  unitId: number;
  cardIndex: number;
  isEgo: boolean;
  diceSlot: number;
  cardName: string;
  cardRange: string;
}

interface SelectingAllyTargetFor {
  unitId: number;
  cardIndex: number;
  isEgo: boolean;
  diceSlot: number;
  cardName: string;
}

interface BattleActionsOptions {
  sendAction: (action: ClientAction) => Promise<ActionResult>;
  selectingSlot: Ref<SelectingSlot | null>;
  selectingTargetFor: Ref<SelectingTargetFor | null>;
  selectingAllyTargetFor: Ref<SelectingAllyTargetFor | null>;
  isOwnUnit: (unitId: number) => boolean;
  state: Ref<GameState>;
}

export function useBattleActions({
  sendAction,
  selectingSlot,
  selectingTargetFor,
  selectingAllyTargetFor,
  isOwnUnit,
  state,
}: BattleActionsOptions) {
  const actionError = ref<string | null>(null);
  let errorTimer: ReturnType<typeof setTimeout> | null = null;

  /** Sends an action, showing an error banner on failure (auto-cleared after 3 s). */
  async function doSendAction(action: ClientAction): Promise<boolean> {
    if (errorTimer) {
      clearTimeout(errorTimer);
      errorTimer = null;
    }
    actionError.value = null;
    const { ok, error } = await sendAction(action);
    if (!ok) {
      actionError.value = error ?? "Action failed";
      errorTimer = setTimeout(() => {
        actionError.value = null;
        errorTimer = null;
      }, 3000);
    }
    return ok;
  }

  // ── Card routing (slot-first step 2) ──────────────────────────────────────

  /**
   * After a card is tapped, decides next step based on range:
   * - Instance (no allyTarget): send immediately
   * - Instance + allyTarget: enter ally-target selection
   * - Everything else: enter enemy-target selection
   */
  function routeCard(
    unitId: number,
    cardIndex: number,
    isEgo: boolean,
    diceSlot: number,
  ) {
    const unit = state.value.allies?.find(
      (u: AllyUnit) => u.id === unitId,
    );
    if (!unit) return;
    const cardList = isEgo ? (unit.ego ?? []) : (unit.hand ?? []);
    const card = cardList[cardIndex];
    if (card?.range === "Instance") {
      if (card?.allyTarget) {
        selectingAllyTargetFor.value = {
          unitId,
          cardIndex,
          isEgo,
          diceSlot,
          cardName: card?.name ?? "?",
        };
      } else {
        doSendAction({
          type: "playCard",
          unitId,
          cardIndex,
          diceSlot,
          ...(isEgo ? { isEgo: 1 } : {}),
        });
      }
    } else {
      selectingTargetFor.value = {
        unitId,
        cardIndex,
        isEgo,
        diceSlot,
        cardName: card?.name ?? "?",
        cardRange: card?.range ?? "",
      };
    }
  }

  // ── Interaction handlers ──────────────────────────────────────────────────

  function onCardClick(unitId: number, cardIndex: number, isEgo = false) {
    if (!isOwnUnit(unitId)) return;
    // step 2 re-route: already targeting for this unit -> switch card
    if (selectingTargetFor.value?.unitId === unitId) {
      const diceSlot = selectingTargetFor.value.diceSlot;
      selectingTargetFor.value = null;
      routeCard(unitId, cardIndex, isEgo, diceSlot);
      return;
    }
    // step 1 -> 2: a slot for this unit must be selected first
    if (!selectingSlot.value || selectingSlot.value.unitId !== unitId) return;
    const diceSlot = selectingSlot.value.diceSlot;
    selectingSlot.value = null;
    routeCard(unitId, cardIndex, isEgo, diceSlot);
  }

  function onSlotSelectClick(unit: { id: number }, diceSlot: number) {
    // in step 2, any slot click cancels
    if (selectingTargetFor.value || selectingAllyTargetFor.value) {
      cancelTargeting();
      return;
    }
    // toggle: same slot deselects
    if (
      selectingSlot.value?.unitId === unit.id &&
      selectingSlot.value?.diceSlot === diceSlot
    ) {
      selectingSlot.value = null;
    } else {
      selectingSlot.value = { unitId: unit.id, diceSlot };
    }
  }

  async function onAllyTargetClick(targetUnitId: number) {
    if (!selectingAllyTargetFor.value) return;
    const { unitId, cardIndex, isEgo, diceSlot } =
      selectingAllyTargetFor.value;
    await doSendAction({
      type: "playCard",
      unitId,
      cardIndex,
      diceSlot,
      targetUnitId,
      ...(isEgo ? { isEgo: 1 } : {}),
    });
    selectingAllyTargetFor.value = null;
  }

  async function onTargetDieClick(
    targetUnitId: number,
    targetDiceSlot: number,
  ) {
    if (!selectingTargetFor.value) return;
    const { unitId, cardIndex, isEgo, diceSlot } = selectingTargetFor.value;
    await doSendAction({
      type: "playCard",
      unitId,
      cardIndex,
      diceSlot,
      targetUnitId,
      targetDiceSlot,
      ...(isEgo ? { isEgo: 1 } : {}),
    });
    selectingTargetFor.value = null;
  }

  function cancelTargeting() {
    selectingSlot.value = null;
    selectingTargetFor.value = null;
    selectingAllyTargetFor.value = null;
  }

  async function onRemoveCard(unitId: number, diceSlot: number) {
    await doSendAction({ type: "removeCard", unitId, diceSlot });
    if (
      selectingTargetFor.value?.unitId === unitId &&
      selectingTargetFor.value?.diceSlot === diceSlot
    )
      selectingTargetFor.value = null;
    if (
      selectingAllyTargetFor.value?.unitId === unitId &&
      selectingAllyTargetFor.value?.diceSlot === diceSlot
    )
      selectingAllyTargetFor.value = null;
    if (selectingSlot.value?.unitId === unitId) selectingSlot.value = null;
  }

  async function onSelectAbnormality(
    cardId: number,
    targetUnitId?: number,
  ) {
    await doSendAction(
      targetUnitId !== undefined
        ? { type: "selectAbnormality", cardId, targetUnitId }
        : { type: "selectAbnormality", cardId },
    );
  }

  async function onSelectEgo(choiceId: number) {
    await doSendAction({ type: "selectEgo", choiceId });
  }

  async function onConfirm() {
    await doSendAction({ type: "confirm" });
    selectingSlot.value = null;
    selectingTargetFor.value = null;
  }

  // ── Phase change reset ────────────────────────────────────────────────────

  watch(
    () => state.value.phase,
    () => {
      selectingSlot.value = null;
      selectingTargetFor.value = null;
      selectingAllyTargetFor.value = null;
      if (errorTimer) {
        clearTimeout(errorTimer);
        errorTimer = null;
      }
      actionError.value = null;
    },
  );

  /** Cleanup the error timer on component unmount. */
  function cleanupErrorTimer() {
    if (errorTimer) {
      clearTimeout(errorTimer);
      errorTimer = null;
    }
  }

  // Belt-and-suspenders: tie the timer's lifetime to the owning effect scope so
  // it's always cleared on unmount even if a consumer forgets cleanupErrorTimer.
  onScopeDispose(cleanupErrorTimer);

  return {
    actionError,
    onCardClick,
    onSlotSelectClick,
    onTargetDieClick,
    onAllyTargetClick,
    onRemoveCard,
    onSelectAbnormality,
    onSelectEgo,
    onConfirm,
    cancelTargeting,
    cleanupErrorTimer,
  };
}
