<!--
  BattleView.vue

  Top-level battle scene coordinator. Owns all interactive state and provides it
  to EnemyUnit and AllyUnit via BATTLE_CTX (provide/inject), avoiding prop drilling.

  Props:
    state – full battle state snapshot from the SSE stream
-->
<script setup lang="ts">
import type { BattleCtx } from "~/composables/useBattleContext";

const props = defineProps<{ state: any }>();

// ---------------------------------------------------------------------------
// Interactive state (only meaningful during SelectCard phase)
// ---------------------------------------------------------------------------

const isSelectPhase = computed(
  () => props.state?.phase === "ApplyLibrarianCardPhase",
);

const selectingSlotFor = ref<{ unitId: number; cardIndex: number; isEgo: boolean } | null>(
  null,
);

const selectingTargetFor = ref<{
  unitId: number;
  cardIndex: number;
  isEgo: boolean;
  diceSlot: number;
  cardName: string;
  cardRange: string;
} | null>(null);

const selectingAllyTargetFor = ref<{
  unitId: number;
  cardIndex: number;
  isEgo: boolean;
  diceSlot: number;
  cardName: string;
} | null>(null);

const actionError = ref<string | null>(null);

watch(
  () => props.state?.phase,
  () => {
    selectingSlotFor.value = null;
    selectingTargetFor.value = null;
    selectingAllyTargetFor.value = null;
    actionError.value = null;
  },
);

// ---------------------------------------------------------------------------
// Derived display data
// ---------------------------------------------------------------------------

const ALLY_COLORS = ["#4fc3f7", "#81c784", "#ffb74d", "#ce93d8", "#f48fb1"];

const allyColors = computed<Record<number, string>>(() => {
  const m: Record<number, string> = {};
  (props.state?.allies ?? []).forEach((a: any, i: number) => {
    m[a.id] = ALLY_COLORS[i % ALLY_COLORS.length];
  });
  return m;
});

const attackMap = computed(() => {
  const m: Record<
    number,
    Record<number, Array<{ name: string; color: string; range: string }>>
  > = {};
  const allSides = [
    ...(props.state?.allies ?? []),
    ...(props.state?.enemies ?? []),
  ];
  allSides.forEach((unit: any, i: number) => {
    const color = ALLY_COLORS[i % ALLY_COLORS.length];
    const name = unit.name ?? unit.keyPage?.name ?? `#${unit.id}`;
    (unit.slottedCards ?? []).forEach((sc: any) => {
      if (sc.targetUnitId == null) return;
      (m[sc.targetUnitId] ??= {})[sc.targetSlot] ??= [];
      m[sc.targetUnitId][sc.targetSlot].push({
        name,
        color,
        range: sc.range ?? "",
      });
    });
  });
  return m;
});

const allUnits = computed(() => [
  ...(props.state?.allies ?? []),
  ...(props.state?.enemies ?? []),
]);

// ---------------------------------------------------------------------------
// Screen width — only show arrow overlay on wide screens
// ---------------------------------------------------------------------------

const showArrows = ref(false);

onMounted(() => {
  const mq = window.matchMedia("(min-width: 900px)");
  showArrows.value = mq.matches;
  mq.addEventListener("change", (e: MediaQueryListEvent) => {
    showArrows.value = e.matches;
  });
});

// ---------------------------------------------------------------------------
// Action senders
// ---------------------------------------------------------------------------

async function sendAction(action: object): Promise<boolean> {
  actionError.value = null;
  try {
    const res = await fetch("/action", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(action),
    });
    if (!res.ok) {
      const data = await res.json().catch(() => ({}))
      actionError.value = (data as any).error ?? `Server error ${res.status}`;
      setTimeout(() => { actionError.value = null }, 3000);
      return false;
    }
    return true;
  } catch (e) {
    actionError.value = String(e);
    return false;
  }
}

// ---------------------------------------------------------------------------
// Interaction handlers
// ---------------------------------------------------------------------------

function onCardClick(unitId: number, cardIndex: number, isEgo = false) {
  if (
    selectingSlotFor.value?.unitId === unitId &&
    selectingSlotFor.value?.cardIndex === cardIndex &&
    selectingSlotFor.value?.isEgo === isEgo
  ) {
    selectingSlotFor.value = null;
  } else {
    selectingSlotFor.value = { unitId, cardIndex, isEgo };
    selectingTargetFor.value = null;
  }
}

