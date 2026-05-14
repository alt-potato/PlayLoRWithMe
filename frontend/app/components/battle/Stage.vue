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
  ClientAction,
  GameState,
  PlayerInfo,
  SessionState,
  ActionResult,
} from "~/types/game";
import UnitDisplayCard from "~/components/unit/DisplayCard.vue";

const props = defineProps<{
  state: GameState;
  session: SessionState | null;
  players: PlayerInfo[];
  sendAction: (action: ClientAction) => Promise<ActionResult>;
  claimUnit: (unitId: number) => Promise<ActionResult>;
  releaseUnit: (unitId: number) => Promise<ActionResult>;
  renamePlayer: (name: string) => Promise<ActionResult>;
}>();

const session = toRef(() => props.session);

/**
 * True when the current session can issue actions for this unit right now —
 * the session must own the claim AND the unit must not be mind-controlled
 * (the serializer emits `controllable: false` when a charm-class buff has
 * flipped `IsControllable` off in-game). Hand display, slot interactions,
 * and remove buttons all gate on this combined check so mind-controlled
 * units reuse the existing unclaimed-style affordance.
 */
function isOwnUnit(unitId: number): boolean {
  const ally = props.state?.allies?.find((u) => u.id === unitId);
  if (ally?.controllable === false) return false;
  const s = props.session;
  if (!s || !s.claimsEnabled) return true;
  return s.assignedUnits.includes(unitId);
}

// ---------------------------------------------------------------------------
// Interactive state (only meaningful during SelectCard phase)
// ---------------------------------------------------------------------------

const phase = computed(() => props.state?.phase ?? "");

const debugEnabled = computed(() => useRoute().query.debug === "1");

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

// ---------------------------------------------------------------------------
// Derived display data
// ---------------------------------------------------------------------------

const allyColors = computed(() => buildAllyColors(props.state?.allies ?? []));

const attackMap = computed(() => {
  const m: Record<
    number,
    Record<number, Array<{ name: string; color: string; range: string }>>
  > = {};
  const ac = allyColors.value;
  const ENEMY_COLOR = "var(--crimson)";

  for (const unit of [...(props.state?.allies ?? []), ...(props.state?.enemies ?? [])]) {
    if (isDead(unit)) continue;
    const color = ac[unit.id] ?? ENEMY_COLOR;
    const name = unit.name ?? unit.keyPage?.name ?? `#${unit.id}`;
    for (const sc of unit.slottedCards ?? []) {
      if (sc.targetUnitId == null) continue;
      const bySlot = (m[sc.targetUnitId] ??= {});
      (bySlot[sc.targetSlot ?? -1] ??= []).push({
        name,
        color,
        range: sc.range ?? "",
      });
    }
  }
  return m;
});

const allUnits = computed(() => [
  ...(props.state?.allies ?? []),
  ...(props.state?.enemies ?? []),
]);

/**
 * Per-selection actor → restricted-target lookup. Mirrors the vanilla
 * `BlockOtherUnitsDice` path: when a die is held selected on an actor with
 * non-empty `fixedTargets`, every other unit dims. Recomputed reactively as
 * the selection state changes, so the cue clears on deselect (matching
 * vanilla's `UnblockOtherUnitsDice`).
 */
const restrictedTargetSet = computed<Set<number> | null>(() => {
  const actorId =
    selectingSlot.value?.unitId ??
    selectingTargetFor.value?.unitId ??
    selectingAllyTargetFor.value?.unitId ??
    null;
  if (actorId == null) return null;
  const actor = allUnits.value.find((u) => u.id === actorId);
  const fixed = actor?.fixedTargets;
  if (!fixed || fixed.length === 0) return null;
  return new Set(fixed);
});

function isRestrictedTarget(unitId: number): boolean {
  const set = restrictedTargetSet.value;
  return set !== null && !set.has(unitId);
}

// ---------------------------------------------------------------------------
// Unit display order (manual reordering + dead-to-bottom)
// ---------------------------------------------------------------------------

const { sortedAllies, sortedEnemies, moveAlly, moveEnemy, canMoveUp, canMoveDown } =
  useBattleOrdering({
    allies: computed(() => props.state?.allies ?? []),
    enemies: computed(() => props.state?.enemies ?? []),
  });

// ---------------------------------------------------------------------------
// Action dispatch + interaction handlers
// ---------------------------------------------------------------------------

const stateRef = toRef(() => props.state);

const {
  actionError,
  onCardClick,
  onSlotSelectClick,
  onTargetDieClick,
  onAllyTargetClick,
  onRemoveCard,
  onSelectAbnormality,
  onConfirm,
  cancelTargeting,
  cleanupErrorTimer,
} = useBattleActions({
  sendAction: props.sendAction,
  selectingSlot,
  selectingTargetFor,
  selectingAllyTargetFor,
  isOwnUnit,
  state: stateRef,
});

// ---------------------------------------------------------------------------
// Screen width — only show arrow overlay on wide screens
// ---------------------------------------------------------------------------

