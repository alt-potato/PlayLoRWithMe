/**
 * Tests for the slot-first battle interaction flow in useBattleActions.ts.
 *
 * The composable is I/O-free apart from the injected `sendAction` callback,
 * so the whole routing / selection state machine runs under the default
 * `node` vitest environment with no component mount, no WebSocket, and no
 * DOM — following the same pattern as usePassiveStaging.test.ts and
 * useDeckEditStaging.test.ts.
 *
 * The three selection refs (`selectingSlot`, `selectingTargetFor`,
 * `selectingAllyTargetFor`) are not exported as named types from
 * useBattleActions.ts, so the shapes below mirror the inline types declared
 * on `BattleCtx` in useBattleContext.ts (the two must already stay in sync
 * for the app to type-check).
 */

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { effectScope, nextTick, ref } from "vue";
import type { Ref } from "vue";
import type {
  ActionResult,
  AllyUnit,
  Card,
  ClientAction,
  GameState,
  SessionState,
} from "~/types/game";

import { useBattleActions } from "./useBattleActions";

// ---------------------------------------------------------------------------
// Selection ref shapes (mirrors BattleCtx in useBattleContext.ts)
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Fixture builders — only the fields the routing logic reads are meaningful;
// the rest carry representative defaults so the domain types are satisfied
// without burying the assertions in noise.
// ---------------------------------------------------------------------------

function card(index: number, overrides: Partial<Card> = {}): Card {
  return {
    id: { id: index, packageId: 0 },
    index,
    name: `Card ${index}`,
    cost: 1,
    range: "Near",
    ...overrides,
  };
}

function ally(id: number, overrides: Partial<AllyUnit> = {}): AllyUnit {
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
    hand: [],
    ego: [],
    ...overrides,
  } as AllyUnit;
}

function gameState(allies: AllyUnit[], phase = "ApplyLibrarianCardPhase"): GameState {
  return { scene: "battle", phase, allies } as GameState;
}

/** Sets up a fresh composable instance with fully-mocked injected refs/callbacks. */
function setup(opts: {
  allies?: AllyUnit[];
  phase?: string;
  isOwnUnit?: (unitId: number) => boolean;
  sendAction?: (action: ClientAction) => Promise<ActionResult>;
} = {}) {
  const state = ref(
    gameState(opts.allies ?? [ally(1)], opts.phase),
  ) as Ref<GameState>;
  const selectingSlot = ref<SelectingSlot | null>(null);
  const selectingTargetFor = ref<SelectingTargetFor | null>(null);
  const selectingAllyTargetFor = ref<SelectingAllyTargetFor | null>(null);
  const sendAction = vi.fn(
    opts.sendAction ?? (async (): Promise<ActionResult> => ({ ok: true })),
  );
  const isOwnUnit = opts.isOwnUnit ?? (() => true);

  // useBattleActions registers an onScopeDispose cleanup for the error timer;
  // running it inside an effect scope (as the real BattleStage.vue setup()
  // provides implicitly) avoids a spurious "no active effect scope" warning.
  const scope = effectScope();
  const actions = scope.run(() =>
    useBattleActions({
      sendAction,
      selectingSlot,
      selectingTargetFor,
      selectingAllyTargetFor,
      isOwnUnit,
      state,
    }),
  )!;

  return { actions, state, selectingSlot, selectingTargetFor, selectingAllyTargetFor, sendAction };
}

// ---------------------------------------------------------------------------

