<!--
  HandCard.vue

  Renders a single battle card as a small interactive tile.
  First tap selects the card (emits 'click'). Tapping again while already
  selected emits 'detail' to open the CardDetail sheet.

  Props:
    card     – card object from game state (name, cost, range, rarity, dice[], …)
    selected – true when this card is the active selection
    dimmed   – true when another card is selected (this one fades out)
    color    – ally color hex used for glow when selected

  Emits:
    click   – user tapped an unselected card
    detail  – user tapped an already-selected card (double-tap-to-detail)
-->
<script setup lang="ts">
const props = defineProps<{
  card: any;
  selected?: boolean;
  dimmed?: boolean;
  color?: string;
  unusable?: boolean;
}>();

const emit = defineEmits<{ click: []; detail: [] }>();

let pressTimer: ReturnType<typeof setTimeout> | null = null;
let longPressed = false;

function onPressStart() {
  longPressed = false;
  pressTimer = setTimeout(() => {
    longPressed = true;
    emit("detail");
  }, 500);
}

function onPressEnd() {
  if (pressTimer) {
    clearTimeout(pressTimer);
    pressTimer = null;
  }
}

onUnmounted(() => {
  if (pressTimer) {
    clearTimeout(pressTimer);
    pressTimer = null;
  }
});

function handleClick() {
  if (props.unusable) return;
  if (longPressed) {
    longPressed = false;
    return;
  }
  emit("click");
}
</script>

<template>
  <div
    class="hcard"
    :class="{
      'hcard--selected': selected,
      'hcard--dim': dimmed,
      'hcard--unusable': unusable,
    }"
    :style="{
      borderColor: selected && color ? color : cardBorderColor(card),
      '--glow': selected && color ? color + '44' : undefined,
    }"
    @click="handleClick"
    @mousedown="onPressStart"
    @mouseup="onPressEnd"
    @mouseleave="onPressEnd"
    @touchstart.passive="onPressStart"
    @touchend="onPressEnd"
    @touchmove="onPressEnd"
  >
    <div class="hcard-header">
      <span class="hcard-cost" :style="costStyle(card) ?? {}">{{
        card.cost
      }}</span>
      <span class="hcard-range">{{ card.range }}</span>
    </div>
    <span class="hcard-name"
      >{{ card.name
      }}<span
        v-if="card.abilityDesc"
        class="hcard-desc-plus"
        :title="card.abilityDesc"
        >+</span
      ></span
    >
    <div v-if="card.dice?.length" class="hcard-dice">
      <div v-for="(d, i) in card.dice" :key="i" class="hcard-die">
        <img
          v-if="diceIcon(d.type, d.detail)"
          :src="diceIcon(d.type, d.detail)!"
          class="hcard-die-img"
          :alt="`${d.type} ${d.detail}`"
        />
        <span v-else class="hcard-die-placeholder">·</span>
        <span class="hcard-die-range" :style="{ color: dieTypeColor(d.type) }">
          {{ d.min }}–{{ d.max }}
        </span>
        <span v-if="d.desc" class="hcard-desc-plus" :title="d.desc">+</span>
      </div>
    </div>
    <div v-if="card.bufs?.length" class="hcard-tokens">
      <span v-for="(b, i) in card.bufs" :key="i" class="hcard-token">
        <img
          :src="cardTokenIconUrl(b)"
          :alt="b.label"
          class="hcard-token-icon"
        />
        <span v-if="b.stack > 0" class="hcard-token-stack">×{{ b.stack }}</span>
      </span>
    </div>
  </div>
</template>

<style scoped>
.hcard {
  flex-shrink: 0;
  width: 3.8rem;
  min-height: 5.6rem;
  background: linear-gradient(
    160deg,
    var(--bg-card-2) 0%,
    var(--bg-card-3) 100%
  );
  border: 1px solid var(--border-mid);
  display: flex;
  flex-direction: column;
  align-items: stretch;
  padding: 0.2rem 0.15rem 0.15rem;
  gap: 0.15rem;
  cursor: pointer;
  position: relative;
  touch-action: manipulation;
  transition:
    transform 0.1s,
    border-color 0.12s,
    box-shadow 0.12s;
  user-select: none;
  -webkit-user-select: none;
}
.hcard:hover {
  transform: translateY(-3px);
}
.hcard--selected {
  transform: translateY(-5px);
  box-shadow: 0 4px 16px var(--glow, rgba(201, 162, 39, 0.25));
}
.hcard--dim {
  opacity: 0.28;
}
.hcard--unusable {
  opacity: 0.38;
  cursor: not-allowed;
  filter: grayscale(0.6);
}
.hcard--unusable:hover {
  transform: none;
  box-shadow: none;
}

.hcard-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.1rem;
}

.hcard-range {
  font-size: 0.44rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-3);
  font-family: var(--font-body);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.hcard-cost {
  width: 1.1rem;
  height: 1.1rem;
  background: var(--gold-dim);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.68rem;
  color: var(--gold-bright);
  font-family: var(--font-body);
  font-weight: bold;
  flex-shrink: 0;
}

.hcard-name {
  flex: 1;
  font-size: 0.58rem;
  color: var(--text-1);
  font-family: var(--font-body);
  text-align: center;
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  line-clamp: 3;
  -webkit-box-orient: vertical;
  line-height: 1.3;
  width: 100%;
}

.hcard-dice {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
  margin-top: auto;
}

.hcard-die {
  display: flex;
  align-items: center;
  gap: 0.15rem;
}

.hcard-die-img {
  width: 0.85rem;
  height: 0.85rem;
  object-fit: contain;
  flex-shrink: 0;
}

.hcard-die-placeholder {
  width: 0.85rem;
  text-align: center;
  font-size: 0.6rem;
  color: var(--text-3);
  flex-shrink: 0;
}

.hcard-die-range {
  font-family: var(--font-body);
  font-size: 0.5rem;
}

.hcard-desc-plus {
  font-size: 0.55rem;
  font-weight: 900;
  line-height: 1;
  color: var(--gold);
  opacity: 0.8;
  flex-shrink: 0;
  margin-left: auto;
  cursor: default;
}

.hcard-tokens {
  display: flex;
  flex-wrap: wrap;
  gap: 0.1rem;
  margin-top: 0.1rem;
}

.hcard-token {
  display: inline-flex;
  align-items: center;
  gap: 0.08rem;
  padding: 0.06rem 0.12rem 0.06rem 0.06rem;
  background: var(--bg-info);
  border: 1px solid var(--border-info);
  color: var(--text-info);
  font-family: var(--font-body);
}

.hcard-token-icon {
  width: 0.75rem;
  height: 0.75rem;
  object-fit: contain;
  flex-shrink: 0;
}

.hcard-token-stack {
  font-size: 0.4rem;
}
</style>