// The breakpoint where the stage flips from one-column (mobile) to three-column
// (enemy | center | ally). Mirrored in the @media (min-width: ...px) rules
// below; CSS custom properties cannot drive @media query conditions, so the
// literal lives here as a named constant and the matching @media rules carry a
// comment pointing back at it.
const STAGE_WIDE_BREAKPOINT_PX = 600;
const STAGE_WIDE_QUERY = `(min-width: ${STAGE_WIDE_BREAKPOINT_PX}px)`;

const showArrows = ref(false);

let arrowMq: MediaQueryList | null = null;

function onMediaChange(e: MediaQueryListEvent) {
  showArrows.value = e.matches;
}

onMounted(() => {
  arrowMq = window.matchMedia(STAGE_WIDE_QUERY);
  showArrows.value = arrowMq.matches;
  arrowMq.addEventListener("change", onMediaChange);
});

onBeforeUnmount(() => {
  arrowMq?.removeEventListener("change", onMediaChange);
  cleanupErrorTimer();
});

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
  isRestrictedTarget,
  claimUnit: props.claimUnit,
  releaseUnit: props.releaseUnit,
} satisfies BattleCtx);
</script>

<template>
  <!-- Status bar -->
  <BattleStatusBar
    :stage="state.stage"
    :phase="debugEnabled ? state.phase : undefined"
    :confirm-enabled="isSelectPhase"
    :confirm-label="isSelectPhase ? 'START' : 'WAITING'"
    @confirm="onConfirm"
  />

  <!-- Error banner -->
  <div v-if="actionError" class="banner-error" role="alert">{{ actionError }}</div>

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
            aria-label="Move unit up"
            @click.stop="moveEnemy(unit.id, -1)"
          >
            ▲
          </button>
          <button
            class="reorder-btn"
            :disabled="!canMoveDown(sortedEnemies, unit)"
            aria-label="Move unit down"
            @click.stop="moveEnemy(unit.id, 1)"
          >
            ▼
          </button>
        </div>
        <UnitDisplayCard :unit="unit" :isAlly="false" side="left" />
      </div>
    </section>

    <section class="stage-center">
      <div
        v-if="showArrows && state.allies && state.enemies"
        class="arrow-toggles"
      >
        <button
          class="toggle-btn"
          :class="{ active: showIncoming }"
          :style="showIncoming ? { '--tc': 'var(--incoming)' } : {}"
          title="Incoming one-sided attacks"
          aria-label="Toggle incoming one-sided attacks"
          :aria-pressed="showIncoming"
          @click="showIncoming = !showIncoming"
        >
          →
        </button>
        <button
          class="toggle-btn"
          :class="{ active: showClash }"
          :style="showClash ? { '--tc': 'var(--clash)' } : {}"
          title="Clashes"
          aria-label="Toggle clashes"
          :aria-pressed="showClash"
          @click="showClash = !showClash"
        >
          ⚔
        </button>
        <button
          class="toggle-btn"
          :class="{ active: showOutgoing }"
          :style="showOutgoing ? { '--tc': 'var(--outgoing)' } : {}"
          title="Outgoing one-sided attacks"
          aria-label="Toggle outgoing one-sided attacks"
          :aria-pressed="showOutgoing"
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
            aria-label="Move unit up"
            @click.stop="moveAlly(unit.id, -1)"
          >
            ▲
          </button>
          <button
            class="reorder-btn"
            :disabled="!canMoveDown(sortedAllies, unit)"
            aria-label="Move unit down"
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

  <!-- Emotion level-up selection overlay (key page or abnormality card) -->
  <LazyEmotionUpgradePicker
    v-if="state?.abnormalitySelection"
    :selection="state.abnormalitySelection"
    :allies="state?.allies ?? []"
    :ally-colors="allyColors"
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
      <button class="targeting-cancel" aria-label="Cancel ally targeting" @click="cancelTargeting">cancel</button>
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
      <button class="targeting-cancel" aria-label="Cancel targeting" @click="cancelTargeting">cancel</button>
    </div>
  </Transition>
</template>

<style scoped>
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
  font-size: var(--fs-3xs);
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
  font-size: var(--fs-3xs);
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
  font-size: var(--fs-3xs);
  font-family: var(--font-body);
  background: var(--bg-crimson-deep);
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
  display: flex;
  justify-content: space-between;

  /* Mobile: single column, enemies → allies */
  flex-direction: column;
  gap: 1rem;
}

/* Mirrors STAGE_WIDE_BREAKPOINT_PX in the script section. */
@media (min-width: 600px) {
  /* Tablet+: enemy | fill | ally — sides are content-sized, middle expands */
  .stage {
    flex-direction: row;
    gap: 0.75rem;
  }
}

.stage-center {
  flex: none;
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
.wing--ally .unit-slot {
  justify-content: right;
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
  font-size: var(--fs-4xs);
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
  font-size: var(--fs-3xs);
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
/* Mirrors STAGE_WIDE_BREAKPOINT_PX in the script section. */
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
  font-size: var(--fs-xs);
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