async function onSlotClick(unit: any, cardIndex: number, diceSlot: number) {
  const isEgo = selectingSlotFor.value?.isEgo ?? false;
  selectingSlotFor.value = null;
  const cardList = isEgo ? (unit.ego ?? []) : (unit.hand ?? []);
  const card = cardList[cardIndex];
  if (card?.range === "Instance") {
    if (card?.allyTarget) {
      // Ally-targeting instant: need the player to pick an ally before sending
      selectingAllyTargetFor.value = {
        unitId: unit.id,
        cardIndex,
        isEgo,
        diceSlot,
        cardName: card?.name ?? "?",
      };
    } else {
      await sendAction({
        type: "playCard",
        unitId: unit.id,
        cardIndex,
        diceSlot,
        ...(isEgo ? { isEgo: 1 } : {}),
      });
    }
  } else {
    selectingTargetFor.value = {
      unitId: unit.id,
      cardIndex,
      isEgo,
      diceSlot,
      cardName: card?.name ?? "?",
      cardRange: card?.range ?? "",
    };
  }
}

async function onAllyTargetClick(targetUnitId: number) {
  if (!selectingAllyTargetFor.value) return;
  const { unitId, cardIndex, isEgo, diceSlot } = selectingAllyTargetFor.value;
  const ok = await sendAction({
    type: "playCard",
    unitId,
    cardIndex,
    diceSlot,
    targetUnitId,
    ...(isEgo ? { isEgo: 1 } : {}),
  });
  if (ok) selectingAllyTargetFor.value = null;
}

async function onTargetDieClick(targetUnitId: number, targetDiceSlot: number) {
  if (!selectingTargetFor.value) return;
  const { unitId, cardIndex, isEgo, diceSlot } = selectingTargetFor.value;
  const ok = await sendAction({
    type: "playCard",
    unitId,
    cardIndex,
    diceSlot,
    targetUnitId,
    targetDiceSlot,
    ...(isEgo ? { isEgo: 1 } : {}),
  });
  if (ok) selectingTargetFor.value = null;
}

function cancelTargeting() {
  selectingTargetFor.value = null;
  selectingAllyTargetFor.value = null;
  selectingSlotFor.value = null;
}

async function onRemoveCard(unitId: number, diceSlot: number) {
  await sendAction({ type: "removeCard", unitId, diceSlot });
  if (selectingTargetFor.value?.unitId === unitId && selectingTargetFor.value?.diceSlot === diceSlot)
    selectingTargetFor.value = null;
  if (selectingAllyTargetFor.value?.unitId === unitId && selectingAllyTargetFor.value?.diceSlot === diceSlot)
    selectingAllyTargetFor.value = null;
  if (selectingSlotFor.value?.unitId === unitId) selectingSlotFor.value = null;
}

async function onConfirm() {
  await sendAction({ type: "confirm" });
  selectingSlotFor.value = null;
  selectingTargetFor.value = null;
}

// ---------------------------------------------------------------------------
// Arrow overlay toggles
// ---------------------------------------------------------------------------

const showIncoming = ref(true);
const showClash = ref(true);
const showOutgoing = ref(true);

// ---------------------------------------------------------------------------
// Provide context
// ---------------------------------------------------------------------------

provide(BATTLE_CTX, {
  isSelectPhase,
  selectingSlotFor,
  selectingTargetFor,
  selectingAllyTargetFor,
  onCardClick,
  onSlotClick,
  onTargetDieClick,
  onAllyTargetClick,
  onRemoveCard,
  allyColors,
  attackMap,
  allUnits,
} satisfies BattleCtx);
</script>

