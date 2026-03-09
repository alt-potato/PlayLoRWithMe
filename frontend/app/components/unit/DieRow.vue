<!-- 
  Speed die row plus slotted cards.
-->
<script setup lang="ts">
const props = defineProps<{
  unit: any;
  die: any;
  card: any;
  color: string;
  isReversed: boolean;
  canControl: boolean;
  onLongPress: () => void;
}>();

const {
  phase,
  isSelectPhase,
  selectingSlot,
  selectingTargetFor,
  onSlotSelectClick,
} = inject(BATTLE_CTX) as BattleCtx;

const isUnitBroken = computed(
  () => props.unit.turnState === "BREAK" || isDead(props.unit),
);

const slotState = computed(() => {
  // card is slotted on this die
  if (props.card !== null) return "slot-filled";

  // die is selected and awaiting card
  if (
    isSelectPhase.value &&
    selectingSlot?.value?.unitId === props.unit.id &&
    selectingSlot?.value?.diceSlot === props.die.slot &&
    !props.die.staggered &&
    !isUnitBroken.value
  )
    return "slot-open";

  // card is selected, awaiting target
  if (
    isSelectPhase.value &&
    selectingTargetFor?.value?.unitId === props.unit.id &&
    selectingTargetFor?.value?.diceSlot === props.die.slot
  )
    return "slot-pending";

  // slot is empty
  return "slot-available";
});

let slotLongPressed = false;
let slotPressTimer: ReturnType<typeof setTimeout> | null = null;
function onSlotPressStart(sc: any) {
  if (!sc) return;
  slotLongPressed = false;
  slotPressTimer = setTimeout(() => {
    slotLongPressed = true;
    props.onLongPress();
  }, 500);
}

function onSlotPressEnd() {
  if (slotPressTimer) {
    clearTimeout(slotPressTimer);
    slotPressTimer = null;
  }
}

function handleSlotClick(d: any, sc: any) {
  if (slotLongPressed) {
    slotLongPressed = false;
    return;
  }
  if (!isSelectPhase.value) return;
  if (sc !== null || d.staggered || isUnitBroken.value) return;
  onSlotSelectClick(props.unit, d.slot);
}

function dieColor(sc: any): string {
  if (sc.clash) return ARROW_COLORS.clash;
  if (sc.targetUnitId != null) return ARROW_COLORS.outgoing;
  return props.color; // Instance / untargeted
}

function getDieDisplayValue(die: any) {
  // die is broken
  if (die.staggered || isUnitBroken.value) {
    return "✕";
  }
  // dice has no value
  // TODO: check where die is set and invalidate there
  if (
    phase.value === "RoundEndPhase" ||
    phase.value === "RoundStartPhase_UI" ||
    phase.value === "RoundStartPhase_System"
  ) {
    return "—";
  }
  // dice should have valid value
  return die.value || "—";
}
</script>

<template>
  <div
    class="slot-row"
    :class="slotState"
    @click.stop="handleSlotClick(die, card)"
    @mousedown="onSlotPressStart(card)"
    @mouseup="onSlotPressEnd"
    @mouseleave="onSlotPressEnd"
    @touchstart.passive="onSlotPressStart(card)"
    @touchend="onSlotPressEnd"
    @touchmove="onSlotPressEnd"
  >
    <!-- Hexagonal die (data-die used by ArrowOverlay for coordinate lookup) -->
    <span
      class="hex-wrap"
      :class="{
        staggered: die.staggered || isUnitBroken,
        'hex-available':
          isSelectPhase &&
          selectingSlot === null &&
          card === null &&
          !die.staggered &&
          !isUnitBroken,
        'hex-open':
          isSelectPhase &&
          selectingSlot?.unitId === unit.id &&
          selectingSlot?.diceSlot === die.slot &&
          card === null &&
          !die.staggered &&
          !isUnitBroken,
        'hex-pending':
          isSelectPhase &&
          selectingTargetFor?.unitId === unit.id &&
          selectingTargetFor?.diceSlot === die.slot,
      }"
      :data-die="`${unit.id}-${die.slot}`"
      :style="
        card !== null && !die.staggered && !isUnitBroken
          ? { background: dieColor(card) }
          : {}
      "
    >
      <span
        class="hex-inner"
        :class="{
          'hex-inner--dim-value':
            getDieDisplayValue(die) === '—' && !(die.staggered || isUnitBroken),
        }"
        >{{ getDieDisplayValue(die) }}</span
      >
    </span>

    <!-- slotted card -->
    <div class="slot-content">
      <slot />
    </div>
  </div>
