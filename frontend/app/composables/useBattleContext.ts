/**
 * useBattleContext.ts
 *
 * Typed provide/inject key for the interactive battle state shared between
 * BattleView (provider) and EnemyUnit / AllyUnit (consumers).
 *
 * Usage in BattleView:
 *   provide(BATTLE_CTX, { isSelectPhase, selectingSlotFor, ... })
 *
 * Usage in child components:
 *   const ctx = inject(BATTLE_CTX)!
 */

import type { InjectionKey, Ref, ComputedRef } from 'vue'

export interface BattleCtx {
  /** True when the stage phase is 'ApplyLibrarianCardPhase'. */
  isSelectPhase: ComputedRef<boolean>

  /**
   * Set when a hand card is tapped first (card-first flow).
   * Cleared when the player picks a slot or cancels.
   */
  selectingSlotFor: Ref<{ unitId: number; cardIndex: number; isEgo: boolean } | null>

  /**
   * Set after a non-Instance card's slot is chosen and the player must
   * now pick a target speed die.
   * Carries display info needed by TargetPicker.
   */
  selectingTargetFor: Ref<{
    unitId: number; cardIndex: number; isEgo: boolean; diceSlot: number;
    cardName: string; cardRange: string;
  } | null>

  /**
   * Set after an Instance card with allyTarget=true has its slot chosen.
   * Player must now tap an ally unit to complete the play.
   */
  selectingAllyTargetFor: Ref<{
    unitId: number; cardIndex: number; isEgo: boolean; diceSlot: number;
    cardName: string;
  } | null>

  /** Toggle card selection; second tap on same card cancels. */
  onCardClick: (unitId: number, cardIndex: number, isEgo?: boolean) => void

  /**
   * Called when the player picks a dice slot to play a card into.
   * For Instance-range cards the action is sent immediately (no target needed).
   * For all other ranges it transitions to targeting mode.
   */
  onSlotClick: (unit: any, cardIndex: number, diceSlot: number) => Promise<void>

  /** Called when the player picks any unit/die as the target. */
  onTargetDieClick: (targetUnitId: number, targetDiceSlot: number) => Promise<void>

  /** Called when the player picks an ally unit as the target (for allyTarget Instance cards). */
  onAllyTargetClick: (targetUnitId: number) => Promise<void>

  /** Return a slotted card to the unit's hand. */
  onRemoveCard: (unitId: number, diceSlot: number) => Promise<void>

  /** Per-ally color keyed by unit id. */
  allyColors: ComputedRef<Record<number, string>>

  /**
   * For each unit id + die slot, list of {name, color, range} for every
   * attacker (ally OR enemy) currently targeting that slot.
   */
  attackMap: ComputedRef<Record<number, Record<number, Array<{
    name: string; color: string; range: string;
  }>>>>

  /** All units (allies + enemies) for name lookups. */
  allUnits: ComputedRef<any[]>
}

/** InjectionKey used to share BattleCtx from BattleView down to unit components. */
export const BATTLE_CTX: InjectionKey<BattleCtx> = Symbol('battleCtx')