describe("useBattleActions — card routing (routeCard via onCardClick)", () => {
  it("Instance range without allyTarget dispatches playCard immediately, no target selection", () => {
    const hand = [card(0, { range: "Instance" })];
    const { actions, selectingSlot, selectingTargetFor, selectingAllyTargetFor, sendAction } =
      setup({ allies: [ally(3, { hand })] });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0);

    expect(sendAction).toHaveBeenCalledWith({
      type: "playCard",
      unitId: 3,
      cardIndex: 0,
      diceSlot: 0,
    });
    // sent immediately -- no intermediate selection state is entered
    expect(selectingSlot.value).toBeNull();
    expect(selectingTargetFor.value).toBeNull();
    expect(selectingAllyTargetFor.value).toBeNull();
  });

  it("Instance + allyTarget enters ally-target selection instead of sending", () => {
    const hand = [card(0, { range: "Instance", allyTarget: true, name: "Heal" })];
    const { actions, selectingAllyTargetFor, sendAction } = setup({
      allies: [ally(3, { hand })],
    });

    actions.onSlotSelectClick({ id: 3 }, 1);
    actions.onCardClick(3, 0);

    expect(sendAction).not.toHaveBeenCalled();
    expect(selectingAllyTargetFor.value).toEqual({
      unitId: 3,
      cardIndex: 0,
      isEgo: false,
      diceSlot: 1,
      cardName: "Heal",
    });
  });

  it("every other range enters enemy-target selection carrying the card's range", () => {
    const hand = [card(0, { range: "FarArea", name: "Sweep" })];
    const { actions, selectingTargetFor, sendAction } = setup({ allies: [ally(3, { hand })] });

    actions.onSlotSelectClick({ id: 3 }, 2);
    actions.onCardClick(3, 0);

    expect(sendAction).not.toHaveBeenCalled();
    expect(selectingTargetFor.value).toEqual({
      unitId: 3,
      cardIndex: 0,
      isEgo: false,
      diceSlot: 2,
      cardName: "Sweep",
      cardRange: "FarArea",
    });
  });

  it("EGO cards read from the unit's ego list and carry isEgo:1 on dispatch", () => {
    const ego = [card(0, { range: "Instance", name: "Ego Strike" })];
    const { actions, sendAction } = setup({ allies: [ally(3, { ego, hand: [] })] });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0, true);

    expect(sendAction).toHaveBeenCalledWith({
      type: "playCard",
      unitId: 3,
      cardIndex: 0,
      diceSlot: 0,
      isEgo: 1,
    });
  });

  it("EGO + Instance + allyTarget selection carries isEgo through to the ally-target dispatch", async () => {
    const ego = [card(0, { range: "Instance", allyTarget: true, name: "Ego Heal" })];
    const { actions, sendAction } = setup({ allies: [ally(3, { ego })] });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0, true);
    await actions.onAllyTargetClick(4);

    expect(sendAction).toHaveBeenCalledWith({
      type: "playCard",
      unitId: 3,
      cardIndex: 0,
      diceSlot: 0,
      targetUnitId: 4,
      isEgo: 1,
    });
  });

  it("no-ops when the unit cannot be found in state", () => {
    const { actions, selectingSlot, sendAction } = setup({ allies: [ally(1)] });

    actions.onSlotSelectClick({ id: 999 }, 0);
    actions.onCardClick(999, 0);

    // the slot selection is still consumed on the way into routeCard, but
    // routeCard's own unit lookup fails silently -- no crash, no dispatch
    expect(selectingSlot.value).toBeNull();
    expect(sendAction).not.toHaveBeenCalled();
  });
});