<template>
  <!-- Teaser (top bar) -->
  <div class="teaser-bar">
    <div class="teaser-left">
      <div class="teaser-info">
        <span class="teaser-item"
          ><span class="k">Floor</span>
          <strong>{{ state.stage?.floor }}</strong></span
        >
        <span class="teaser-sep">·</span>
        <span class="teaser-item"
          ><span class="k">Ch</span>
          <strong>{{ state.stage?.chapter }}</strong></span
        >
        <span class="teaser-sep">·</span>
        <span class="teaser-item"
          ><span class="k">Wave</span>
          <strong>{{ state.stage?.wave }}</strong></span
        >
        <span class="teaser-sep">·</span>
        <span class="teaser-item"
          ><span class="k">Round</span>
          <strong>{{ state.stage?.round }}</strong></span
        >
      </div>
      <span class="phase">{{ state.phase }}</span>
    </div>

    <div class="teaser-center">
      <button class="confirm-btn" :disabled="!isSelectPhase" @click="onConfirm">
        {{ isSelectPhase ? "CONFIRM" : "WAITING" }}
      </button>
    </div>

    <div class="teaser-right">
      <div class="arrow-toggles">
        <button
          class="toggle-btn"
          :class="{ active: showIncoming }"
          :style="showIncoming ? { '--tc': '#c62828' } : {}"
          title="Incoming one-sided attacks"
          @click="showIncoming = !showIncoming"
        >
          →
        </button>
        <button
          class="toggle-btn"
          :class="{ active: showClash }"
          :style="showClash ? { '--tc': '#c9a227' } : {}"
          title="Clashes"
          @click="showClash = !showClash"
        >
          ⚔
        </button>
        <button
          class="toggle-btn"
          :class="{ active: showOutgoing }"
          :style="showOutgoing ? { '--tc': '#4fc3f7' } : {}"
          title="Outgoing one-sided attacks"
          @click="showOutgoing = !showOutgoing"
        >
          ←
        </button>
      </div>
    </div>
  </div>

  <!-- Error banner -->
  <div v-if="actionError" class="banner-error">{{ actionError }}</div>

  <!-- Stage (battlefield) -->
  <!-- 
    DISCLAIMER: i am fully aware that stage left/right is reversed on an actual stage, 
    but it gets confusing since this is not actually a stage. please understand.
    (i don't think i actually use the terms "left" and "right" here, but preempting, yknow?)
  -->
  <div class="stage">
    <section class="wing wing--enemy">
      <h2 class="wing-heading">Guests</h2>
      <EnemyUnit v-for="unit in state.enemies" :key="unit.id" :unit="unit" />
    </section>

    <section class="stage-center"></section>

    <section class="wing wing--ally">
      <h2 class="wing-heading wing-heading--ally">Librarians</h2>
      <AllyUnit v-for="unit in state.allies" :key="unit.id" :unit="unit" />
    </section>
  </div>

  <!-- Arrow SVG overlay -- desktop only, reactively tracked -->
  <ArrowOverlay
    v-if="showArrows && state.allies && state.enemies"
    :allies="state.allies"
    :enemies="state.enemies"
    :show-incoming="showIncoming"
    :show-clash="showClash"
    :show-outgoing="showOutgoing"
  />

  <!-- Ally targeting banner -->
  <Transition name="banner">
    <div v-if="selectingAllyTargetFor" class="targeting-banner targeting-banner--ally">
      <span class="targeting-card">{{ selectingAllyTargetFor.cardName }}</span>
      <span class="targeting-sep">·</span>
      <span class="targeting-slot">slot {{ selectingAllyTargetFor.diceSlot }}</span>
      <span class="targeting-sep">·</span>
      <span class="targeting-hint">select a Librarian</span>
      <button class="targeting-cancel" @click="cancelTargeting">cancel</button>
    </div>
  </Transition>

  <!-- Targeting banner -->
  <Transition name="banner">
    <div v-if="selectingTargetFor" class="targeting-banner">
      <span class="targeting-card">{{ selectingTargetFor.cardName }}</span>
      <span class="targeting-sep">·</span>
      <span class="targeting-slot">slot {{ selectingTargetFor.diceSlot }}</span>
      <span class="targeting-sep">·</span>
      <span class="targeting-hint">select a target</span>
      <button class="targeting-cancel" @click="cancelTargeting">cancel</button>
    </div>
  </Transition>
</template>

<style scoped>
/* ── Stage bar ─────────────────────────────────────────────────────────────── */
.teaser-bar {
  padding: 0.4rem 0.75rem;
  background: var(--bg-surface);
  border: 1px solid var(--border-mid);
  border-bottom: 1px solid var(--gold-dim);
  margin-bottom: 0.75rem;

  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  gap: 0.5rem;
}
.teaser-left {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 0;
}
.teaser-center {
  display: flex;
  justify-content: center;
}
.teaser-right {
  display: flex;
  justify-content: flex-end;
  align-items: center;
}
.teaser-info {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  flex-wrap: wrap;
}
.teaser-item {
  font-size: 0.72rem;
  color: var(--text-2);
  font-family: var(--font-mono);
}
.teaser-item .k {
  font-family: var(--font-display);
  font-size: 0.58rem;
  letter-spacing: 0.07em;
  text-transform: uppercase;
  color: var(--text-2);
}
.teaser-item strong {
  color: var(--gold);
}
.teaser-sep {
  color: var(--border-hi);
  font-size: 0.65rem;
}

.phase {
  color: var(--text-2);
  font-size: 0.65rem;
  font-style: italic;
  font-family: var(--font-body);
  white-space: nowrap;
}
.confirm-btn {
  padding: 0.25rem 1rem;
  background: #0a1a0a;
  border: 1px solid var(--green-hi);
  color: #4caf50;
  font-size: 0.7rem;
  font-family: var(--font-display);
  letter-spacing: 0.08em;
  cursor: pointer;
  white-space: nowrap;
  transition:
    background 0.15s,
    color 0.15s;
}
.confirm-btn:hover:not(:disabled) {
  background: var(--green-hi);
  color: #fff;
}
.confirm-btn:disabled {
  border-color: var(--border-mid);
  color: var(--text-3);
  background: transparent;
  cursor: not-allowed;
  opacity: 0.5;
}

/* ── Targeting banner ──────────────────────────────────────────────────────── */
.targeting-banner {
  position: fixed;
  left: 0;
  right: 0;
  bottom: 0;
  padding: 0.5rem 0.75rem calc(0.5rem + env(safe-area-inset-bottom, 0px));
  display: flex;
  align-items: center;
  gap: 0.4rem;
  background: #1a1400;
  border-top: 2px solid var(--gold-dim);
  font-size: 0.72rem;
  font-family: var(--font-mono);
  z-index: 99;
}
.targeting-card {
  color: var(--gold-bright);
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
}
.targeting-slot {
  color: var(--text-2);
  white-space: nowrap;
}
.targeting-hint {
  color: var(--text-3);
  font-style: italic;
  flex: 1;
  white-space: nowrap;
}
.targeting-sep { color: var(--border-hi); }
.targeting-cancel {
  margin-left: auto;
  background: transparent;
  border: 1px solid var(--border-mid);
  color: var(--text-2);
  font-size: 0.65rem;
  font-family: var(--font-mono);
  padding: 0.1rem 0.4rem;
  cursor: pointer;
  white-space: nowrap;
  flex-shrink: 0;
}
.targeting-cancel:hover { border-color: var(--crimson); color: var(--crimson-hi); }
.banner-enter-active, .banner-leave-active { transition: opacity 0.15s, transform 0.15s; }
.banner-enter-from, .banner-leave-to { opacity: 0; transform: translateY(-4px); }
.targeting-banner--ally {
  background: #0a1a0a;
  border-top-color: var(--green-hi);
}

/* ── Error banner ──────────────────────────────────────────────────────────── */
.banner-error {
  padding: 0.3rem 0.75rem;
  font-size: 0.72rem;
  font-family: var(--font-mono);
  background: #180808;
  border: 1px solid var(--crimson);
  color: #ef9a9a;
  margin-left: 0.5em;
  margin-right: 0.5em;
  margin-bottom: 0.5rem;
}

/* ── Stage layout ────────────────────────────────────────────────────── */

.stage {
  margin-left: 0.6em;
  margin-right: 0.6em;

  /* Mobile: single column, enemies → allies */
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

@media (min-width: 600px) {
  /* Tablet+: enemy | fill | ally — sides are content-sized, middle expands */
  .stage {
    flex-direction: row;
    gap: 0.75rem;
  }
}

.stage-center {
  flex: 1;
}

.wing {
  display: flex;
  flex-direction: column;
  flex: 1;
  gap: 0.5rem;
  min-width: 0;
  max-width: 28rem;
}

.wing-heading {
  font-family: var(--font-display);
  font-size: 0.65rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.16em;
  color: var(--crimson-hi);
  margin-bottom: 0.3rem;
}
.wing--ally {
  align-items: flex-end;
}
.wing-heading--ally {
  color: var(--gold);
  text-align: right;
}
@media (min-width: 600px) {
  .wing-heading--ally {
    text-align: left;
  }
}
/* ── Arrow toggle buttons ──────────────────────────────────────────────────── */
.arrow-toggles {
  display: flex;
  flex-direction: row;
  gap: 0.25rem;
}

.toggle-btn {
  position: relative;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 1.8rem;
  height: 1.8rem;
  background: transparent;
  border: 1px solid var(--border-mid);
  color: var(--border-hi);
  font-size: 0.85rem;
  cursor: pointer;
  transition:
    border-color 0.15s,
    color 0.15s,
    opacity 0.15s;
}
.toggle-btn::before {
  content: "";
  position: absolute;
  inset: 0;
  background: var(--tc, transparent);
  opacity: 0;
  transition: opacity 0.15s;
}
.toggle-btn.active {
  border-color: var(--tc, var(--border-hi));
  color: var(--tc, var(--border-hi));
  opacity: 1;
}
.toggle-btn.active::before {
  opacity: 0.18;
}
.toggle-btn:not(.active) {
  opacity: 0.3;
}
.toggle-btn:hover {
  opacity: 1;
}
</style>
