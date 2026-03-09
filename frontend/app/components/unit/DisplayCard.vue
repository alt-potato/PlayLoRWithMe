<!-- 
  Displays a single unit card. Header shows turn state, name, light pips,
  and emotion level. Passives, abnormalities, and resistances are collapsed
  into a single details section.

  Props:
    unit – unit object from the game state snapshot
-->
<script setup lang="ts">
import type { BattleCtx } from "~/composables/useBattleContext";

const props = defineProps<{ unit: any; isAlly: boolean }>();

const {
  isSelectPhase,
  selectingSlot,
  selectingTargetFor,
  selectingAllyTargetFor,
  onCardClick,
  onSlotSelectClick,
  onRemoveCard,
  onAllyTargetClick,
  cancelTargeting,
  allyColors,
  allUnits,
} = inject(BATTLE_CTX) as BattleCtx;

const isAllyTargeting = computed(
  () =>
    selectingAllyTargetFor.value !== null &&
    selectingAllyTargetFor.value.unitId !== props.unit.id,
);

const isUnitBroken = computed(
  () => props.unit.turnState === "BREAK" || isDead(props.unit),
);

const myColor = computed(() => allyColors.value[props.unit.id] ?? "#888");

const slots = computed(() => sortedSlots(props.unit));

function dieColor(sc: any): string {
  if (sc.clash) return ARROW_COLORS.clash;
  if (sc.targetUnitId != null) return ARROW_COLORS.outgoing;
  return myColor.value; // Instance / untargeted
}

function targetLabel(sc: any): string {
  if (sc?.targetUnitId == null) return "";
  const u = allUnits.value.find((u: any) => u.id === sc.targetUnitId);
  const prefix = sc.clash ? "⚔" : "↗";
  return `${prefix} ${u?.name ?? `#${sc.targetUnitId}`} ·${sc.targetSlot}`;
}

const detailCard = ref<any>(null);
const egoMode = ref(false);
const expandedBuff = ref<string | null>(null);
const handExpanded = ref(false);

const shouldAutoExpand = computed(
  () =>
    selectingSlot.value?.unitId === props.unit.id ||
    selectingTargetFor.value?.unitId === props.unit.id ||
    selectingAllyTargetFor.value?.unitId === props.unit.id,
);

const showHandCards = computed(
  () => handExpanded.value || shouldAutoExpand.value,
);

watch(isSelectPhase, () => {
  handExpanded.value = false;
});

let slotLongPressed = false;
let slotPressTimer: ReturnType<typeof setTimeout> | null = null;