describe("useBattleActions — ownership gating", () => {
  it("onCardClick early-returns for a unit the session does not own", () => {
    const hand = [card(0, { range: "Instance" })];
    const { actions, selectingSlot, selectingTargetFor, sendAction } = setup({
      allies: [ally(3, { hand })],
      isOwnUnit: () => false,
    });

    actions.onSlotSelectClick({ id: 3 }, 0);
    // the slot IS recorded (onSlotSelectClick has no ownership check of its
    // own) but the card tap that would route/dispatch must be blocked
    expect(selectingSlot.value).toEqual({ unitId: 3, diceSlot: 0 });

    actions.onCardClick(3, 0);

    expect(sendAction).not.toHaveBeenCalled();
    expect(selectingTargetFor.value).toBeNull();
    // slot selection is untouched by the blocked click
    expect(selectingSlot.value).toEqual({ unitId: 3, diceSlot: 0 });
  });

  it("onCardClick proceeds normally once isOwnUnit reports true", () => {
    const hand = [card(0, { range: "Instance" })];
    const { actions, sendAction } = setup({
      allies: [ally(3, { hand })],
      isOwnUnit: () => true,
    });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0);

    expect(sendAction).toHaveBeenCalledTimes(1);
  });

  // Reproduces Stage.vue's real isOwnUnit contract (see CLAUDE.md /
  // MEMORY.md "Ownership Gating"): when claims are enabled, an unclaimed
  // unit is NOT freely controllable -- it must be explicitly assigned to
  // this session. Getting this backwards (treating "no claim recorded" as
  // "anyone may act") would silently defeat the server's own
  // SessionManager.IsAuthorized check, so it is exercised directly against
  // the documented semantics rather than trusting a hand-picked mock.
  function realIsOwnUnit(session: SessionState | null) {
    return (unitId: number) => {
      if (!session || !session.claimsEnabled) return true;
      return session.assignedUnits.includes(unitId);
    };
  }

  it("blocks an unclaimed unit when claims are enabled", () => {
    const hand = [card(0, { range: "Instance" })];
    const session: SessionState = {
      sessionId: "s1",
      assignedUnits: [],
      claimsEnabled: true,
    };
    const { actions, sendAction } = setup({
      allies: [ally(3, { hand })],
      isOwnUnit: realIsOwnUnit(session),
    });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0);

    expect(sendAction).not.toHaveBeenCalled();
  });

  it("allows a claimed unit when claims are enabled", () => {
    const hand = [card(0, { range: "Instance" })];
    const session: SessionState = {
      sessionId: "s1",
      assignedUnits: [3],
      claimsEnabled: true,
    };
    const { actions, sendAction } = setup({
      allies: [ally(3, { hand })],
      isOwnUnit: realIsOwnUnit(session),
    });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0);

    expect(sendAction).toHaveBeenCalledTimes(1);
  });

  it("allows any unit when claims are disabled, regardless of assignment", () => {
    const hand = [card(0, { range: "Instance" })];
    const session: SessionState = {
      sessionId: "s1",
      assignedUnits: [],
      claimsEnabled: false,
    };
    const { actions, sendAction } = setup({
      allies: [ally(3, { hand })],
      isOwnUnit: realIsOwnUnit(session),
    });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0);

    expect(sendAction).toHaveBeenCalledTimes(1);
  });
});

describe("useBattleActions — slot-first sequence (steps 1-2)", () => {
  it("selecting an empty slot then a card routes using that slot", () => {
    const hand = [card(0, { range: "FarArea" })];
    const { actions, selectingSlot, selectingTargetFor } = setup({
      allies: [ally(3, { hand })],
    });

    actions.onSlotSelectClick({ id: 3 }, 4);
    expect(selectingSlot.value).toEqual({ unitId: 3, diceSlot: 4 });

    actions.onCardClick(3, 0);
    // the slot is consumed on the way into routing
    expect(selectingSlot.value).toBeNull();
    expect(selectingTargetFor.value?.diceSlot).toBe(4);
  });

  it("tapping the same slot again toggles the selection off", () => {
    const { actions, selectingSlot } = setup({ allies: [ally(3)] });

    actions.onSlotSelectClick({ id: 3 }, 0);
    expect(selectingSlot.value).not.toBeNull();

    actions.onSlotSelectClick({ id: 3 }, 0);
    expect(selectingSlot.value).toBeNull();
  });

  it("tapping a different slot (no target selection active) replaces the selection", () => {
    const { actions, selectingSlot } = setup({ allies: [ally(3)] });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onSlotSelectClick({ id: 3 }, 1);

    expect(selectingSlot.value).toEqual({ unitId: 3, diceSlot: 1 });
  });

  it("a card tap for a unit with no slot selected does nothing", () => {
    const hand = [card(0, { range: "Instance" })];
    const { actions, sendAction } = setup({ allies: [ally(3, { hand })] });

    actions.onCardClick(3, 0);

    expect(sendAction).not.toHaveBeenCalled();
  });
});

