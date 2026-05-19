<!-- 
  Speed die row plus slotted cards.

  Props:
    unit       – ally or enemy unit object from game state
    die        – die object from game state
    card       – slotted card object from game state (name, dice[], targetUnitId, ...)
    isReversed – whether this die is on the right of the unit (usually === !isAlly)
    isAlly     – whether this is an ally unit 
-->
<script setup lang="ts">
import type { SlottedCardEntry, SpeedDie, Unit } from "~/types/game";

const props = withDefaults(
  defineProps<{
    unit: Unit;
    die: SpeedDie;
    card: SlottedCardEntry | undefined;
    isReversed: boolean;
    isAlly: boolean;
    onLongPress: () => void;
  }>(),
  {
    isReversed: false,
    isAlly: true,
  },
);

const {
  phase,
  isSelectPhase,
  selectingSlot,
  selectingTargetFor,
  onSlotSelectClick,
  onTargetDieClick,
  onRemoveCard,
  allUnits,
  isOwnUnit,
  isRestrictedTarget,
} = useBattleCtx();

type DieState =
  | "empty" // no card in slot
  | "available" // no card in slot, ready to select
  | "open" // slot selected, awaiting card
  | "pending" // card selected, awaiting target
  | "unopposed-outgoing" // target set, no clash
  | "unopposed-incoming"
  | "clash" // target set, clash
  | "broken"
  | "locked" // die disabled (paralysis-class buff or per-die isControlable=false)
  | "hidden-target" // card slotted but target suppressed by the mod-side enemy-targets gate (Crying Children / Unhearing Child); render the rolled value normally with no directional decoration
  | "invalid";

const isUnitBroken = computed(
  () => props.unit.turnState === "BREAK" || isDead(props.unit),
);

let slotLongPressed = false;
let slotPressTimer: ReturnType<typeof setTimeout> | null = null;

// transient rejection flash — see flashReject below. Stored as a slot index
// so multiple dice in the same row can flash independently without sharing
// timer state.
const rejectedSlot = ref<number | null>(null);
let rejectTimer: ReturnType<typeof setTimeout> | null = null;
const REJECT_FLASH_MS = 380;

onBeforeUnmount(() => {
  if (slotPressTimer) {
    clearTimeout(slotPressTimer);
    slotPressTimer = null;
  }
  if (rejectTimer) {
    clearTimeout(rejectTimer);
    rejectTimer = null;
  }
});

// Plays a brief red overlay flash on the die. Reset to null first and toggle
// back on the next frame so consecutive clicks restart the animation cleanly.
function flashReject(slot: number) {
  if (rejectTimer) clearTimeout(rejectTimer);
  rejectedSlot.value = null;
  requestAnimationFrame(() => {
    rejectedSlot.value = slot;
    rejectTimer = setTimeout(() => {
      rejectedSlot.value = null;
      rejectTimer = null;
    }, REJECT_FLASH_MS);
  });
}
function onSlotPressStart(sc: SlottedCardEntry | undefined) {
  if (!sc) return;
  slotLongPressed = false;
  slotPressTimer = setTimeout(() => {
    slotLongPressed = true;
    props.onLongPress();
  }, LONG_PRESS_MS);
}

