<!--
  AllyUnit.vue

  Displays a single ally unit card. Header shows turn state, name, light pips,
  and emotion level. During SelectCard phase the hand becomes interactive.
  Everything else (hand read-only, EGO, team pages, passives, abnormalities,
  resistances) is collapsed into a single details section.

  Props:
    unit – ally unit object from the game state snapshot

  Injects: BATTLE_CTX (provided by BattleView)
-->
<script setup lang="ts">
import type { BattleCtx } from "~/composables/useBattleContext";

const props = defineProps<{ unit: any }>();

const {
  isSelectPhase,
  selectingSlotFor,
  selectingTargetFor,
  selectingAllyTargetFor,
  onCardClick,
  onSlotClick,
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
  if (slotPressTimer) { clearTimeout(slotPressTimer); slotPressTimer = null; }
}

function handleSlotClick(d: any, sc: any) {
  if (slotLongPressed) { slotLongPressed = false; return; }
  if (selectingTargetFor.value?.unitId === props.unit.id && selectingTargetFor.value?.diceSlot === d.slot) {
    cancelTargeting();
  } else if (isSelectPhase.value && selectingSlotFor.value?.unitId === props.unit.id && sc === null && !d.staggered) {
    onSlotClick(props.unit, selectingSlotFor.value!.cardIndex, d.slot);
  }
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
  <div
    class="unit-card"
    :class="{ 'unit-card--ally-select': isAllyTargeting }"
    @click.capture="isAllyTargeting ? onAllyTargetClick(unit.id) : undefined"
  >
    <!-- ── Header ── -->
    <div class="unit-header">
      <!-- row 1: turn badge, name -->
      <div class="unit-header-1">
        <span
          class="state-badge"
          :style="{ background: turnColor(unit.turnState) }"
          >{{ turnLabel(unit.turnState) }}</span
        >
        <span class="unit-name">{{
          unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}`
        }}</span>
      </div>

      <!-- row 2: light pips, emotion level -->
      <div class="unit-header-2">
        <div
          v-if="unit.maxPlayPoint"
          class="ap-pips"
          :title="`Light: ${unit.playPoint}/${unit.maxPlayPoint} (${unit.reservedPlayPoint ?? 0} reserved)`"
        >
          <span
            v-for="n in Math.max(
              0,
              unit.playPoint - (unit.reservedPlayPoint ?? 0),
            )"
            :key="'f' + n"
            class="ap-pip ap-pip--lit"
          />
          <span
            v-for="n in unit.reservedPlayPoint ?? 0"
            :key="'r' + n"
            class="ap-pip ap-pip--reserved"
          />
          <span
            v-for="n in Math.max(0, unit.maxPlayPoint - unit.playPoint)"
            :key="'u' + n"
            class="ap-pip"
          />
        </div>
        <div class="unit-meta">
          <div v-if="unit.emotionCoins?.max" class="emotion-meta">
            <div class="epips">
              <span
                v-for="n in unit.emotionCoins.positive"
                :key="'p' + n"
                class="epip epip--pos"
              />
              <span
                v-for="n in unit.emotionCoins.negative"
                :key="'n' + n"
                class="epip epip--neg"
              />
              <span
                v-for="n in Math.max(
                  0,
                  unit.emotionCoins.max -
                    unit.emotionCoins.positive -
                    unit.emotionCoins.negative,
                )"
                :key="'e' + n"
                class="epip epip--empty"
              />
            </div>
            <span class="em-level">Em{{ unit.emotionLevel }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- ── HP / SP bars ── -->
    <UnitStatus :unit="unit" />

    <!-- ── Speed dice + slotted cards ── -->
    <div v-if="slots.length" class="slot-list">
      <div
        v-for="{ die: d, card: sc } in slots"
        :key="d.slot"
        class="slot-row"
        :class="{
          'slot-filled': sc !== null,
          'slot-open':
            isSelectPhase &&
            selectingSlotFor?.unitId === unit.id &&
            sc === null &&
            !d.staggered,
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
            staggered: d.staggered,
            'hex-open':
              isSelectPhase &&
              selectingSlotFor?.unitId === unit.id &&
              sc === null &&
              !d.staggered,
            'hex-pending':
              isSelectPhase &&
              selectingTargetFor?.unitId === unit.id &&
              selectingTargetFor?.diceSlot === d.slot,
          }"
          :data-die="`${unit.id}-${d.slot}`"
          :style="
            sc !== null && !d.staggered ? { background: dieColor(sc) } : {}
          "
        >
          <span class="hex-inner">{{ d.staggered ? "✕" : d.value }}</span>
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

    <!-- ── Buffs ── -->
    <div v-if="unit.buffs?.length" class="buffs">
      <span
        v-for="b in unit.buffs"
        :key="b.type"
        class="buff-tag"
        :class="buffClass(b)"
        style="position: relative"
        @click.stop="toggleBuff(b.type)"
      >
        <img :src="buffIconUrl(b)" :alt="b.type" class="buff-icon" />
        <span v-if="b.stacks > 1">×{{ b.stacks }}</span>
        <div v-if="expandedBuff === b.type" class="buff-expanded">
          <img :src="buffIconUrl(b)" :alt="b.type" class="buff-expanded-icon" />
          <div class="buff-expanded-text">
            <div class="buff-expanded-name">{{ b.type }}</div>
            <div v-if="b.desc">{{ b.desc }}</div>
          </div>
        </div>
      </span>
    </div>

    <!-- ── Interactive hand (select phase only) ── -->
    <template v-if="isSelectPhase && (unit.hand?.length || hasEgo)">
      <div class="hand-section">
        <div class="hand-header">
          <span class="section-label">{{ egoMode ? "EGO" : "Hand" }}</span>
          <button
            v-if="hasEgo"
            class="ego-toggle"
            :class="{ 'ego-toggle--active': egoMode }"
            @click.stop="egoMode = !egoMode"
          >
            EGO
          </button>
        </div>

        <div class="hand-row">
          <template v-if="egoMode">
            <HandCard
              v-for="(c, i) in unit.ego"
              :key="c.id.id + c.id.packageId"
              :card="c"
              :selected="
                selectingSlotFor?.unitId === unit.id &&
                selectingSlotFor?.cardIndex === i &&
                selectingSlotFor?.isEgo === true
              "
              :dimmed="
                selectingSlotFor !== null &&
                !(
                  selectingSlotFor.unitId === unit.id &&
                  selectingSlotFor.cardIndex === i &&
                  selectingSlotFor.isEgo === true
                )
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
                selectingSlotFor?.unitId === unit.id &&
                selectingSlotFor?.cardIndex === i &&
                !selectingSlotFor?.isEgo
              "
              :dimmed="
                selectingSlotFor !== null &&
                !(
                  selectingSlotFor.unitId === unit.id &&
                  selectingSlotFor.cardIndex === i &&
                  !selectingSlotFor.isEgo
                )
              "
              :color="myColor"
              :unusable="c.canUse === false"
              @click="onCardClick(unit.id, Number(i))"
              @detail="detailCard = c"
            />
          </template>
        </div>
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

      <!-- Hand (read-only, outside select phase) -->
      <template v-if="!isSelectPhase && unit.hand?.length">
        <div class="det-label">Hand</div>
        <div class="clist">
          <div
            v-for="c in unit.hand"
            :key="c.id.id + c.id.packageId"
            class="centry"
          >
            <span class="centry-cost">{{ c.cost }}</span>
            <span>{{ c.name }}</span>
            <span class="centry-range">{{ c.range }}</span>
          </div>
        </div>
      </template>

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

      <!-- Abnormalities -->
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
        <ResistanceTable :resistances="unit.keyPage?.resistances" />
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
}
.section-label {
  font-family: var(--font-display);
  font-size: 0.58rem;
  text-transform: uppercase;
  letter-spacing: 0.12em;
  color: var(--text-2);
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