describe("useBattleActions — re-routing (step 4: different card, same unit)", () => {
  it("re-routes a different card from the same unit while a target selection is pending, reusing the slot", () => {
    const hand = [
      card(0, { range: "FarArea", name: "First" }),
      card(1, { range: "Instance", name: "Second" }),
    ];
    const { actions, selectingTargetFor, sendAction } = setup({
      allies: [ally(3, { hand })],
    });

    actions.onSlotSelectClick({ id: 3 }, 5);
    actions.onCardClick(3, 0);
    expect(selectingTargetFor.value).toMatchObject({ cardIndex: 0, diceSlot: 5 });

    // re-route to a different card -- an Instance card, so this dispatches
    // immediately instead of re-entering target selection
    actions.onCardClick(3, 1);

    expect(sendAction).toHaveBeenCalledWith({
      type: "playCard",
      unitId: 3,
      cardIndex: 1,
      diceSlot: 5,
    });
    expect(selectingTargetFor.value).toBeNull();
  });

  it("re-routing between two target-requiring cards preserves the slot and updates the card info", () => {
    const hand = [
      card(0, { range: "Near", name: "First" }),
      card(1, { range: "Far", name: "Second" }),
    ];
    const { actions, selectingTargetFor } = setup({ allies: [ally(3, { hand })] });

    actions.onSlotSelectClick({ id: 3 }, 2);
    actions.onCardClick(3, 0);
    actions.onCardClick(3, 1);

    expect(selectingTargetFor.value).toEqual({
      unitId: 3,
      cardIndex: 1,
      isEgo: false,
      diceSlot: 2,
      cardName: "Second",
      cardRange: "Far",
    });
  });
});

describe("useBattleActions — cancellation", () => {
  it("tapping any slot while a target selection is pending cancels everything", () => {
    const hand = [card(0, { range: "Near" })];
    const { actions, selectingSlot, selectingTargetFor, selectingAllyTargetFor } = setup({
      allies: [ally(3, { hand })],
    });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0);
    expect(selectingTargetFor.value).not.toBeNull();

    actions.onSlotSelectClick({ id: 3 }, 1);

    expect(selectingSlot.value).toBeNull();
    expect(selectingTargetFor.value).toBeNull();
    expect(selectingAllyTargetFor.value).toBeNull();
  });

  it("cancelTargeting clears all three selection refs directly", () => {
    const hand = [card(0, { range: "Instance", allyTarget: true })];
    const { actions, selectingSlot, selectingTargetFor, selectingAllyTargetFor } = setup({
      allies: [ally(3, { hand })],
    });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0);
    expect(selectingAllyTargetFor.value).not.toBeNull();

    actions.cancelTargeting();

    expect(selectingSlot.value).toBeNull();
    expect(selectingTargetFor.value).toBeNull();
    expect(selectingAllyTargetFor.value).toBeNull();
  });
});