const dieState: ComputedRef<DieState> = computed(() => {
  // a per-die staggered flag always wins — actually destroyed dice show the X
  // glyph regardless of any unit-level state.
  if (props.die.staggered) return "broken";

  // lock wins over unit-level broken so a stun-immobilised unit that has also
  // entered BREAK still shows the lock overlay (matching SpeedDiceSetter's
  // BreakDice(breaked:true, locked:true) call when HasStun() is true).
  if (props.die.locked) return "locked";

  // unit-level broken state (BREAK turnState or dead): the unit cannot act,
  // so all remaining dice render as X.
  if (isUnitBroken.value) return "broken";

  // Placeholder dice emitted by the serializer before rolls have occurred
  if (
    phase.value === "RoundEndPhase" ||
    phase.value === "RoundStartPhase_UI" ||
    phase.value === "RoundStartPhase_System"
  )
    return "invalid";

  if (props.card == null) {
    // no card slotted
    // is card selection in progress?
    if (
      isSelectPhase.value &&
      selectingSlot?.value?.unitId === props.unit.id &&
      selectingSlot?.value?.diceSlot === props.die.slot
    )
      return "open";

    if (
      isSelectPhase.value &&
      selectingTargetFor?.value?.unitId === props.unit.id &&
      selectingTargetFor?.value?.diceSlot === props.die.slot
    )
      return "pending";

    // suppress slot interaction for unowned ally units and all enemy units
    if (!props.isAlly || !isOwnUnit(props.unit.id)) return "empty";
    return isSelectPhase.value ? "available" : "empty";
  } else {
    // card is slotted, target is set
    if (props.card.clash) return "clash";
    if (props.card.targetUnitId != null)
      return props.isAlly ? "unopposed-outgoing" : "unopposed-incoming";

    // Card is slotted on an enemy die but `targetUnitId` was suppressed by the
    // mod-side enemy-targets gate (vanilla `StageController.IsVisibleEnemyTarget()`
    // is false — e.g. The Crying Children's Page / Unhearing Child / passive
    // 240428). The base game still draws the rolled die value in this state,
    // only the directional arrows go away, so we mirror that here: render the
    // die normally (value visible, no incoming/outgoing/clash decoration).
    if (!props.isAlly) return "hidden-target";

    return "invalid";
  }
});

const dieDisplayValue: ComputedRef<string> = computed(() => {
  switch (dieState.value) {
    case "broken":
      return "✕";
    case "invalid":
      return "—";
    case "locked":
      // the lock glyph overlay is the sole indicator; suppress the rolled value
      return "";
    default:
      return formatSpeedDieValue(props.die.value) || "—";
  }
});

const slotState = computed(() => {
  // card is already slotted on this die
  if (props.card != null) return "slot-filled";

  switch (dieState.value) {
    case "open":
      return "slot-open";
    case "pending":
      return "slot-pending";
    case "broken":
    case "locked":
      // locked slots share the broken treatment: no pointer cursor, no green
      // pulse, no click affordance — the die cannot accept a card.
      return "slot-broken";
    case "available":
      return "slot-available";
    default:
      return "slot-empty";
  }
});

const isTargeting = computed(() => selectingTargetFor.value !== null);
// Unit-level targetable gate for the enemy faction. `isUnitTargetable` folds
// the wire's `targetable` flag with a defensive `!isDead` guard so the green
// hex affordance never appears on a unit that already shows the DEAD badge —
// covers both steady state and the `_isKnockout` lag window where `hp <= 0`
// can arrive in a state push before the wire's `targetable` flips to false.
// `isRestrictedTarget` overlays BigBird_Eye's per-selection fixed-target list.
const canBeTargeted = computed(
  () =>
    isTargeting.value &&
    isUnitTargetable(props.unit) &&
    !isRestrictedTarget(props.unit.id),
);
// Combines two unrelated sources of "this die can't be targeted":
//   1. Intrinsic: `targetable === false` — Justitia-style invincibility on
//      enemies, stealth-class ally buffs. Shown statically.
//   2. Per-selection: the currently-selected actor has a fixedTargets list
//      (BigBird_Eye / "Stared At") and this unit is not in it. Cleared on
//      deselect, matching the vanilla `BlockOtherUnitsDice`/`Unblock` pair.
// Both render with the same crosshatch overlay so the rolled value (still
// useful for clash planning) stays visible underneath.
const isUntargetable = computed(
  () => props.unit.targetable === false || isRestrictedTarget(props.unit.id),
);

function onSlotPressEnd() {
  if (slotPressTimer) {
    clearTimeout(slotPressTimer);
    slotPressTimer = null;
  }
}