</template>

<style scoped>
/* ── Slotted card content ── */
.slot-filled .slot-content {
  background: var(--bg-card-2);
}
.slot-open {
  cursor: pointer;
}
.slot-open .slot-content {
  background: #0c1e0c;
}
.slot-open:hover .slot-content {
  background: #102010;
}
.slot-empty {
  color: var(--text-3);
  font-style: italic;
  font-size: 0.68rem;
}
.slot-row {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.06rem 0.15rem 0.06rem 0;
}
.slot-content {
  flex: 1;
  min-width: 0;
  min-height: 2.2rem;
  display: flex;
  align-items: center;
  gap: 0.35rem;
}
.slot-pending .slot-content {
  background: #1a1400;
}
.slot-target {
  cursor: pointer;
}

/* ── Hexagonal die ── */
/* Two-layer clip-path creates a "border" effect without actual CSS border.
   data-die is on the outer element so ArrowOverlay can locate it correctly. */
.hex-wrap {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.4rem;
  height: 2.1rem;
  clip-path: var(--hex);
  background: var(--border-mid);
  flex-shrink: 0;
}
.hex-inner {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 1.75rem;
  clip-path: var(--hex);
  background: var(--bg-card-2);
  font-family: var(--font-body);
  font-size: 0.82rem;
  color: var(--text-1);
  pointer-events: none;
}
.hex-wrap.staggered {
  background: var(--crimson-dim);
}
.hex-wrap.staggered .hex-inner {
  background: #230808;
  color: var(--crimson-hi);
}
.hex-wrap.hex-available {
  background: var(--border-mid);
  cursor: pointer;
  animation: hex-beckon 2.2s ease-in-out infinite;
}
.hex-wrap.hex-available .hex-inner {
  color: var(--gold-dim);
}
.hex-wrap.hex-available:hover {
  animation: none;
  background: var(--gold-dim);
}
.hex-wrap.hex-available:hover .hex-inner {
  background: #141000;
  color: var(--gold);
}
@keyframes hex-beckon {
  0%,
  100% {
    background: var(--border-mid);
  }
  50% {
    background: #3a2c00;
  }
}
.slot-available {
  cursor: pointer;
}
.slot-available .slot-empty {
  color: #4a3800;
  transition: color 0.15s;
}
.slot-available:hover .slot-empty {
  color: var(--gold-dim);
}
.hex-wrap.hex-open {
  background: var(--green-hi);
  cursor: pointer;
  transition: background 0.1s;
}
.hex-wrap.hex-open .hex-inner {
  background: #0c1e0c;
  color: var(--text-1);
  transition:
    background 0.1s,
    color 0.1s;
}
.hex-wrap.hex-open:hover {
  background: #4caf50;
}
.hex-wrap.hex-open:hover .hex-inner {
  background: #102010;
  color: #fff;
}
.hex-wrap.hex-pending {
  background: var(--gold);
  animation: hex-pulse 1.2s ease-in-out infinite;
}
.hex-wrap.hex-pending .hex-inner {
  background: #1a1400;
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
.hex-inner--dim-value {
  color: var(--text-2);
}

/* ── Targetable die highlight ── */
.hex-wrap.hex-target {
  background: var(--green-hi);
  transition: background 0.1s;
}
.slot-target:hover .hex-wrap.hex-target {
  background: #4caf50;
}
.slot-target:hover .hex-wrap.hex-target .hex-inner {
  background: #102010;
  color: #fff;
}
.slot-target:hover .slot-content {
  background: #0c1e0c;
  transition: background 0.1s;
}
</style>