describe("useBattleActions — target dispatch payload shapes", () => {
  it("onTargetDieClick posts playCard with targetUnitId and targetDiceSlot, matching the documented payload", async () => {
    const hand = [card(0, { range: "Near" })];
    const { actions, selectingTargetFor, sendAction } = setup({
      allies: [ally(3, { hand })],
    });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0);

    await actions.onTargetDieClick(5, 1);

    expect(sendAction).toHaveBeenCalledWith({
      type: "playCard",
      unitId: 3,
      cardIndex: 0,
      diceSlot: 0,
      targetUnitId: 5,
      targetDiceSlot: 1,
    });
    expect(selectingTargetFor.value).toBeNull();
  });

  it("onTargetDieClick is a no-op when there is no pending target selection", async () => {
    const { actions, sendAction } = setup({ allies: [ally(3)] });

    await actions.onTargetDieClick(5, 1);

    expect(sendAction).not.toHaveBeenCalled();
  });

  it("onAllyTargetClick posts playCard with targetUnitId only, no targetDiceSlot", async () => {
    const hand = [card(0, { range: "Instance", allyTarget: true })];
    const { actions, selectingAllyTargetFor, sendAction } = setup({
      allies: [ally(3, { hand })],
    });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0);

    await actions.onAllyTargetClick(2);

    const payload = sendAction.mock.calls[0]?.[0];
    expect(payload).toEqual({
      type: "playCard",
      unitId: 3,
      cardIndex: 0,
      diceSlot: 0,
      targetUnitId: 2,
    });
    expect(payload && "targetDiceSlot" in payload).toBe(false);
    expect(selectingAllyTargetFor.value).toBeNull();
  });

  it("onAllyTargetClick is a no-op when there is no pending ally-target selection", async () => {
    const { actions, sendAction } = setup({ allies: [ally(3)] });

    await actions.onAllyTargetClick(2);

    expect(sendAction).not.toHaveBeenCalled();
  });
});

describe("useBattleActions — onRemoveCard", () => {
  it("posts removeCard with the documented payload shape", async () => {
    const { actions, sendAction } = setup({ allies: [ally(3)] });

    await actions.onRemoveCard(3, 2);

    expect(sendAction).toHaveBeenCalledWith({ type: "removeCard", unitId: 3, diceSlot: 2 });
  });

  it("clears a matching pending target selection", async () => {
    const hand = [card(0, { range: "Near" })];
    const { actions, selectingTargetFor } = setup({ allies: [ally(3, { hand })] });

    actions.onSlotSelectClick({ id: 3 }, 2);
    actions.onCardClick(3, 0);
    expect(selectingTargetFor.value?.diceSlot).toBe(2);

    await actions.onRemoveCard(3, 2);

    expect(selectingTargetFor.value).toBeNull();
  });

  it("clears a matching pending ally-target selection", async () => {
    const hand = [card(0, { range: "Instance", allyTarget: true })];
    const { actions, selectingAllyTargetFor } = setup({ allies: [ally(3, { hand })] });

    actions.onSlotSelectClick({ id: 3 }, 2);
    actions.onCardClick(3, 0);
    expect(selectingAllyTargetFor.value).not.toBeNull();

    await actions.onRemoveCard(3, 2);

    expect(selectingAllyTargetFor.value).toBeNull();
  });

  it("leaves an unrelated unit's pending selection untouched", async () => {
    const hand = [card(0, { range: "Near" })];
    const { actions, selectingTargetFor } = setup({
      allies: [ally(3, { hand }), ally(4)],
    });

    actions.onSlotSelectClick({ id: 3 }, 2);
    actions.onCardClick(3, 0);

    await actions.onRemoveCard(4, 0);

    expect(selectingTargetFor.value?.unitId).toBe(3);
  });
});

describe("useBattleActions — abnormality and EGO selection", () => {
  it("posts selectAbnormality without targetUnitId when omitted", async () => {
    const { actions, sendAction } = setup({ allies: [ally(3)] });

    await actions.onSelectAbnormality(5);

    expect(sendAction).toHaveBeenCalledWith({ type: "selectAbnormality", cardId: 5 });
  });

  it("posts selectAbnormality with targetUnitId when provided", async () => {
    const { actions, sendAction } = setup({ allies: [ally(3)] });

    await actions.onSelectAbnormality(5, 2);

    expect(sendAction).toHaveBeenCalledWith({
      type: "selectAbnormality",
      cardId: 5,
      targetUnitId: 2,
    });
  });

  it("posts selectEgo with the choice id", async () => {
    const { actions, sendAction } = setup({ allies: [ally(3)] });

    await actions.onSelectEgo(7);

    expect(sendAction).toHaveBeenCalledWith({ type: "selectEgo", choiceId: 7 });
  });
});