function handleSlotClick(d: SpeedDie, sc: SlottedCardEntry | undefined) {
  if (slotLongPressed) {
    slotLongPressed = false;
    return;
  }
  if (!isSelectPhase.value) return;

  if (props.isAlly) {
    // Clicks on a slotted die are reserved for long-press detail; tap is a
    // no-op rather than a rejection so we don't flash on it.
    if (sc != null) return;
    const rejected =
      !isOwnUnit(props.unit.id) ||
      d.staggered ||
      d.locked ||
      d.controllable === false ||
      isUnitBroken.value;
    if (rejected) {
      flashReject(d.slot);
      return;
    }
    onSlotSelectClick(props.unit, d.slot);
  } else {
    // Enemy dice only matter while the user is targeting; outside of that the
    // tap is a no-op (not a rejection) so the row stays quiet on idle taps.
    if (!isTargeting.value) return;
    // Mirrors vanilla's gate: `SpeedDiceUI.OnClickSpeedDice` early-returns only
    // on per-die `!isControlable`, and `IsTargetableUnit`'s same-faction
    // controllability / stun checks are skipped for cross-faction player→enemy
    // attacks. Per-die staggered and the Stun lock overlay are valid targets
    // (matches `TargetPicker.vue` and the CLAUDE.md convention).
    const valid = canBeTargeted.value && isDieTargetable(d);
    if (!valid) {
      flashReject(d.slot);
      return;
    }
    onTargetDieClick(props.unit.id, d.slot);
  }
}

function targetLabel(sc: SlottedCardEntry | undefined): string {
  if (sc?.targetUnitId == null) return "";
  const u = allUnits.value.find((u) => u.id === sc.targetUnitId);
  const prefix = sc.clash ? "⚔" : "↗";
  return `${prefix} ${u?.name ?? `#${sc.targetUnitId}`} ·${sc.targetSlot}`;
}
</script>

<template>
  <div
    class="slot-row"
    :class="[
      slotState,
      {
        'slot-reversed': isReversed,
        'slot-target': canBeTargeted && !isAlly && isDieTargetable(die),
      },
    ]"
    @click.stop="handleSlotClick(die, card)"
    @mousedown="onSlotPressStart(card)"
    @mouseup="onSlotPressEnd"
    @mouseleave="onSlotPressEnd"
    @touchstart.passive="onSlotPressStart(card)"
    @touchend.passive="onSlotPressEnd"
    @touchmove.passive="onSlotPressEnd"
  >
    <!-- Hexagonal die (data-die used by ArrowOverlay for coordinate lookup) -->
    <span
      class="hex-wrap"
      :class="[
        dieState,
        {
          'hex-target': canBeTargeted && !isAlly && isDieTargetable(die),
          'hex-rejected': rejectedSlot === die.slot,
        },
      ]"
      :data-die="`${unit.id}-${die.slot}`"
    >
      <span
        class="hex-inner"
        :class="{
          'hex-inner--dim-value': dieDisplayValue === '—',
        }"
        >{{ dieDisplayValue }}</span
      >
      <!-- additive overlays — lock and untargetable preserve the underlying
           faction-coloured fill so the cue does not erase identifying state.
           Lock only renders when the die is not also broken: broken takes
           priority because a destroyed die is unusable regardless. -->
      <span
        v-if="dieState === 'locked'"
        class="hex-overlay hex-lock"
        aria-label="locked"
      >
        <svg viewBox="0 0 16 16" aria-hidden="true">
          <path
            d="M5 7V5a3 3 0 0 1 6 0v2h1v6H4V7h1zm1.4 0h3.2V5a1.6 1.6 0 0 0-3.2 0v2z"
            fill="currentColor"
          />
        </svg>
      </span>
      <span
        v-if="isUntargetable"
        class="hex-overlay hex-untargetable"
        aria-hidden="true"
      ></span>
    </span>

    <!-- slotted card -->
    <div class="slot-wrapper">
      <div class="slot-content">
        <SlottedCard
          v-if="card != null"
          :card="card"
          :targetLabel="targetLabel(card) || undefined"
        />
        <div v-else class="slot-empty">—</div>
        <button
          v-if="isAlly && isSelectPhase && card != null && isOwnUnit(unit.id)"
          class="remove-btn"
          title="Return to hand"
          aria-label="Remove card from slot"
          @click="onRemoveCard(unit.id, card.slot)"
        >
          ✕
        </button>
      </div>
      <slot />
    </div>
  </div>
