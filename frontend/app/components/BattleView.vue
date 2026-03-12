<!--
  Top-level battle scene coordinator. Owns all interactive state and provides it
  to EnemyUnit and AllyUnit via BATTLE_CTX (provide/inject), avoiding prop drilling.

  Naming convention is based on stage nomeclature, to match the theme of the game.

  Props:
    state – full battle state snapshot from the SSE stream
-->
<script setup lang="ts">
import type { BattleCtx } from "~/composables/useBattleContext";
import type {
  AllyUnit,
  GameState,
  PlayerInfo,
  Unit,
  SessionState,
  ActionResult,
} from "~/types/game";
import DisplayCard from "./unit/DisplayCard.vue";

const props = defineProps<{
  state: GameState;
  session: SessionState | null;
  players: PlayerInfo[];
  sendAction: (action: Record<string, unknown>) => Promise<ActionResult>;
  claimUnit: (unitId: number) => Promise<ActionResult>;
  releaseUnit: (unitId: number) => Promise<ActionResult>;
}>();

const session = computed(() => props.session);

/** True when this session owns the unit (or the unit is unclaimed by anyone). */
function isOwnUnit(unitId: number): boolean {
  const s = props.session;
  if (!s || !s.claimsEnabled) return true;
  return s.assignedUnits.includes(unitId);
}

// ---------------------------------------------------------------------------
// Interactive state (only meaningful during SelectCard phase)
// ---------------------------------------------------------------------------

const phase = computed(() => props.state?.phase ?? "");

const isSelectPhase = computed(
  () => props.state?.phase === "ApplyLibrarianCardPhase",
);

const selectingSlot = ref<{
  unitId: number;
  diceSlot: number;
} | null>(null);

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
let errorTimer: ReturnType<typeof setTimeout> | null = null;

