<!-- 
  Displays a single unit card. Header shows turn state, name, light pips,
  and emotion level. Passives, abnormalities, and resistances are collapsed
  into a single details section.

  Props:
    unit – unit object from the game state snapshot
    canControl - whether the unit can be commanded (functionally, whether the unit is an ally)
    side - "left" or "right"
-->
<script setup lang="ts">
import type { BattleCtx } from "~/composables/useBattleContext";

const props = defineProps<{
  unit: any;
  isAlly?: boolean;
  side?: "right" | "left";
}>();

const {
  isSelectPhase,
  selectingSlot,
  selectingTargetFor,
  selectingAllyTargetFor,
  onCardClick,
  onAllyTargetClick,
  onTargetDieClick,
  allyColors,
  attackMap,
} = inject(BATTLE_CTX) as BattleCtx;

const slots = computed(() => sortedSlots(props.unit));

const isAllyTargeting = computed(
  () =>
    props.isAlly &&
    selectingAllyTargetFor.value !== null &&
    selectingAllyTargetFor.value.unitId !== props.unit.id,
);
const borderStyle = computed(() => {
  const color = props.isAlly
    ? isAllyTargeting.value
      ? "var(--green-hi)" // turn green if ally is selecting target
      : (allyColors.value[props.unit.id] ?? "var(--gold-dim)")
    : "var(--crimson)";

  return {
    [`border-${props.side}`]: "2px solid " + color,
  };
});