</template>

<style scoped>
/* ── Slotted card content ── */
.slot-wrapper {
  display: flex;
  flex-direction: column;
  flex: 1;
}
.slot-content {
  flex: 1;
  min-width: 0;
  min-height: 2.2rem;
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 0.35rem;
}
.slot-row {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.06rem 0.15rem 0.06rem 0;
}
.slot-filled .slot-wrapper {
  background: var(--bg-card-2);
}
.slot-open {
  cursor: pointer;
}
.slot-open .slot-wrapper {
  background: var(--bg-green-2);
}
.slot-open:hover .slot-wrapper {
  background: var(--bg-green-3);
}
.slot-empty {
  color: var(--text-3);
}
.slot-pending .slot-wrapper {
  background: var(--bg-gold);
}
.slot-target {
  cursor: pointer;
}
.slot-empty {
  cursor: default;
}
.slot-available {
  cursor: pointer;
}
.slot-available .slot-empty {
  color: var(--text-gold-deep);
  transition: color 0.15s;
}
.slot-available:hover .slot-empty {
  color: var(--gold-dim);
}

/* ── Hexagonal die ── */
/* Two-layer clip-path creates a "border" effect without actual CSS border.
   data-die is on the outer element so ArrowOverlay can locate it correctly. */
/* default (empty) */
.hex-wrap {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.4rem;
  height: 2.1rem;
  clip-path: var(--hex);
  /* Outline = lighter shade of the inner fill so both stay in the same
     colour family. State-class rules (.broken / .open / .pending / .clash
     / .unopposed-* / .hex-target) override this for committed combat states. */
  background: color-mix(in srgb, var(--die-faction-fill, var(--border-mid)) 70%, white);
  flex-shrink: 0;
  position: relative;
}
.hex-inner {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 1.75rem;
  clip-path: var(--hex);
  /* faction fill inherits from the parent unit-card, which sets
     --die-faction-fill based on faction (or the CDC per-unit override).
     Falls back to a neutral panel colour when no ancestor sets it
     (e.g. preview-die contexts outside DisplayCard). */
  background: var(--die-faction-fill, var(--bg-card-2));
  font-family: var(--font-body);
  font-size: var(--fs-2xs);
  /* Numeral colour picks up the per-unit accent (CDC's tint, e.g. #6666ff
     for WARP Cleanup Agents) when present, falling back to --text-1. */
  color: var(--die-accent-color, var(--text-1));
  pointer-events: none;
}
/* empty (ready for select) — the beckon pulses between the static outline
   colour and the unit's accent so themed dice cycle in their own palette
   rather than the generic gold. Vanilla / un-overridden units fall back
   through the cascade to the gold tokens. */
.hex-wrap.available {
  background: color-mix(in srgb, var(--die-faction-fill, var(--border-mid)) 70%, white);
  cursor: pointer;
  animation: hex-beckon 2.2s ease-in-out infinite;
}
.hex-wrap.available .hex-inner {
  color: var(--die-accent-color, var(--gold-dim));
}
.hex-wrap.available:hover {
  animation: none;
  background: var(--die-accent-color, var(--gold-dim));
}
.hex-wrap.available:hover .hex-inner {
  background: color-mix(in srgb, var(--die-accent-color, var(--bg-gold-hover)) 35%, white);
  color: var(--die-accent-color, var(--gold));
}
@keyframes hex-beckon {
  0%,
  100% {
    background: color-mix(in srgb, var(--die-faction-fill, var(--border-mid)) 70%, white);
  }
  50% {
    background: var(--die-accent-color, var(--bg-gold-beacon));
  }
}
/* open */
.hex-wrap.open {
  background: var(--green-hi);
  cursor: pointer;
  transition: background 0.1s;
}
.hex-wrap.open .hex-inner {
  background: var(--bg-green-2);
  color: var(--text-1);
  transition:
    background 0.1s,
    color 0.1s;
}
.hex-wrap.open:hover {
  background: var(--green);
}
.hex-wrap.open:hover .hex-inner {
  background: var(--bg-green-3);
  color: #fff;
}
/* pending */
.hex-wrap.pending {
  background: var(--gold);
  animation: hex-pulse 1.2s ease-in-out infinite;
}
.hex-wrap.pending .hex-inner {
  background: var(--bg-gold);
  color: var(--gold-bright);
}
@keyframes hex-pulse {
  0%,
  100% {
    background: var(--gold);
  }
  50% {
    background: var(--gold-dim);
  }
}
/* unopposed: incoming */
.hex-wrap.unopposed-incoming {
  background: var(--incoming);
}
/* unopposed: outgoing */
.hex-wrap.unopposed-outgoing {
  background: var(--outgoing);
}
/* clash */
.hex-wrap.clash {
  background: var(--clash);
}
/* broken */
.hex-wrap.broken {
  background: var(--crimson-dim);
}
.hex-wrap.broken .hex-inner {
  background: var(--bg-broken);
  color: var(--crimson-hi);
}
/* invalid */
.hex-inner--dim-value {
  color: var(--text-2);
}