describe("useBattleActions — onConfirm", () => {
  it("posts confirm and clears slot/target selection", async () => {
    const hand = [card(0, { range: "Near" })];
    const { actions, selectingSlot, selectingTargetFor, sendAction } = setup({
      allies: [ally(3, { hand })],
    });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0);

    await actions.onConfirm();

    expect(sendAction).toHaveBeenCalledWith({ type: "confirm" });
    expect(selectingSlot.value).toBeNull();
    expect(selectingTargetFor.value).toBeNull();
  });
});

describe("useBattleActions — action error banner", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("surfaces the server error and auto-clears after 3 seconds", async () => {
    const { actions } = setup({
      allies: [ally(3)],
      sendAction: async () => ({ ok: false, error: "not your turn" }),
    });

    await actions.onRemoveCard(3, 0);
    expect(actions.actionError.value).toBe("not your turn");

    vi.advanceTimersByTime(2999);
    expect(actions.actionError.value).toBe("not your turn");

    vi.advanceTimersByTime(1);
    expect(actions.actionError.value).toBeNull();
  });

  it("falls back to a generic message when the server omits one", async () => {
    const { actions } = setup({
      allies: [ally(3)],
      sendAction: async () => ({ ok: false }),
    });

    await actions.onRemoveCard(3, 0);

    expect(actions.actionError.value).toBe("Action failed");
  });

  it("a subsequent successful action clears a still-pending error and its timer", async () => {
    const sendAction = vi
      .fn<(action: ClientAction) => Promise<ActionResult>>()
      .mockResolvedValueOnce({ ok: false, error: "boom" })
      .mockResolvedValueOnce({ ok: true });
    const { actions } = setup({ allies: [ally(3)], sendAction });

    await actions.onRemoveCard(3, 0);
    expect(actions.actionError.value).toBe("boom");

    await actions.onRemoveCard(3, 1);
    expect(actions.actionError.value).toBeNull();

    // the stale timer from the first failure must not fire and re-toggle state
    vi.advanceTimersByTime(3000);
    expect(actions.actionError.value).toBeNull();
  });

  it("cleanupErrorTimer cancels the pending auto-clear", async () => {
    const { actions } = setup({
      allies: [ally(3)],
      sendAction: async () => ({ ok: false, error: "boom" }),
    });

    await actions.onRemoveCard(3, 0);
    expect(actions.actionError.value).toBe("boom");

    actions.cleanupErrorTimer();
    vi.advanceTimersByTime(3000);

    // without the cleanup, the original 3s timer would have fired here and
    // reset actionError to null -- staying at "boom" proves it was cancelled
    expect(actions.actionError.value).toBe("boom");
  });

  it("a phase change resets all selection state and the error banner", async () => {
    const hand = [card(0, { range: "Near" })];
    const { actions, state, selectingSlot, selectingTargetFor } = setup({
      allies: [ally(3, { hand })],
      sendAction: async () => ({ ok: false, error: "boom" }),
    });

    actions.onSlotSelectClick({ id: 3 }, 0);
    actions.onCardClick(3, 0);
    await actions.onRemoveCard(3, 0);
    expect(actions.actionError.value).toBe("boom");

    state.value = { ...state.value, phase: "SomeOtherPhase" };
    await nextTick();

    expect(selectingSlot.value).toBeNull();
    expect(selectingTargetFor.value).toBeNull();
    expect(actions.actionError.value).toBeNull();

    // the stale 3s timer from the pre-reset error must not fire afterwards
    vi.advanceTimersByTime(3000);
    expect(actions.actionError.value).toBeNull();
  });
});
