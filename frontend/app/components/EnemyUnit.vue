<!--
  EnemyUnit.vue

  Displays a single enemy unit card. Speed dice protrude from the right edge
  toward the battlefield centre. Header shows turn state, name, light pips,
  and emotion level. Passives, abnormalities, and resistances are collapsed.

  Props:
    unit – enemy unit object from the game state snapshot

  Injects: BATTLE_CTX (provided by BattleView)
-->
<script setup lang="ts">
import type { BattleCtx } from "~/composables/useBattleContext";

const props = defineProps<{ unit: any }>();

const { attackMap, selectingTargetFor, onTargetDieClick } = inject(
  BATTLE_CTX,
) as BattleCtx;

const detailCard = ref<any>(null);
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

function handleSlotClick(d: any) {
  if (slotLongPressed) { slotLongPressed = false; return; }
  if (canBeTargeted.value && !d.staggered) onTargetDieClick(props.unit.id, d.slot);
}
function toggleBuff(type: string) {
  expandedBuff.value = expandedBuff.value === type ? null : type;
}

const isTargeting = computed(() => selectingTargetFor.value !== null);
const canBeTargeted = computed(
  () => isTargeting.value && props.unit.targetable,
);

const slotList = computed(() => sortedSlots(props.unit));

function dieColor(sc: any): string {
  if (sc.clash) return ARROW_COLORS.clash;
  return ARROW_COLORS.incoming; // enemy targeting ally (one-sided or clash already handled)
}

const hasDetails = computed(() => {
  const u = props.unit;
  return u.passives?.length || u.abnormalities?.length || u.keyPage;
});

const detailsLabel = computed(() => {
  const u = props.unit;
  const parts: string[] = [];
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
    :class="{ 'unit-card--dim': isTargeting && !canBeTargeted }"
  >
    <!-- ── Header ── -->
    <div class="unit-header">
      <!-- row 1: name, turn badge -->
      <div class="unit-header-1">
        <span class="unit-name">{{
          unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}`
        }}</span>
        <span
          class="state-badge"
          :style="{ background: turnColor(unit.turnState) }"
          >{{ turnLabel(unit.turnState) }}</span
        >
      </div>

      <!-- row 2: emotion level, light pips -->
      <div class="unit-header-2">
        <div class="unit-meta">
          <div v-if="unit.emotionCoins?.max" class="emotion-meta">
            <span class="em-level">Em{{ unit.emotionLevel }}</span>
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
          </div>
        </div>
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
      </div>
    </div>

    <!-- ── HP / SG bars ── -->
    <UnitStatus :unit="unit" />

    <!-- ── Speed dice + slotted cards ── -->
    <div v-if="slotList.length" class="slot-list">
      <div
        v-for="{ die: d, card: sc } in slotList"
        :key="d.slot"
        class="slot-row"
        :class="{
          'slot-filled': sc !== null,
          'slot-target': canBeTargeted && !d.staggered,
        }"
        @click.stop="handleSlotClick(d)"
        @mousedown="onSlotPressStart(sc)"
        @mouseup="onSlotPressEnd"
        @mouseleave="onSlotPressEnd"
        @touchstart.passive="onSlotPressStart(sc)"
        @touchend="onSlotPressEnd"
        @touchmove="onSlotPressEnd"
      >
        <!-- Content: card info + incoming chips (left) -->
        <div class="slot-content">
          <div v-if="sc !== null" class="slot-card-row">
            <SlottedCard
              :sc="sc"
              :target-label="
                sc.targetUnitId != null
                  ? `${sc.clash ? '⚔' : '↗'} #${sc.targetUnitId}·${sc.targetSlot}`
                  : undefined
              "
              :clash="sc.clash"
            />
          </div>
          <div v-if="attackMap[unit.id]?.[d.slot]?.length" class="incoming-row">
            <span
              v-for="atk in attackMap[unit.id]?.[d.slot]"
              :key="atk.name"
              class="incoming-chip"
              :class="{ 'chip-mass': isMassRange(atk.range) }"
              :style="{ borderColor: atk.color, color: atk.color }"
              >↑ {{ atk.name }}{{ isMassRange(atk.range) ? " ✦" : "" }}</span
            >
          </div>
          <span
            v-if="sc === null && !attackMap[unit.id]?.[d.slot]?.length"
            class="slot-empty"
            >—</span
          >
        </div>

        <!-- Die (protrudes beyond right card edge) -->
        <span
          class="hex-wrap"
          :class="{
            staggered: d.staggered,
            'hex-target': canBeTargeted && !d.staggered,
          }"
          :data-die="`${unit.id}-${d.slot}`"
          :title="`Slot ${d.slot}`"
          :style="
            sc !== null && !d.staggered ? { background: dieColor(sc) } : {}
          "
        >
          <span class="hex-inner">{{ d.staggered ? "✕" : d.value }}</span>
        </span>
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

    <!-- ── Card detail overlay ── -->
    <CardDetail
      v-if="detailCard"
      :card="detailCard"
      @close="detailCard = null"
    />

    <!-- ── Collapsed details ── -->
    <details v-if="hasDetails" class="collapse">
      <summary>{{ detailsLabel }}</summary>

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

      <template v-if="unit.abnormalities?.length">
        <div class="det-label">Abnormalities</div>
        <div class="clist">
          <div v-for="ab in unit.abnormalities" :key="ab.id" class="centry">
            <span>{{ ab.name }}</span>
            <span class="centry-range">Lv{{ ab.emotionLevel }}</span>
          </div>
        </div>
      </template>

      <template v-if="unit.keyPage">
        <div class="det-label">Resistances</div>
        <ResistanceTable :resistances="unit.keyPage?.resistances" />
      </template>
    </details>
  </div>
</template>

<style scoped>
/* ── Targetable die highlight ────────────────────────────────────────────── */
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

/* ── Card shell — enemy accent on left ───────────────────────────────────── */
.unit-card {
  width: 100%;
  border-left: 2px solid var(--crimson);
  transition: opacity 0.15s;
}
.unit-card--dim {
  opacity: 0.35;
  pointer-events: none;
}

/* ── Header — enemy: badge left, name center, meta right ─────────────────── */
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

/* ── Slot list (die on right, protrudes beyond card edge) ────────────────── */
.slot-list {
  display: flex;
  flex-direction: column;
  gap: 0.07rem;
  margin-right: -1.8rem;
}
.slot-row {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.06rem 0 0.06rem 0.15rem;
}
.slot-content {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0.06rem;
}
.slot-card-row {
  display: flex;
  gap: 0.3rem;
  align-items: center;
  min-width: 0;
}

/* ── Incoming attack chips ───────────────────────────────────────────────── */
.incoming-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.2rem;
}
.incoming-chip {
  font-size: 0.6rem;
  padding: 0.08rem 0.28rem;
  background: transparent;
  border: 1px solid;
  white-space: nowrap;
  font-family: var(--font-body);
}
.chip-mass {
  font-weight: bold;
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