/* ── Additive overlays — lock and untargetable ──
   Both are absolutely positioned on top of .hex-inner and leave the
   underlying faction-coloured fill visible so the cue does not erase
   identifying state. pointer-events: none so slot clicks pass through. */
.hex-overlay {
  position: absolute;
  inset: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  pointer-events: none;
  clip-path: var(--hex);
}
.hex-lock {
  color: var(--text-1);
}
.hex-lock svg {
  width: 1.1rem;
  height: 1.1rem;
  opacity: 0.92;
  filter: drop-shadow(0 0 1px rgba(0, 0, 0, 0.7));
}
/* hatched ⌧ via a repeating linear gradient — additive, leaves the
   underlying faction colour and rolled value readable. */
.hex-untargetable {
  background-image: repeating-linear-gradient(
    45deg,
    rgba(255, 255, 255, 0.32) 0,
    rgba(255, 255, 255, 0.32) 2px,
    transparent 2px,
    transparent 6px
  );
  mix-blend-mode: overlay;
}

/* ── Rejection flash ──
   A transient red overlay played when a click can't be accepted. Ally side:
   unowned, mind-controlled unit, staggered/locked die, per-die clock EGO,
   broken unit. Enemy side: untargetable unit (Justitia-style invincibility,
   NotTargetable buff, dead) or per-die `controllable === false` (clock EGO).
   Per-die staggered and Stun lock are NOT rejection causes on the enemy side
   — they're valid targets per vanilla. Pseudo-element keeps the underlying
   die state visible while signalling that the input was received. */
.hex-wrap.hex-rejected::after {
  content: "";
  position: absolute;
  inset: 0;
  background: var(--crimson-hi, #e53935);
  clip-path: var(--hex);
  pointer-events: none;
  animation: hex-reject-flash 380ms ease-out forwards;
  z-index: 5;
}
@keyframes hex-reject-flash {
  0% {
    opacity: 0;
  }
  20% {
    opacity: 0.75;
  }
  100% {
    opacity: 0;
  }
}

/* ── Targetable die highlight ── */
.hex-wrap.hex-target {
  background: var(--green-hi);
  transition: background 0.1s;
}
.slot-target:hover .hex-wrap.hex-target {
  background: var(--green);
}
.slot-target:hover .hex-wrap.hex-target .hex-inner {
  background: var(--bg-green-3);
  color: #fff;
}
.slot-target:hover .slot-content {
  background: var(--bg-green-2);
  transition: background 0.1s;
}

/* ── Remove button ── */
.remove-btn {
  margin-left: auto;
  flex-shrink: 0;
  background: transparent;
  border: none;
  color: var(--text-3);
  cursor: pointer;
  font-size: var(--fs-2xs);
  padding: 0 0.15rem;
  line-height: 1;
  font-family: var(--font-body);
}
.remove-btn:hover {
  color: var(--crimson-hi);
}
</style>