function onSlotPressStart(sc: any) {
  if (!sc) return;
  slotLongPressed = false;
  slotPressTimer = setTimeout(() => {
    slotLongPressed = true;
    detailCard.value = sc;
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
function toggleBuff(type: string) {
  expandedBuff.value = expandedBuff.value === type ? null : type;
}

const hasEgo = computed(() => (props.unit.ego?.length ?? 0) > 0);

watch(hasEgo, (val) => {
  if (!val) egoMode.value = false;
});

const hasDetails = computed(() => {
  const u = props.unit;
  return (
    (!isSelectPhase.value && u.hand?.length) ||
    u.ego?.length ||
    u.passives?.length ||
    u.abnormalities?.length ||
    u.keyPage
  );
});

const detailsLabel = computed(() => {
  const u = props.unit;
  const parts: string[] = [];
  if (!isSelectPhase.value && u.hand?.length)
    parts.push(`Hand (${u.hand.length})`);
  if (u.ego?.length) parts.push(`EGO (${u.ego.length})`);
  if (u.passives?.length) parts.push(`Passives (${u.passives.length})`);
  if (u.abnormalities?.length) parts.push(`Abn. (${u.abnormalities.length})`);
  if (u.keyPage) parts.push("Res.");
  return parts.length ? parts.join(" · ") : "Details";
});

function passiveClass(p: any) {
  const cls: Record<string, boolean> = {
    unavailable: !!p.disabled,
    "passive-negative": !!p.isNegative,
  };
  if (p.rare) cls[`rarity-${p.rare.toLowerCase()}`] = true;
  return cls;
}
</script>

<template>
  <div class="unit-card">
    <!-- ── Header ── -->
    <div class="unit-header">
      <!-- row 1: turn badge, name -->
      <div class="unit-header-1">
        <span
          class="state-badge"
          :style="{
            background: isDead(unit) ? '#e53935' : turnColor(unit.turnState),
          }"
          >{{ isDead(unit) ? "DEAD" : turnLabel(unit.turnState) }}</span
        >
        <span class="unit-name">{{
          unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}`
        }}</span>
      </div>

      <!-- row 2: light pips, emotion level -->
      <div class="unit-header-2">
        <UnitLightDisplay
          :current="unit.playPoint"
          :max="unit.maxPlayPoint"
          :reserved="unit.reservedPlayPoint"
        />
        <UnitEmotionDisplay
          :positive="unit.emotionCoins.positive"
          :negative="unit.emotionCoins.negative"
          :max="unit.emotionCoins.max"
          :level="unit.emotionLevel"
        />
      </div>
    </div>

    <!-- ── HP / SP bars ── -->
    <UnitStatusDisplay
      :hp="unit.hp"
      :maxHp="unit.maxHp"
      :sg="unit.staggerGauge"
      :maxSg="unit.maxStaggerGauge"
    />

    <!-- ── Speed dice + slotted cards ── -->
    <div v-if="slots.length && !isDead(unit)" class="slot-list">
      <div
        v-for="{ die: d, card: sc } in slots"
        :key="d.slot"
        class="slot-row"
        :class="{
          'slot-filled': sc !== null,
          'slot-available':
            isSelectPhase &&
            selectingSlot === null &&
            sc === null &&
            !d.staggered &&
            !isUnitBroken,
          'slot-open':
            isSelectPhase &&
            selectingSlot?.unitId === unit.id &&
            selectingSlot?.diceSlot === d.slot &&
            sc === null &&
            !d.staggered &&
            !isUnitBroken,
          'slot-pending':
            isSelectPhase &&
            selectingTargetFor?.unitId === unit.id &&
            selectingTargetFor?.diceSlot === d.slot,
        }"
        @click.stop="handleSlotClick(d, sc)"
        @mousedown="onSlotPressStart(sc)"
        @mouseup="onSlotPressEnd"
        @mouseleave="onSlotPressEnd"
        @touchstart.passive="onSlotPressStart(sc)"
        @touchend="onSlotPressEnd"
        @touchmove="onSlotPressEnd"
      >
        <!-- Hexagonal die (data-die used by ArrowOverlay for coordinate lookup) -->
        <span
          class="hex-wrap"
          :class="{
            staggered: d.staggered || isUnitBroken,
            'hex-available':
              isSelectPhase &&
              selectingSlot === null &&
              sc === null &&
              !d.staggered &&
              !isUnitBroken,
            'hex-open':
              isSelectPhase &&
              selectingSlot?.unitId === unit.id &&
              selectingSlot?.diceSlot === d.slot &&
              sc === null &&
              !d.staggered &&
              !isUnitBroken,
            'hex-pending':
              isSelectPhase &&
              selectingTargetFor?.unitId === unit.id &&
              selectingTargetFor?.diceSlot === d.slot,
          }"
          :data-die="`${unit.id}-${d.slot}`"
          :style="
            sc !== null && !d.staggered && !isUnitBroken
              ? { background: dieColor(sc) }
              : {}
          "
        >
          <span class="hex-inner">{{
            d.staggered || isUnitBroken ? "✕" : d.value || "—"
          }}</span>
        </span>

        <div class="slot-content">
          <template v-if="sc !== null">
            <SlottedCard
              :sc="sc"
              :target-label="targetLabel(sc) || undefined"
              :clash="sc.clash"
              :my-color="myColor"
            />
            <button
              v-if="isSelectPhase"
              class="remove-btn"
              title="Return to hand"
              @click="onRemoveCard(unit.id, sc.slot)"
            >
              ✕
            </button>
          </template>
          <template v-else>
            <span class="slot-empty">—</span>
          </template>
        </div>
      </div>
    </div>

    <!-- ── Interactive hand (for allies in select phase only) ── -->
    <template v-if="isAlly && isSelectPhase && (unit.hand?.length || hasEgo)">
      <div class="hand-section">
        <div class="hand-header" @click.stop="handExpanded = !handExpanded">
          <span class="section-label">
            <span class="hand-chevron">{{ showHandCards ? "▾" : "▸" }}</span>
            {{ egoMode ? "EGO hand" : "Hand" }}
            <span class="hand-count"
              >({{
                egoMode ? (unit.ego?.length ?? 0) : (unit.hand?.length ?? 0)
              }})</span
            >
          </span>
          <button
            v-if="hasEgo"
            class="ego-toggle"
            :class="{ 'ego-toggle--active': egoMode }"
            @click.stop="egoMode = !egoMode"
          >
            EGO
          </button>
        </div>

        <Transition name="hand-expand">
          <div v-if="showHandCards" class="hand-row" @click.stop>
            <template v-if="egoMode">
              <HandCard
                v-for="(c, i) in unit.ego"
                :key="c.id.id + c.id.packageId"
                :card="c"
                :selected="
                  selectingTargetFor?.unitId === unit.id &&
                  selectingTargetFor?.cardIndex === i &&
                  selectingTargetFor?.isEgo === true
                "
                :dimmed="
                  (selectingSlot !== null &&
                    selectingSlot.unitId !== unit.id) ||
                  (selectingTargetFor !== null &&
                    selectingTargetFor.unitId === unit.id &&
                    !(
                      selectingTargetFor.cardIndex === i &&
                      selectingTargetFor.isEgo === true
                    ))
                "
                :color="myColor"
                :unusable="c.canUse === false"
                @click="onCardClick(unit.id, Number(i), true)"
                @detail="detailCard = c"
              />
            </template>
            <template v-else>
              <HandCard
                v-for="(c, i) in unit.hand"
                :key="c.id.id + c.id.packageId"
                :card="c"
                :selected="
                  selectingTargetFor?.unitId === unit.id &&
                  selectingTargetFor?.cardIndex === i &&
                  !selectingTargetFor?.isEgo
                "
                :dimmed="
                  (selectingSlot !== null &&
                    selectingSlot.unitId !== unit.id) ||
                  (selectingTargetFor !== null &&
                    selectingTargetFor.unitId === unit.id &&
                    !(
                      selectingTargetFor.cardIndex === i &&
                      !selectingTargetFor.isEgo
                    ))
                "
                :color="myColor"
                :unusable="c.canUse === false"
                @click="onCardClick(unit.id, Number(i))"
                @detail="detailCard = c"
              />
            </template>
          </div>
        </Transition>
      </div>
    </template>

    <!-- ── Card detail overlay ── -->
    <CardDetail
      v-if="detailCard"
      :card="detailCard"
      @close="detailCard = null"
    />

    <!-- ── Collapsed details ── -->
    <details v-if="hasDetails" class="collapse">
      <summary>{{ detailsLabel }}</summary>

      <!-- Passives -->
      <template v-if="unit.passives?.length">
        <div class="det-label">Passives</div>
        <div class="passive-list">
          <template v-for="p in unit.passives" :key="p.id.id + p.id.packageId">
            <details
              v-if="p.desc"
              class="passive-entry"
              :class="passiveClass(p)"
            >
              <summary class="passive-name">{{ p.name }}</summary>
              <p class="passive-desc">{{ p.desc }}</p>
            </details>
            <div v-else class="passive-entry" :class="passiveClass(p)">
              <span class="passive-name">{{ p.name }}</span>
            </div>
          </template>
        </div>
      </template>

      <!-- Abnormality pages -->
      <template v-if="unit.abnormalities?.length">
        <div class="det-label">Abnormalities</div>
        <div class="clist">
          <div v-for="ab in unit.abnormalities" :key="ab.id" class="centry">
            <span>{{ ab.name }}</span>
            <span class="centry-range">Lv{{ ab.emotionLevel }}</span>
          </div>
        </div>
      </template>

      <!-- Resistances -->
      <template v-if="unit.keyPage">
        <div class="det-label">Resistances</div>
        <UnitResistanceTable :resistances="unit.keyPage?.resistances" />
      </template>
    </details>
  </div>
</template>

<style scoped>
/* ── Card shell — ally accent on right ───────────────────────────────────── */
.unit-card {
  width: 100%;
  border-right: 2px solid var(--gold-dim);
  transition:
    border-color 0.15s,
    background 0.15s;
}
.unit-card--ally-select {
  border-right-color: var(--green-hi);
  background: #0a1a0a;
  cursor: pointer;
}
.unit-card--ally-select:hover {
  background: #0c1e0c;
}

/* ── Header — ally: meta left, name center, badge right (via row-reverse) ── */
.unit-header {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}
.unit-header div {
  display: flex;
  justify-content: space-between;
  gap: 0.4rem;
}
.unit-name {
  text-align: right;
}

/* ── Slot list (die on left, protrudes beyond card edge) ─────────────────── */
.slot-list {
  display: flex;
  flex-direction: column;
  gap: 0.07rem;
  margin-left: -1.8rem;
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
  display: flex;
  align-items: center;
  gap: 0.35rem;
}
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

/* ── Hand section ────────────────────────────────────────────────────────── */
.hand-section {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}
.hand-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.4rem;
  cursor: pointer;
  user-select: none;
}
.hand-header:hover .section-label {
  color: var(--text-1);
}
.section-label {
  display: flex;
  align-items: center;
  gap: 0.3em;
  font-family: var(--font-display);
  font-size: 0.58rem;
  text-transform: uppercase;
  letter-spacing: 0.12em;
  color: var(--text-2);
  transition: color 0.15s;
}
.hand-chevron {
  font-size: 0.55rem;
  color: var(--gold-dim);
}
.hand-count {
  color: var(--text-3);
  font-size: 0.55rem;
  letter-spacing: 0;
  text-transform: none;
}

/* ── Hand expand transition ──────────────────────────────────────────────── */
.hand-expand-enter-active {
  transition:
    opacity 0.18s ease,
    transform 0.18s ease;
}
.hand-expand-leave-active {
  transition:
    opacity 0.14s ease,
    transform 0.14s ease;
}
.hand-expand-enter-from,
.hand-expand-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}
.ego-toggle {
  font-family: var(--font-display);
  font-size: 0.52rem;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  padding: 0.1rem 0.4rem;
  background: transparent;
  border: 1px solid var(--crimson);
  color: var(--crimson-hi);
  cursor: pointer;
  transition:
    background 0.12s,
    color 0.12s;
}
.ego-toggle--active {
  background: var(--crimson);
  color: #fff;
}
.ego-toggle:hover:not(.ego-toggle--active) {
  background: var(--crimson-dim);
}

/* ── Hand card row ───────────────────────────────────────────────────────── */
.hand-row {
  display: flex;
  gap: 0.35rem;
  flex-wrap: wrap;
}

/* ── Detail sub-section labels ───────────────────────────────────────────── */
.det-label {
  font-family: var(--font-display);
  font-size: 0.53rem;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--text-3);
  margin-top: 0.35rem;
  padding-bottom: 0.1rem;
  border-bottom: 1px solid var(--border);
}
</style>