function dieColor(sc: any): string {
  if (sc?.clash) return ARROW_COLORS.clash;
  if (props.isAlly) return ARROW_COLORS.incoming;
  if (sc?.targetUnitId != null) return ARROW_COLORS.outgoing;
  return "#F0F"; // Instance / untargeted
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
  if (u.passives?.length) parts.push(`Passives (${u.passives.length})`);
  if (u.abnormalities?.length)
    parts.push(`Abnormalities (${u.abnormalities.length})`);
  if (u.keyPage) parts.push("Resistances");
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
    :class="{
      'unit-card--ally-select': isAllyTargeting,
      'reversed-order': side !== 'right',
    }"
    :style="borderStyle"
    @click.capture="isAllyTargeting ? onAllyTargetClick(unit.id) : undefined"
  >
    <!-- ── Header ── -->
    <div class="unit-header">
      <!-- row 1: turn badge, name -->
      <div class="unit-header-row reversible-container">
        <span
          class="state-badge"
          :style="{
            background: isDead(unit) ? '#e53935' : turnColor(unit.turnState),
          }"
          >{{ isDead(unit) ? "DEAD" : turnLabel(unit.turnState) }}</span
        >
        <span class="unit-name reversible-text">{{
          unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}`
        }}</span>
      </div>

      <!-- row 2: light pips, emotion level -->
      <div class="unit-header-row reversible-container">
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
    <div v-for="{ die, card } in slots" :key="die.slot" class="slot-list">
      <UnitDieRow
        class="reversible-container"
        :unit="unit"
        :die="die"
        :card="card"
        :color="dieColor(card)"
        :isReversed="side !== 'right'"
        :isAlly="isAlly"
        :onLongPress="() => (detailCard = card)"
      >
        <div
          v-if="!isDead(unit) && attackMap[unit.id]?.[die.slot]?.length"
          class="incoming-row"
        >
          <span
            v-for="atk in attackMap[unit.id]?.[die.slot]"
            :key="atk.name"
            class="incoming-chip"
            :class="{ 'chip-mass': isMassRange(atk.range) }"
            :style="{ borderColor: atk.color, color: atk.color }"
            >↑ {{ atk.name }}{{ isMassRange(atk.range) ? " ✦" : "" }}</span
          >
        </div>
      </UnitDieRow>
    </div>

    <!-- ── Status effects ── -->
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
            <div class="buff-expanded-name">{{ b.name ?? b.type }}</div>
            <div v-if="b.desc">{{ b.desc }}</div>
          </div>
        </div>
      </span>
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
      <summary class="section-label">{{ detailsLabel }}</summary>

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
/* ── Card shell (accents controlled by borderColor) ── */
.unit-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  padding: 0.6rem;
  display: flex;
  flex-direction: column;
  flex: 1;
  gap: 0.32rem;
  font-size: 0.78rem;
  font-family: var(--font-body);
  overflow: visible;
  width: 100%;
  max-width: 24rem;
  min-width: 0;
  transition:
    border-color 0.15s,
    background 0.15s;
}
.unit-card--ally-select {
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
.unit-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.4rem;
}
.state-badge {
  font-family: var(--font-body);
  font-size: 0.52rem;
  padding: 0.1rem 0.3rem;
  color: #000;
  white-space: nowrap;
  font-weight: bold;
  flex-shrink: 0;
}
.unit-name {
  font-family: var(--font-display);
  font-size: 0.7rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  color: var(--text-1);
  flex: 1;
  flex-shrink: 0;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  text-align: right;
}

/* ── Slot list (die on left, protrudes beyond card edge) ── */
.slot-list {
  display: flex;
  flex-direction: column;
  gap: 0.07rem;
  margin-left: -1.8rem;
  margin-right: 0;
}
.reversed-order .slot-list {
  margin-left: 0;
  margin-right: -1.8rem;
}
.slot-card-entry {
  display: flex;
  flex-direction: row;
  gap: 0.1rem;
}

/* ── Incoming attack chips ── */
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

/* ── Buffs ── */
.buffs {
  display: flex;
  flex-wrap: wrap;
  gap: 0.2rem;
}
.buff-tag {
  display: inline-flex;
  align-items: center;
  gap: 0.18rem;
  font-size: 0.6rem;
  padding: 0.08rem 0.25rem 0.08rem 0.15rem;
  background: #1c1000;
  border: 1px solid #4a2800;
  color: #ff9800;
  font-family: var(--font-body);
  cursor: pointer;
  user-select: none;
}
.buff-tag--positive {
  background: #0a1a0a;
  border-color: #2e5c2e;
  color: #81c784;
}
.buff-tag--negative {
  background: #1a0808;
  border-color: #5c1a1a;
  color: #ef9a9a;
}
.buff-icon {
  width: 0.9rem;
  height: 0.9rem;
  object-fit: contain;
  flex-shrink: 0;
}
.buff-expanded {
  display: flex;
  align-items: flex-start;
  gap: 0.4rem;
  margin-top: 0.2rem;
  padding: 0.3rem 0.45rem;
  background: var(--bg-surface);
  border: 1px solid var(--border-mid);
  font-size: 0.65rem;
  color: var(--text-2);
  line-height: 1.4;
  width: max-content;
  max-width: 16rem;
  position: absolute;
  z-index: 10;
}
.buff-expanded-icon {
  width: 2rem;
  height: 2rem;
  object-fit: contain;
  flex-shrink: 0;
}
.buff-expanded-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.buff-expanded-name {
  color: var(--text-1);
  font-weight: 600;
  margin-bottom: 0.1rem;
}

/* ── Generic card list (hand, EGO, passives, etc.) ────────────────────────── */
.clist {
  display: flex;
  flex-direction: column;
  gap: 0.08rem;
  margin-top: 0.2rem;
}
.centry {
  display: flex;
  gap: 0.3rem;
  align-items: baseline;
  font-size: 0.7rem;
  color: var(--text-2);
}
.centry.unavailable {
  color: var(--text-3);
}
.centry-cost {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1.1rem;
  height: 1.1rem;
  background: var(--bg-card-3);
  border: 1px solid var(--border-mid);
  font-size: 0.58rem;
  color: var(--gold);
  flex-shrink: 0;
  font-family: var(--font-body);
}
.centry-range {
  color: var(--text-3);
  margin-left: auto;
  font-size: 0.6rem;
  font-family: var(--font-body);
}

/* ── Hand section ── */
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

/* ── Hand expand transition ── */
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

/* ── Hand card row ── */
.hand-row {
  display: flex;
  gap: 0.35rem;
  flex-wrap: wrap;
}

/* ── Passive list ────────────────────────────────────────────────────────────── */
.passive-list {
  display: flex;
  flex-direction: column;
  margin-top: 0.2rem;
}
.passive-entry {
  border-left: 2px solid var(--border-mid);
  padding-left: 0.4rem;
  padding-top: 0.15rem;
  padding-bottom: 0.15rem;
}
.passive-entry + .passive-entry {
  border-top: 1px solid var(--border);
}
.passive-entry.rarity-uncommon {
  border-left-color: #56a348;
}
.passive-entry.rarity-rare {
  border-left-color: #4169c4;
}
.passive-entry.rarity-unique {
  border-left-color: var(--gold);
}
.passive-entry.rarity-special {
  border-left-color: var(--crimson-hi);
}
.passive-entry.unavailable {
  opacity: 0.42;
}

.passive-name {
  font-size: 0.7rem;
  color: var(--text-1);
  list-style: none;
  display: flex;
  align-items: baseline;
  gap: 0.3rem;
  cursor: default;
  user-select: none;
  line-height: 1.4;
}
.passive-name::marker,
.passive-name::-webkit-details-marker {
  display: none;
}
details.passive-entry > .passive-name {
  cursor: pointer;
}
details.passive-entry > .passive-name::before {
  content: "▸ ";
  font-size: 0.5rem;
  color: var(--text-3);
  flex-shrink: 0;
}
details[open].passive-entry > .passive-name::before {
  content: "▾ ";
}
.passive-negative > .passive-name {
  color: var(--crimson-hi);
}
.passive-desc {
  font-size: 0.68rem;
  color: var(--text-2);
  line-height: 1.45;
  margin: 0.2rem 0 0.05rem;
}

/* ── Detail sub-section labels ── */
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
