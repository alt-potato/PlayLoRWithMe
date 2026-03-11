<!--
  AbnormalityPicker.vue

  Full-viewport overlay shown when the game prompts players to choose an
  abnormality page (emotion card) after an act. Mirrors the in-game LevelUpUI.

  Props:
    choices    – array of { id, name, emotionLevel, targetType, state, desc?, flavorText? }
    allies     – ally unit list (for SelectOne targeting step)
    allyColors – map of unitId → hex color
    teamEmotionLevel, teamCoin, teamCoinMax, teamPositiveCoins, teamNegativeCoins
      – optional team-level emotion stats shown in the header

  Emits:
    select({ cardId, targetUnitId? })
-->
<script setup lang="ts">
import type { AllyUnit } from "~/types/game";

const props = defineProps<{
  choices: Array<{
    id: number;
    name: string;
    emotionLevel: number;
    targetType: string;
    state: string; // "Positive" | "Negative"
    desc?: string;
    flavorText?: string;
  }>;
  allies: AllyUnit[];
  allyColors: Record<number, string>;
  teamEmotionLevel?: number;
  teamCoin?: number;
  teamCoinMax?: number;
  teamPositiveCoins?: number;
  teamNegativeCoins?: number;
}>();

const emit = defineEmits<{
  select: [{ cardId: number; targetUnitId?: number }];
}>();

const pendingCard = ref<(typeof props.choices)[number] | null>(null);

function onCardClick(card: (typeof props.choices)[number]) {
  if (card.targetType === "SelectOne") {
    if (pendingCard.value?.id === card.id) {
      pendingCard.value = null;
    } else {
      pendingCard.value = card;
    }
  } else {
    emit("select", { cardId: card.id });
  }
}

function onAllyClick(ally: AllyUnit) {
  if (!pendingCard.value) return;
  emit("select", { cardId: pendingCard.value.id, targetUnitId: ally.id });
}

function onBack() {
  pendingCard.value = null;
}

function stateColor(state: string) {
  return state === "Positive" ? "#4caf50" : "#c62828";
}

const totalCoins = computed(
  () => (props.teamPositiveCoins ?? 0) + (props.teamNegativeCoins ?? 0),
);
const posRatio = computed(() =>
  totalCoins.value > 0
    ? (props.teamPositiveCoins ?? 0) / totalCoins.value
    : 0.5,
);
const showTeamInfo = computed(() => props.teamEmotionLevel !== undefined);
</script>