watch(
  () => props.state?.phase,
  () => {
    selectingSlot.value = null;
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
  (props.state?.allies ?? []).forEach((a, i) => {
    m[a.id] = ALLY_COLORS[i % ALLY_COLORS.length]!;
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
  allSides.forEach((unit, i) => {
    if (isDead(unit)) return;
    const color = ALLY_COLORS[i % ALLY_COLORS.length]!;
    const name = unit.name ?? unit.keyPage?.name ?? `#${unit.id}`;
    (unit.slottedCards ?? []).forEach((sc) => {
      if (sc.targetUnitId == null) return;
      const bySlot = (m[sc.targetUnitId] ??= {});
      (bySlot[sc.targetSlot ?? -1] ??= []).push({
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
// Unit display order (manual reordering + dead-to-bottom)
// ---------------------------------------------------------------------------

const allyOrder = ref<number[]>([]);
const enemyOrder = ref<number[]>([]);

function syncOrder(
  order: Ref<number[]>,
  units: (Unit | AllyUnit)[] | undefined,
) {
  if (!units) return;
  const ids = units.map((u) => u.id);
  order.value = [
    ...order.value.filter((id) => ids.includes(id)),
    ...ids.filter((id) => !order.value.includes(id)),
  ];
}

watch(
  () => props.state?.allies,
  (u) => syncOrder(allyOrder, u),
  { immediate: true },
);
watch(
  () => props.state?.enemies,
  (u) => syncOrder(enemyOrder, u),
  { immediate: true },
);

function makeSorted(units: Ref<(Unit | AllyUnit)[]>, order: Ref<number[]>) {
  return computed(() =>
    [...units.value].sort((a, b) => {
      const ad = isDead(a) ? 1 : 0,
        bd = isDead(b) ? 1 : 0;
      if (ad !== bd) return ad - bd;
      return order.value.indexOf(a.id) - order.value.indexOf(b.id);
    }),
  );
}

const sortedAllies = makeSorted(
  computed(() => props.state?.allies ?? []),
  allyOrder,
);
const sortedEnemies = makeSorted(
  computed(() => props.state?.enemies ?? []),
  enemyOrder,
);

function moveUnit(
  order: Ref<number[]>,
  sorted: (Unit | AllyUnit)[],
  unitId: number,
  dir: -1 | 1,
) {
  const living = sorted.filter((u) => !isDead(u));
  const di = living.findIndex((u) => u.id === unitId);
  const ni = di + dir;
  if (ni < 0 || ni >= living.length) return;
  const arr = [...order.value];
  const ia = arr.indexOf(unitId),
    ib = arr.indexOf(living[ni]!.id);
  if (ia < 0 || ib < 0) return;
  [arr[ia], arr[ib]] = [arr[ib]!, arr[ia]!];
  order.value = arr;
}

function canMoveUp(sorted: (Unit | AllyUnit)[], unit: Unit | AllyUnit) {
  if (isDead(unit)) return false;
  return (
    sorted.filter((u) => !isDead(u)).findIndex((u) => u.id === unit.id) > 0
  );
}

function canMoveDown(sorted: (Unit | AllyUnit)[], unit: Unit | AllyUnit) {
  if (isDead(unit)) return false;
  const living = sorted.filter((u) => !isDead(u));
  const i = living.findIndex((u) => u.id === unit.id);
  return i >= 0 && i < living.length - 1;
}

function moveAlly(unitId: number, dir: -1 | 1) {
  moveUnit(allyOrder, sortedAllies.value, unitId, dir);
}
function moveEnemy(unitId: number, dir: -1 | 1) {
  moveUnit(enemyOrder, sortedEnemies.value, unitId, dir);
}

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

async function doSendAction(action: Record<string, unknown>): Promise<boolean> {
  if (errorTimer) {
    clearTimeout(errorTimer);
    errorTimer = null;
  }
  actionError.value = null;
  const { ok, error } = await props.sendAction(action);
  if (!ok) {
    actionError.value = error ?? "Action failed";
    errorTimer = setTimeout(() => {
      actionError.value = null;
      errorTimer = null;
    }, 3000);
  }
  return ok;
}

// ---------------------------------------------------------------------------
// Interaction handlers
// ---------------------------------------------------------------------------

function onCardClick(unitId: number, cardIndex: number, isEgo = false) {
  if (!isOwnUnit(unitId)) return;
  // Step 2 re-route: already in targeting for this unit → switch card
  if (selectingTargetFor.value?.unitId === unitId) {
    const diceSlot = selectingTargetFor.value.diceSlot;
    selectingTargetFor.value = null;
    routeCard(unitId, cardIndex, isEgo, diceSlot);
    return;
  }
  // Step 1 → 2: a slot for this unit must be selected first
  if (!selectingSlot.value || selectingSlot.value.unitId !== unitId) return;
  const diceSlot = selectingSlot.value.diceSlot;
  selectingSlot.value = null;
  routeCard(unitId, cardIndex, isEgo, diceSlot);
}

function routeCard(
  unitId: number,
  cardIndex: number,
  isEgo: boolean,
  diceSlot: number,
) {
  const unit = props.state?.allies?.find((u) => u.id === unitId);
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

function onSlotSelectClick(unit: Unit, diceSlot: number) {
  // In step 2, any slot click cancels
  if (selectingTargetFor.value || selectingAllyTargetFor.value) {
    cancelTargeting();
    return;
  }
  // Toggle: same slot deselects
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
  const { unitId, cardIndex, isEgo, diceSlot } = selectingAllyTargetFor.value;
  const ok = await doSendAction({
    type: "playCard",
    unitId,
    cardIndex,
    diceSlot,
    targetUnitId,
    ...(isEgo ? { isEgo: 1 } : {}),
  });
  selectingAllyTargetFor.value = null;
}

async function onTargetDieClick(targetUnitId: number, targetDiceSlot: number) {
  if (!selectingTargetFor.value) return;
  const { unitId, cardIndex, isEgo, diceSlot } = selectingTargetFor.value;
  const ok = await doSendAction({
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

async function onSelectAbnormality(cardId: number, targetUnitId?: number) {
  const body: any = { type: "selectAbnormality", cardId };
  if (targetUnitId !== undefined) body.targetUnitId = targetUnitId;
  await doSendAction(body);
}

async function onConfirm() {
  await doSendAction({ type: "confirm" });
  selectingSlot.value = null;
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
  phase,
  isSelectPhase,
  selectingSlot,
  selectingTargetFor,
  selectingAllyTargetFor,
  onCardClick,
  onSlotSelectClick,
  onTargetDieClick,
  onAllyTargetClick,
  onRemoveCard,
  cancelTargeting,
  allyColors,
  attackMap,
  allUnits,
  onSelectAbnormality,
  session,
  isOwnUnit,
  claimUnit: props.claimUnit,
  releaseUnit: props.releaseUnit,
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
      <!-- Session / claim panel (only meaningful when claim enforcement is on) -->
      <SessionPanel
        v-if="session?.claimsEnabled"
        :allies="state.allies ?? []"
        :players="players"
      />
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
  <div class="stage" @click="cancelTargeting">
    <section class="wing wing--enemy">
      <h2 class="wing-heading">Guests</h2>
      <div
        v-for="unit in sortedEnemies"
        :key="unit.id"
        class="unit-slot"
        :class="{ 'unit-slot--dead': isDead(unit) }"
      >
        <div class="unit-reorder">
          <button
            class="reorder-btn"
            :disabled="!canMoveUp(sortedEnemies, unit)"
            @click.stop="moveEnemy(unit.id, -1)"
          >
            ▲
          </button>
          <button
            class="reorder-btn"
            :disabled="!canMoveDown(sortedEnemies, unit)"
            @click.stop="moveEnemy(unit.id, 1)"
          >
            ▼
          </button>
        </div>
        <UnitDisplayCard :unit="unit" :isAlly="false" side="left" />
      </div>
    </section>

    <section class="stage-center">
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
    </section>

    <section class="wing wing--ally">
      <h2 class="wing-heading wing-heading--ally">Librarians</h2>
      <div
        v-for="unit in sortedAllies"
        :key="unit.id"
        class="unit-slot"
        :class="{ 'unit-slot--dead': isDead(unit) }"
      >
        <UnitDisplayCard :unit="unit" :isAlly="true" side="right" />
        <div class="unit-reorder">
          <button
            class="reorder-btn"
            :disabled="!canMoveUp(sortedAllies, unit)"
            @click.stop="moveAlly(unit.id, -1)"
          >
            ▲
          </button>
          <button
            class="reorder-btn"
            :disabled="!canMoveDown(sortedAllies, unit)"
            @click.stop="moveAlly(unit.id, 1)"
          >
            ▼
          </button>
        </div>
      </div>
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
    :focus-unit-id="
      selectingSlot?.unitId ??
      selectingTargetFor?.unitId ??
      selectingAllyTargetFor?.unitId ??
      null
    "
  />

  <!-- Abnormality page selection overlay -->
  <AbnormalityPicker
    v-if="state?.abnormalitySelection"
    :choices="state.abnormalitySelection.choices"
    :allies="state?.allies ?? []"
    :ally-colors="allyColors"
    :team-emotion-level="state.abnormalitySelection.teamEmotionLevel"
    :team-coin="state.abnormalitySelection.teamCoin"
    :team-coin-max="state.abnormalitySelection.teamCoinMax"
    :team-positive-coins="state.abnormalitySelection.teamPositiveCoins"
    :team-negative-coins="state.abnormalitySelection.teamNegativeCoins"
    @select="
      ({ cardId, targetUnitId }) => onSelectAbnormality(cardId, targetUnitId)
    "
  />

  <!-- Ally targeting banner -->
  <Transition name="banner">
    <div
      v-if="selectingAllyTargetFor"
      class="targeting-banner targeting-banner--ally"
    >
      <span class="targeting-card">{{ selectingAllyTargetFor.cardName }}</span>
      <span class="targeting-sep">·</span>
      <span class="targeting-slot"
        >slot {{ selectingAllyTargetFor.diceSlot }}</span
      >
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
  font-family: var(--font-body);
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
  font-family: var(--font-mono);
  white-space: nowrap;
}
.confirm-btn {
  padding: 0.25rem 1rem;
  background: var(--bg-green);
  border: 1px solid var(--green-hi);
  color: var(--green);
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
  background: var(--bg-gold);
  border-top: 2px solid var(--gold-dim);
  font-size: 0.72rem;
  font-family: var(--font-body);
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
.targeting-sep {
  color: var(--border-hi);
}
.targeting-cancel {
  margin-left: auto;
  background: transparent;
  border: 1px solid var(--border-mid);
  color: var(--text-2);
  font-size: 0.65rem;
  font-family: var(--font-body);
  padding: 0.1rem 0.4rem;
  cursor: pointer;
  white-space: nowrap;
  flex-shrink: 0;
}
.targeting-cancel:hover {
  border-color: var(--crimson);
  color: var(--crimson-hi);
}
.banner-enter-active,
.banner-leave-active {
  transition:
    opacity 0.15s,
    transform 0.15s;
}
.banner-enter-from,
.banner-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}
.targeting-banner--ally {
  background: var(--bg-green);
  border-top-color: var(--green-hi);
}

/* ── Error banner ──────────────────────────────────────────────────────────── */
.banner-error {
  padding: 0.3rem 0.75rem;
  font-size: 0.72rem;
  font-family: var(--font-body);
  background: #180808;
  border: 1px solid var(--crimson);
  color: var(--text-red);
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
  display: flex;
  flex-direction: column;
  align-items: center;
}

.wing {
  display: flex;
  flex-direction: column;
  flex: 1;
  gap: 0.5rem;
  min-width: 0;
  max-width: 28rem;
}

.unit-slot {
  display: flex;
  width: 100%;
  flex-direction: row;
  align-items: stretch;
  gap: 0.2rem;
}

.unit-slot--dead {
  opacity: 0.4;
  pointer-events: none;
}

.unit-reorder {
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 0.1rem;
  flex-shrink: 0;
}

.reorder-btn {
  background: transparent;
  border: none;
  color: var(--text-3);
  font-size: 0.55rem;
  line-height: 1;
  padding: 0.15rem 0.1rem;
  cursor: pointer;
  display: block;
}
.reorder-btn:hover:not(:disabled) {
  color: var(--text-1);
}
.reorder-btn:disabled {
  opacity: 0.2;
  cursor: default;
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
