<!--
  CardDetail.vue

  Bottom-sheet overlay showing full card detail: dice breakdown, ability
  description, rarity, tags. Follows the same fixed backdrop + sheet
  pattern as TargetPicker.vue.

  Props:
    card – card object from game state

  Emits:
    close – user dismissed the sheet
-->
<script setup lang="ts">
const props = defineProps<{ card: any }>();
defineEmits<{ close: [] }>();

const isEgo = computed(() =>
  props.card.options?.some((o: string) => o.startsWith("Ego") || o === "EGO"),
);
const isExhaust = computed(() => props.card.options?.includes("ExhaustOnUse"));
const borderColor = computed(() => cardBorderColor(props.card));
</script>

<template>
  <div class="backdrop" @click="$emit('close')" />
  <div class="detail-sheet">
    <!-- Header -->
    <div class="sheet-header" :style="{ borderTopColor: borderColor }">
      <div class="header-top">
        <span class="card-range">{{ card.range }}</span>
        <span class="cost-badge" :style="costStyle(card) ?? {}">{{ card.cost }}</span>
        <button class="close-btn" @click="$emit('close')">✕</button>
      </div>
      <div class="card-title">{{ card.name }}</div>
      <div class="card-tags">
        <span
          class="tag rarity-tag"
          :style="{ color: borderColor, borderColor: borderColor }"
          >{{ card.rarity }}</span
        >
        <span v-if="isEgo" class="tag ego-tag">EGO</span>
        <span v-if="card.emotionLimit > 0" class="tag emotion-tag"
          >Em{{ card.emotionLimit }}+</span
        >
        <span v-if="isExhaust" class="tag exhaust-tag">Exhaust</span>
      </div>
    </div>

    <!-- Body -->
    <div class="sheet-body">
      <div v-if="card.bufs?.length" class="token-list">
        <div v-for="(b, i) in card.bufs" :key="i" class="token-entry">
          <span class="token-label">{{ b.label }}</span>
          <span v-if="b.stack > 0" class="token-stack">×{{ b.stack }}</span>
        </div>
      </div>

      <p v-if="card.abilityDesc" class="ability-desc">{{ card.abilityDesc }}</p>

      <div v-if="card.dice?.length" class="dice-list">
        <div
          v-for="(d, i) in card.dice"
          :key="i"
          class="die-row"
          :style="{ borderLeftColor: dieTypeColor(d.type) }"
        >
          <div class="die-icon-wrap">
            <img
              v-if="diceIcon(d.type, d.detail)"
              :src="diceIcon(d.type, d.detail)!"
              class="die-img"
              :alt="`${d.type} ${d.detail}`"
            />
            <span v-else class="die-img-placeholder">—</span>
          </div>
          <div class="die-info">
            <span class="die-range" :style="{ color: dieTypeColor(d.type) }">
              {{ d.min }}–{{ d.max }}
            </span>
            <span v-if="d.desc" class="die-desc">{{ d.desc }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.backdrop {
  position: fixed;
  inset: 0;
  background: rgba(2, 3, 12, 0.7);
  z-index: 100;
}

.detail-sheet {
  position: fixed;
  left: 0;
  right: 0;
  bottom: 0;
  max-height: 78vh;
  overflow-y: auto;
  background: var(--bg-surface);
  border-top: 3px solid var(--gold-dim);
  border-radius: 10px 10px 0 0;
  z-index: 101;
  padding-bottom: env(safe-area-inset-bottom, 0);
}

/* ── Sheet header ────────────────────────────────────────────────────────── */
.sheet-header {
  position: sticky;
  top: 0;
  background: var(--bg-surface);
  border-top: 3px solid transparent;
  border-bottom: 1px solid var(--border-mid);
  padding: 0.75rem 1rem 0.6rem;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.header-top {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.card-range {
  font-family: var(--font-body);
  font-size: 0.65rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-2);
  flex: 1;
}

.cost-badge {
  width: 1.5rem;
  height: 1.5rem;
  background: var(--gold-dim);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.82rem;
  color: var(--gold-bright);
  font-family: var(--font-body);
  font-weight: bold;
  flex-shrink: 0;
}

.close-btn {
  background: transparent;
  border: none;
  color: var(--text-2);
  font-size: 1rem;
  cursor: pointer;
  padding: 0 0.2rem;
  line-height: 1;
  flex-shrink: 0;
}
.close-btn:hover {
  color: var(--crimson-hi);
}

.card-title {
  font-family: var(--font-display);
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: 0.03em;
}

.card-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}

.tag {
  font-size: 0.58rem;
  padding: 0.08rem 0.35rem;
  border: 1px solid;
  font-family: var(--font-body);
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.rarity-tag {
  background: transparent;
}
.ego-tag {
  color: #c62828;
  border-color: #c62828;
  background: #1a0505;
}
.emotion-tag {
  color: var(--gold);
  border-color: var(--gold-dim);
  background: transparent;
}
.exhaust-tag {
  color: #ff9800;
  border-color: #a05000;
  background: transparent;
}

/* ── Card tokens ─────────────────────────────────────────────────────────── */
.token-list {
  display: flex;
  flex-wrap: wrap;
  gap: 0.3rem;
}

.token-entry {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.15rem 0.45rem;
  background: #0d1a2e;
  border: 1px solid #3d5a80;
  color: #90a4ae;
  font-size: 0.68rem;
  font-family: var(--font-body);
}

.token-stack {
  color: #b0bec5;
  font-weight: 600;
}

/* ── Sheet body ──────────────────────────────────────────────────────────── */
.sheet-body {
  padding: 0.75rem 1rem 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.ability-desc {
  font-family: var(--font-body);
  font-size: 0.78rem;
  color: var(--text-2);
  line-height: 1.5;
  margin: 0;
}

.dice-list {
  display: flex;
  flex-direction: column;
  gap: 0;
}

.die-row {
  display: flex;
  align-items: flex-start;
  gap: 0.6rem;
  padding: 0.6rem 0;
  border-bottom: 1px solid var(--border);
  border-left: 2px solid transparent;
  padding-left: 0.5rem;
}

.die-icon-wrap {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 1.6rem;
}

.die-img {
  width: 1.375rem;
  height: 1.375rem;
  object-fit: contain;
}

.die-img-placeholder {
  font-size: 1rem;
  color: var(--text-3);
}

.die-info {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  flex: 1;
  min-width: 0;
}

.die-range {
  font-family: var(--font-body);
  font-size: 1rem;
  font-weight: 600;
}

.die-desc {
  font-family: var(--font-body);
  font-size: 0.72rem;
  color: var(--text-2);
  line-height: 1.4;
}
</style>
