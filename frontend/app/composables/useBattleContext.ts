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

import type { InjectionKey, Ref, ComputedRef } from "vue";
import type { AllyUnit, Unit, SessionState, ActionResult } from "~/types/game";

export interface BattleCtx {
  phase: ComputedRef<string>;

  /** True when the stage phase is 'ApplyLibrarianCardPhase'. */
  isSelectPhase: ComputedRef<boolean>;

  /**
   * Set when the player taps an empty speed die slot (step 1 of slot-first flow).
   * Cleared when the player picks a card, cancels, or phase changes.
   */
  selectingSlot: Ref<{
    unitId: number;
    diceSlot: number;
  } | null>;

  /**
   * Set after a non-Instance card's slot is chosen and the player must
   * now pick a target speed die.
   * Carries display info needed by TargetPicker.
   */
  selectingTargetFor: Ref<{
    unitId: number;
    cardIndex: number;
    isEgo: boolean;
    diceSlot: number;
    cardName: string;
    cardRange: string;
  } | null>;

  /**
   * Set after an Instance card with allyTarget=true has its slot chosen.
   * Player must now tap an ally unit to complete the play.
   */
  selectingAllyTargetFor: Ref<{
    unitId: number;
    cardIndex: number;
    isEgo: boolean;
    diceSlot: number;
    cardName: string;
  } | null>;

  /** Called when a hand card is tapped (slot-first step 2). */
  onCardClick: (unitId: number, cardIndex: number, isEgo?: boolean) => void;

  /**
   * Called when the player taps an empty speed die slot (slot-first step 1).
   * Toggles selection on same slot; replaces selection on a different slot.
   */
  onSlotSelectClick: (unit: Unit, diceSlot: number) => void;

  /** Called when the player picks any unit/die as the target. */
  onTargetDieClick: (
    targetUnitId: number,
    targetDiceSlot: number,
  ) => Promise<void>;

  /** Called when the player picks an ally unit as the target (for allyTarget Instance cards). */
  onAllyTargetClick: (targetUnitId: number) => Promise<void>;

  /** Return a slotted card to the unit's hand. */
  onRemoveCard: (unitId: number, diceSlot: number) => Promise<void>;

  /** Cancel all in-progress targeting / slot selection. */
  cancelTargeting: () => void;

  /** Per-ally color keyed by unit id. */
  allyColors: ComputedRef<Record<number, string>>;

  /**
   * For each unit id + die slot, list of {name, color, range} for every
   * attacker (ally OR enemy) currently targeting that slot.
   */
  attackMap: ComputedRef<
    Record<
      number,
      Record<
        number,
        Array<{
          name: string;
          color: string;
          range: string;
        }>
      >
    >
  >;

  /** All units (allies + enemies) for name lookups. */
  allUnits: ComputedRef<(Unit | AllyUnit)[]>;

  /** Called when a remote player picks an abnormality page from the LevelUpUI. */
  onSelectAbnormality: (cardId: number, targetUnitId?: number) => Promise<void>;

  /** The current session (null until the server sends a hello message). */
  session: Ref<SessionState | null>;

  /** Returns true when this session owns (or has uncontested access to) the unit. */
  isOwnUnit: (unitId: number) => boolean;

  /** Claim a librarian for this session. */
  claimUnit: (unitId: number) => Promise<ActionResult>;

  /** Release a previously claimed librarian. */
  releaseUnit: (unitId: number) => Promise<ActionResult>;
}

/** InjectionKey used to share BattleCtx from BattleView down to unit components. */
export const BATTLE_CTX: InjectionKey<BattleCtx> = Symbol("battleCtx");
