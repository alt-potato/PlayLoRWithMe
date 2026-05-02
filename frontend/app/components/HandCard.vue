<!--
  HandCard.vue

  Renders a single battle card as a small interactive tile with two panes:

    - Preview pane (always visible): cost, range glyph, name, dice icons
      (without min-max numbers), token list. Fixed 5:7 footprint so the
      hand-row stays uniformly dense regardless of card complexity.
    - Detail pane: per-die rows with type icon, min-max range coloured by
      die type, and the die's effect description (wraps; pane scrolls
      vertically on overflow). Also surfaces the card-level ability
      description at the top.

  When the detail pane appears depends on `displayMode`:
    - "compact" (default, used in deck-building / key-page browsing):
      preview only at rest. On a hover-capable device, hovering the card
      reveals the detail pane (matches the in-game "mouseover for full
      view" behaviour). Touch-only devices never trigger this; users tap
      the card body to select, or long-press to open CardDetail for full
      info.
    - "full" (used in battle): both panes always rendered together —
      players see everything they need to plan their turn at a glance,
      matching the in-game battle card layout.

  Props:
    card        – card object from game state (name, cost, range, rarity, dice[], …)
    selected    – true when this card is the active selection (gold glow)
    dimmed      – true when another card is selected (this one fades out)
    color       – ally color hex used for glow when selected
    displayMode – "compact" (default) or "full"

  Emits:
    click   – user tapped an unselected card
    detail  – user long-pressed the card (opens CardDetail sheet)
-->
<script setup lang="ts">
import type { Card } from "~/types/game";

const props = withDefaults(
  defineProps<{
    card: Card;
    selected?: boolean;
    dimmed?: boolean;
    color?: string;
    unusable?: boolean;
    /** When true, suppresses click interaction (no slot selection) while keeping long-press for detail. */
    readonly?: boolean;
    /** Copy count shown as a ×N badge; omit or set to 1 to hide. */
    count?: number;
    /** "compact" shows preview at rest, detail pane on hover. "full" always shows both. */
    displayMode?: "compact" | "full";
  }>(),
  { displayMode: "compact" },
);

const emit = defineEmits<{ click: []; detail: [] }>();

// overlay placement: by default the compact-mode detail pane appears to
// the right of the card (`left: 100%`). When the card is close enough to
// the right edge of its nearest scroll-clipping ancestor (or the viewport)
// that the overlay would overflow, we flip it to the left side. Measured
// on mouseenter so the decision reflects current scroll / layout state
// without any reflow. Using the scroll-clipping ancestor (rather than
// just the viewport) keeps the overlay out of adjacent panels — in
// DeckTab, the deck grid clips at its right edge and the floating
// overlay would otherwise be clipped behind the equipped-deck panel.
// Roughly 9rem × 16 = 144px; 150 leaves a small margin to flip earlier
// rather than overlap the clip boundary at the exact pixel.
const OVERLAY_WIDTH_PX = 150;
const cardEl = ref<HTMLElement>();
const flipLeft = ref(false);

function overlayRightBound(el: HTMLElement): number {
  let cur: HTMLElement | null = el.parentElement;
  while (cur) {
    const s = getComputedStyle(cur);
    if (s.overflowX !== "visible") return cur.getBoundingClientRect().right;
    cur = cur.parentElement;
  }
  return window.innerWidth;
}

function onOverlayHoverStart() {
  if (props.displayMode !== "compact") return;
  if (!window.matchMedia("(hover: hover)").matches) return;
  const el = cardEl.value;
  if (!el) return;
  const rect = el.getBoundingClientRect();
  flipLeft.value = rect.right + OVERLAY_WIDTH_PX > overlayRightBound(el);
}

let pressTimer: ReturnType<typeof setTimeout> | null = null;
let longPressed = false;

function onPressStart() {
  longPressed = false;
  pressTimer = setTimeout(() => {
    longPressed = true;
    emit("detail");
  }, LONG_PRESS_MS);
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
  if (props.unusable || props.readonly) return;
  if (longPressed) {
    longPressed = false;
    return;
  }
  emit("click");
}
</script>

<template>
  <div
    ref="cardEl"
    class="hcard"
    :class="[
      `hcard--mode-${displayMode}`,
      {
        'hcard--selected': selected,
        'hcard--dim': dimmed,
        'hcard--unusable': unusable,
        'hcard--readonly': readonly,
        'hcard--flip-left': flipLeft,
      },
    ]"
    :style="{
      borderColor: selected && color ? color : cardBorderColor(card),
      '--glow': selected && color ? color + '44' : undefined,
    }"
    @click="handleClick"
    @mouseenter="onOverlayHoverStart"
    @mousedown="onPressStart"
    @mouseup="onPressEnd"
    @mouseleave="onPressEnd"
    @touchstart.passive="onPressStart"
    @touchend.passive="onPressEnd"
    @touchmove.passive="onPressEnd"
  >
    <!-- preview pane: always rendered -->
    <div class="hcard-preview">
      <div class="hcard-header">
        <span class="hcard-cost" :style="costStyle(card) ?? {}">{{
          card.cost
        }}</span>
        <CardRangeIcon :range="card.range" class="hcard-range" />
      </div>
      <span class="hcard-name">{{ card.name }}</span>
      <div v-if="card.dice?.length" class="hcard-dice-icons">
        <template v-for="(d, i) in card.dice" :key="i">
          <img
            v-if="diceIcon(d.type, d.detail)"
            :src="diceIcon(d.type, d.detail)!"
            class="hcard-die-img"
            :alt="`${d.type} ${d.detail}`"
          />
          <span v-else class="hcard-die-placeholder">·</span>
        </template>
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
      <span v-if="count && count > 1" class="hcard-count">×{{ count }}</span>
    </div>

    <!--
      detail pane: always in the DOM. CSS controls visibility:
      hidden in compact mode at rest; revealed on hover via @media (hover: hover);
      always visible in full mode. Compact-mode reveal is positioned absolutely
      so revealing the pane never reflows neighbouring cards in the row.
    -->
    <div class="hcard-detail" @click.stop>
      <p v-if="card.abilityDesc" class="hcard-detail-ability">
        <KeywordText :text="card.abilityDesc" />
      </p>
      <div v-if="card.dice?.length" class="hcard-detail-dice">
        <div
          v-for="(d, i) in card.dice"
          :key="i"
          class="hcard-detail-die"
        >
          <!-- left column: dice icon + range numbers, vertically centered -->
          <div class="hcard-detail-die-info">
            <img
              v-if="diceIcon(d.type, d.detail)"
              :src="diceIcon(d.type, d.detail)!"
              class="hcard-die-img"
              :alt="`${d.type} ${d.detail}`"
            />
            <span v-else class="hcard-die-placeholder">·</span>
            <span
              class="hcard-die-range"
              :style="{ color: dieTypeColor(d.type) }"
            >
              {{ d.min }}–{{ d.max }}
            </span>
          </div>
          <!-- right column: effect text, vertically centered with the icon+range -->
          <p
            v-if="d.desc"
            class="hcard-die-desc"
            :class="`hcard-die-desc--${d.type.toLowerCase()}`"
          >
            <KeywordText :text="d.desc" />
          </p>
          <span v-else aria-hidden="true"></span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.hcard {
  flex-shrink: 0;
  display: flex;
  align-items: stretch;
  border: 1px solid var(--border-mid);
  cursor: pointer;
  position: relative;
  touch-action: manipulation;
  transition:
    border-color 0.12s,
    box-shadow 0.12s;
  user-select: none;
  -webkit-user-select: none;
}
/* selection highlight is a glow ring; no translate so the card doesn't
   intrude into the sticky filter chrome above the deck list. */