<template>
  <div class="ab-backdrop" @click.self="onBack">
    <div class="ab-panel">
      <header class="ab-header">
        <span class="ab-title">Emotion Card Selection</span>
        <div v-if="showTeamInfo" class="ab-team-info">
          <span class="ab-team-lv">Lv {{ toRoman(teamEmotionLevel!) }}</span>
          <div
            v-if="(teamCoinMax ?? 0) > 0"
            class="ab-coin-bar-wrap"
            :title="`${teamCoin} / ${teamCoinMax} coins`"
          >
            <div class="ab-coin-bar">
              <div
                class="ab-coin-fill"
                :style="{
                  width: `${Math.min(100, ((teamCoin ?? 0) / (teamCoinMax ?? 1)) * 100)}%`,
                }"
              />
            </div>
          </div>
          <div
            v-if="totalCoins > 0"
            class="ab-posneg-wrap"
            :title="`+${teamPositiveCoins} / -${teamNegativeCoins}`"
          >
            <div class="ab-posneg-bar">
              <div
                class="ab-posneg-pos"
                :style="{ width: `${posRatio * 100}%` }"
              />
            </div>
            <span class="ab-posneg-label ab-posneg-label--pos"
              >+{{ teamPositiveCoins }}</span
            >
            <span class="ab-posneg-label ab-posneg-label--neg"
              >-{{ teamNegativeCoins }}</span
            >
          </div>
        </div>
      </header>

      <Transition name="ab-slide" mode="out-in">
        <!-- Choice list -->
        <div v-if="!pendingCard" key="choices" class="ab-choices">
          <button
            v-for="card in choices"
            :key="card.id"
            class="ab-card"
            :style="{ borderColor: stateColor(card.state) }"
            @click="onCardClick(card)"
          >
            <div class="ab-card-header">
              <span
                class="ab-card-badge"
                :style="{
                  background: stateColor(card.state) + '33',
                  color: stateColor(card.state),
                }"
              >
                {{ toRoman(card.emotionLevel) }}
              </span>
              <span
                v-if="card.targetType === 'SelectOne'"
                class="ab-card-target-hint"
                >1 ally</span
              >
            </div>
            <span class="ab-card-name">{{ card.name }}</span>
            <p v-if="card.desc" class="ab-card-desc">{{ card.desc }}</p>
            <p v-if="card.flavorText" class="ab-card-flavor">
              {{ card.flavorText }}
            </p>
          </button>
        </div>

        <!-- Ally picker (SelectOne) -->
        <div v-else key="allies" class="ab-ally-picker">
          <div class="ab-ally-subheader">
            <button class="ab-back" @click="onBack">← back</button>
            <span class="ab-ally-prompt">Select a Librarian</span>
          </div>
          <div class="ab-ally-body">
            <!-- Left: card preview -->
            <div
              class="ab-card ab-card--preview"
              :style="{ borderColor: stateColor(pendingCard.state) }"
            >
              <div class="ab-card-header">
                <span
                  class="ab-card-badge"
                  :style="{
                    background: stateColor(pendingCard.state) + '33',
                    color: stateColor(pendingCard.state),
                  }"
                >
                  {{ toRoman(pendingCard.emotionLevel) }}
                </span>
              </div>
              <span class="ab-card-name">{{ pendingCard.name }}</span>
              <p v-if="pendingCard.desc" class="ab-card-desc">
                {{ pendingCard.desc }}
              </p>
              <p v-if="pendingCard.flavorText" class="ab-card-flavor">
                {{ pendingCard.flavorText }}
              </p>
            </div>

            <!-- Right: ally list -->
            <div class="ab-ally-list">
              <button
                v-for="ally in allies"
                :key="ally.id"
                class="ab-ally-row"
                :style="{ '--ac': allyColors[ally.id] ?? '#888' }"
                @click="onAllyClick(ally)"
              >
                <span class="ab-ally-dot" />
                <span class="ab-ally-name">{{
                  ally.name ?? ally.keyPage?.name ?? `#${ally.id}`
                }}</span>
                <span class="ab-ally-lvl"
                  >Lv {{ toRoman(ally.emotionLevel ?? 0) }}</span
                >
              </button>
            </div>
          </div>
        </div>
      </Transition>
    </div>
  </div>
</template>

<style scoped>
.ab-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.78);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 200;
  padding: 1rem;
}

.ab-panel {
  background: var(--bg-surface);
  border: 1px solid var(--gold-dim);
  width: min(560px, 100%);
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* ── Header ─────────────────────────────────────────────────────────────── */
.ab-header {
  padding: 0.5rem 0.9rem;
  border-bottom: 1px solid var(--gold-dim);
  background: #0d0d00;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.ab-title {
  font-family: var(--font-display);
  font-size: 0.65rem;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--gold);
  white-space: nowrap;
}

/* ── Team info ───────────────────────────────────────────────────────────── */
.ab-team-info {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex: 1;
  min-width: 0;
}

.ab-team-lv {
  font-family: var(--font-display);
  font-size: 0.72rem;
  color: var(--gold);
  flex-shrink: 0;
}

.ab-coin-bar-wrap {
  flex: 1;
  min-width: 40px;
  max-width: 80px;
}

.ab-coin-bar {
  height: 4px;
  background: var(--border-mid);
  border-radius: 2px;
  overflow: hidden;
}

.ab-coin-fill {
  height: 100%;
  background: var(--gold);
  border-radius: 2px;
  transition: width 0.3s;
}

.ab-posneg-wrap {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  flex-shrink: 0;
}

.ab-posneg-bar {
  width: 48px;
  height: 4px;
  background: var(--crimson-hi);
  border-radius: 2px;
  overflow: hidden;
}
.ab-posneg-pos {
  height: 100%;
  background: var(--green);
  border-radius: 2px;
  transition: width 0.3s;
}
.ab-posneg-label {
  font-family: var(--font-body);
  font-size: 0.58rem;
}

.ab-posneg-label--pos {
  color: var(--green);
}
.ab-posneg-label--neg {
  color: var(--crimson-hi);
}

/* ── Choice cards ────────────────────────────────────────────────────────── */
.ab-choices {
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  flex: 1;
  gap: 0.75rem;
  padding: 0.75rem;
}

@media (min-width: 500px) {
  .ab-choices {
    flex-direction: row;
    align-items: flex-start;
  }
}

