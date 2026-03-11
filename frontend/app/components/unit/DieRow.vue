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
const props = withDefaults(
  defineProps<{
    unit: any;
    die: any;
    card: any;
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
} = inject(BATTLE_CTX) as BattleCtx;

type DieState =
  | "empty" // no card in slot
  | "available" // no card in slot, ready to select
  | "open" // slot selected, awaiting card
  | "pending" // card selected, awaiting target
  | "unopposed-outgoing" // target set, no clash
  | "unopposed-incoming"
  | "clash" // target set, clash
  | "broken"
  | "invalid";

const isUnitBroken = computed(
  () => props.unit.turnState === "BREAK" || isDead(props.unit),
);

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

const dieState: ComputedRef<DieState> = computed(() => {
  // die is broken
  if (props.die.staggered || isUnitBroken.value) return "broken";

  // dice has no value
  // TODO: check where dice value comes from and invalidate there
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

    return isSelectPhase.value ? "available" : "empty";
  } else {
    // card is slotted, target is set
    if (props.card.clash) return "clash";
    if (props.card.targetUnitId != null)
      return props.isAlly ? "unopposed-outgoing" : "unopposed-incoming";

    return "invalid";
  }
});

const dieDisplayValue: ComputedRef<string> = computed(() => {
  switch (dieState.value) {
    case "broken":
      return "✕";
    case "invalid":
      return "—";
    default:
      return props.die.value || "—";
  }
});

const slotState = computed(() => {
  // card is already slotted on this die
  if (props.card !== null) return "slot-filled";

  switch (dieState.value) {
    case "open":
      return "slot-open";
    case "pending":
      return "slot-pending";
    case "broken":
      return "slot-broken";
    default:
      return "slot-available";
  }
});

const isTargeting = computed(() => selectingTargetFor.value !== null);
const canBeTargeted = computed(
  () => isTargeting.value && props.unit.targetable,
);

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

  if (props.isAlly) {
    // ally: handle slot selection for playing cards only
    if (sc !== null || d.staggered || isUnitBroken.value) return;
    onSlotSelectClick(props.unit, d.slot);
  } else {
    // enemy: handle targeting only
    if (canBeTargeted.value && !d.staggered) {
      onTargetDieClick(props.unit.id, d.slot);
    }
  }
}

function targetLabel(sc: any): string {
  if (sc?.targetUnitId == null) return "";
  const u = allUnits.value.find((u: any) => u.id === sc.targetUnitId);
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
        // TODO: fix logic so that all valid targets are highlighted
        'slot-target': canBeTargeted && !isAlly && !die.staggered,
      },
    ]"
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
      :class="[
        dieState,
        {
          // TODO: fix logic so that all valid targets are highlighted
          'hex-target': canBeTargeted && !isAlly && !die.staggered,
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
    </span>

    <!-- slotted card -->
    <div class="slot-wrapper">
      <div class="slot-content">
        <SlottedCard
          v-if="card !== null"
          :card="card"
          :targetLabel="targetLabel(card) || undefined"
        />
        <div v-else class="slot-empty">—</div>
        <button
          v-if="isAlly && isSelectPhase"
          class="remove-btn"
          title="Return to hand"
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
  background: #0c1e0c;
}
.slot-open:hover .slot-wrapper {
  background: #102010;
}
.slot-empty {
  color: var(--text-3);
}
.slot-pending .slot-wrapper {
  background: #1a1400;
}
.slot-target {
  cursor: pointer;
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
/* empty (ready for select) */
.hex-wrap.available {
  background: var(--border-mid);
  cursor: pointer;
  animation: hex-beckon 2.2s ease-in-out infinite;
}
.hex-wrap.available .hex-inner {
  color: var(--gold-dim);
}
.hex-wrap.available:hover {
  animation: none;
  background: var(--gold-dim);
}
.hex-wrap.available:hover .hex-inner {
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
/* open */
.hex-wrap.open {
  background: var(--green-hi);
  cursor: pointer;
  transition: background 0.1s;
}
.hex-wrap.open .hex-inner {
  background: #0c1e0c;
  color: var(--text-1);
  transition:
    background 0.1s,
    color 0.1s;
}
.hex-wrap.open:hover {
  background: #4caf50;
}
.hex-wrap.open:hover .hex-inner {
  background: #102010;
  color: #fff;
}
/* pending */
.hex-wrap.pending {
  background: var(--gold);
  animation: hex-pulse 1.2s ease-in-out infinite;
}
.hex-wrap.pending .hex-inner {
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
  background: #230808;
  color: var(--crimson-hi);
}
/* invalid */
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

/* ── Remove button ── */
.remove-btn {
  margin-left: auto;
  flex-shrink: 0;
  background: transparent;
  border: none;
  color: var(--text-3);
  cursor: pointer;
  font-size: 0.8rem;
  padding: 0 0.15rem;
  line-height: 1;
  font-family: var(--font-body);
}
.remove-btn:hover {
  color: var(--crimson-hi);
}
</style>