.hcard--selected {
  box-shadow: 0 0 0 2px var(--glow, rgba(201, 162, 39, 0.25));
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
  box-shadow: none;
}
.hcard--readonly {
  cursor: default;
}

/* ── Preview pane ──
   Baseline width bumped from 4rem → 5.5rem (5:7 ratio preserved) so
   text and glyphs read at ~14-18px instead of ~10-13px. At the prior
   width every element on the card was visibly smaller than the rest
   of the UI chrome, defeating quick scanning during deck-building. */
.hcard-preview {
  flex-shrink: 0;
  width: 5.5rem;
  aspect-ratio: 5 / 7;
  overflow: hidden;
  background: linear-gradient(
    160deg,
    var(--bg-card-2) 0%,
    var(--bg-card-3) 100%
  );
  display: flex;
  flex-direction: column;
  align-items: stretch;
  padding: 0.25rem 0.2rem 0.2rem;
  gap: 0.2rem;
}

.hcard-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.15rem;
}

.hcard-range {
  font-size: 1rem;
}

.hcard-cost {
  width: 1.5rem;
  height: 1.5rem;
  background: var(--gold-dim);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.95rem;
  color: var(--gold-bright);
  font-family: var(--font-body);
  font-weight: bold;
  flex-shrink: 0;
}

.hcard-name {
  flex: 1;
  /* 0.7rem fits the longest common card names (e.g. "Unforgettable") on
     a single line within the 5.5rem preview; `overflow-wrap: break-word`
     is a safety net for any word that still overruns. */
  font-size: 0.7rem;
  color: var(--text-1);
  font-family: var(--font-body);
  text-align: center;
  overflow: hidden;
  overflow-wrap: break-word;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  line-clamp: 3;
  -webkit-box-orient: vertical;
  line-height: 1.3;
  width: 100%;
}

/* preview's icon-only dice strip — centered, tightly packed; wraps so 4
   dice still fit cleanly within the 5.5rem preview width. tight gap
   so they read as a grouped strip rather than individual scattered icons. */
.hcard-dice-icons {
  display: flex;
  flex-wrap: wrap;
  gap: 0.04rem 0.02rem;
  margin-top: auto;
  align-items: center;
  justify-content: center;
}

.hcard-die-img {
  width: 1.15rem;
  height: 1.15rem;
  object-fit: contain;
  flex-shrink: 0;
}