/* Match HandCard/CardDetail visual language: gradient bg, colored 1px border */
.ab-card {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  padding: 0.55rem 0.6rem 0.6rem;
  background: linear-gradient(
    160deg,
    var(--bg-card-2) 0%,
    var(--bg-card-3) 100%
  );
  border: 1px solid; /* color set inline via stateColor */
  cursor: pointer;
  text-align: left;
  transition:
    box-shadow 0.15s,
    transform 0.1s;
}

.ab-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 16px color-mix(in srgb, currentColor 20%, transparent);
}

.ab-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.3rem;
}

/* Emotion level badge — styled like the cost badge in HandCard */
.ab-card-badge {
  min-width: 1.4rem;
  height: 1.4rem;
  display: flex;
  align-items: center;
  justify-content: center;
  font-family: var(--font-display);
  font-size: 0.78rem;
  font-weight: 700;
  flex-shrink: 0;
  padding: 0 0.25rem;
}

.ab-card-target-hint {
  font-family: var(--font-body);
  font-size: 0.55rem;
  color: var(--text-3);
  font-style: italic;
  white-space: nowrap;
}

.ab-card-name {
  font-family: var(--font-display);
  font-size: 0.75rem;
  color: var(--text-1);
  letter-spacing: 0.03em;
  line-height: 1.3;
}

.ab-card-desc {
  font-family: var(--font-body);
  font-size: 0.65rem;
  color: var(--text-2);
  margin: 0;
  line-height: 1.45;
}

.ab-card-flavor {
  font-family: var(--font-body);
  font-size: 0.6rem;
  color: var(--text-3);
  font-style: italic;
  margin: 0;
  line-height: 1.4;
  border-top: 1px solid var(--border-mid);
  padding-top: 0.35rem;
  margin-top: 0.15rem;
}

/* ── Ally picker ─────────────────────────────────────────────────────────── */
.ab-ally-picker {
  display: flex;
  flex-direction: column;
  flex: 1;
  overflow: hidden;
}

.ab-ally-subheader {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.4rem 0.75rem;
  border-bottom: 1px solid var(--border-mid);
  flex-shrink: 0;
}

.ab-ally-prompt {
  font-family: var(--font-display);
  font-size: 0.62rem;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--text-3);
}

.ab-back {
  background: transparent;
  border: 1px solid var(--border-mid);
  color: var(--text-2);
  font-size: 0.65rem;
  font-family: var(--font-body);
  padding: 0.15rem 0.45rem;
  cursor: pointer;
  flex-shrink: 0;
}

.ab-back:hover {
  border-color: var(--gold-dim);
  color: var(--gold);
}

/* Two-column body: card preview | ally list */
.ab-ally-body {
  display: flex;
  flex: 1;
  overflow: hidden;
}

/* Card preview column — reuses .ab-card styles, fixed width */
.ab-card--preview {
  flex: 0 0 auto;
  width: 44%;
  max-width: 180px;
  border-right: none; /* right edge is the divider */
  border-right: 1px solid var(--border-mid);
  overflow-y: auto;
  cursor: default;
  pointer-events: none; /* not interactive */
}

.ab-card--preview:hover {
  transform: none;
  box-shadow: none;
}

.ab-ally-list {
  flex: 1;
  overflow-y: auto;
}

.ab-ally-row {
  display: flex;
  align-items: center;
  gap: 0.55rem;
  width: 100%;
  padding: 0.65rem 0.9rem;
  background: transparent;
  border: none;
  border-bottom: 1px solid var(--border-mid);
  cursor: pointer;
  text-align: left;
  transition: background 0.13s;
}

.ab-ally-row:last-child {
  border-bottom: none;
}

.ab-ally-row:hover {
  background: color-mix(in srgb, var(--ac) 10%, transparent);
}

.ab-ally-dot {
  width: 0.55rem;
  height: 0.55rem;
  border-radius: 50%;
  background: var(--ac);
  flex-shrink: 0;
}

.ab-ally-name {
  font-family: var(--font-display);
  font-size: 0.75rem;
  color: var(--text-1);
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ab-ally-lvl {
  font-family: var(--font-body);
  font-size: 0.62rem;
  color: var(--text-3);
  flex-shrink: 0;
}

/* ── Slide transition ────────────────────────────────────────────────────── */
.ab-slide-enter-active,
.ab-slide-leave-active {
  transition:
    opacity 0.18s,
    transform 0.18s;
}

.ab-slide-enter-from {
  opacity: 0;
  transform: translateX(18px);
}

.ab-slide-leave-to {
  opacity: 0;
  transform: translateX(-18px);
}
</style>