.hcard-die-placeholder {
  width: 1.15rem;
  text-align: center;
  font-size: 0.8rem;
  color: var(--text-3);
  flex-shrink: 0;
}

/* ── Detail pane ──
   Width bumped from 6.5rem → 9rem to accommodate proportionally larger
   body text (desc lines, ability desc) that stays readable at the new
   preview's scale. */
.hcard-detail {
  flex-shrink: 0;
  width: 9rem;
  background: linear-gradient(
    160deg,
    var(--bg-card-3) 0%,
    var(--bg-card-2) 100%
  );
  border-left: 1px solid var(--border-mid);
  display: flex;
  flex-direction: column;
  padding: 0.2rem 0.25rem 0.15rem;
  gap: 0.2rem;
  overflow-y: auto;
  /* slim scrollbar to suit the small surface */
  scrollbar-width: thin;
  scrollbar-color: var(--border-mid) transparent;
}
.hcard-detail::-webkit-scrollbar {
  width: 0.25rem;
}
.hcard-detail::-webkit-scrollbar-track {
  background: transparent;
}
.hcard-detail::-webkit-scrollbar-thumb {
  background: var(--border-mid);
  border-radius: 0.1rem;
}

.hcard-detail-ability {
  margin: 0;
  font-family: var(--font-body);
  font-size: 0.68rem;
  /* page-level text — near-white per in-game styling */
  color: var(--text-page);
  line-height: 1.1;
  word-wrap: break-word;
  overflow-wrap: break-word;
}

/* dice container stacks rows from the top; rows take their natural
   height so the panel content is always top-aligned. base game uses
   single spacing (line-height: 1) within each row. */
.hcard-detail-dice {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.hcard-detail-die {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.3rem;
  /* per-row content (icon+range on the left, desc on the right) is
     vertically centered with each other within the natural row height. */
  align-items: center;
}

/* icon and range numbers sit side-by-side so all dice icons land on the
   same left edge regardless of which row they're in. */
.hcard-detail-die-info {
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 0.2rem;
}

.hcard-die-range {
  font-family: var(--font-body);
  font-size: 0.75rem;
  font-weight: 600;
  line-height: 1;
}

.hcard-die-desc {
  margin: 0;
  font-family: var(--font-body);
  font-size: 0.62rem;
  line-height: 1.05;
  word-wrap: break-word;
  overflow-wrap: break-word;
}

/* per-die desc colour tints, mirroring in-game card text styling.
   atk = light red, def = light blue, standby (counter) = light gold. */
.hcard-die-desc--atk {
  color: var(--text-atk);
}
.hcard-die-desc--def {
  color: var(--text-def);
}
.hcard-die-desc--standby {
  color: var(--text-standby);
}

/* ── Detail pane visibility per displayMode ──
   Compact mode: hidden at rest. On hover-capable devices, revealed on
   hover via @media (hover: hover); positioned absolutely so the reveal
   never reflows neighbouring cards. The overlay accepts that it may be
   covered by sticky filter chrome with higher z-index — that trade-off
   keeps the implementation simple.

   Border-color is inherited so the overlay picks up whichever dynamic
   border the card itself uses (ally color / unusable / default) —
   keeping the floating pane visually continuous with the card beside it. */
.hcard--mode-compact .hcard-detail {
  display: none;
  position: absolute;
  top: 0;
  left: 100%;
  height: 100%;
  border: 1px solid;
  border-color: inherit;
}

/* when the card is close to the viewport's right edge the overlay flips
   to the left side of the card instead. an @mouseenter handler sets
   `hcard--flip-left` based on getBoundingClientRect. */
.hcard--mode-compact.hcard--flip-left .hcard-detail {
  left: auto;
  right: 100%;
}

@media (hover: hover) {
  .hcard--mode-compact:hover .hcard-detail {
    display: flex;
  }
  /* lift the hovered card above subsequent siblings so the overlay
     paints on top of the next cards in the row (they also have
     position: relative and would otherwise paint in dom order). */
  .hcard--mode-compact:hover {
    z-index: 20;
  }
}

/* Full mode (battle): detail pane is always visible inline. The base
   .hcard-detail rule already provides the correct flex behaviour. */

/* ── Tokens ── */
.hcard-tokens {
  display: flex;
  flex-wrap: wrap;
  gap: 0.15rem;
  margin-top: 0.15rem;
}

.hcard-token {
  display: inline-flex;
  align-items: center;
  gap: 0.1rem;
  padding: 0.08rem 0.15rem 0.08rem 0.08rem;
  background: var(--bg-info);
  border: 1px solid var(--border-info);
  color: var(--text-info);
  font-family: var(--font-body);
}

.hcard-token-icon {
  width: 1rem;
  height: 1rem;
  object-fit: contain;
  flex-shrink: 0;
}

.hcard-token-stack {
  font-size: 0.55rem;
}

.hcard-count {
  font-size: 0.62rem;
  color: var(--text-3);
  font-family: var(--font-body);
  align-self: flex-end;
  margin-top: auto;
}
</style>
