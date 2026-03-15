<!--
  Canonical display for a single abnormality page (emotion card).
  Used both in the live battle selection overlay (AbnormalityPicker) and in
  the librarian roster (LibrarianManager).

  In interactive contexts (AbnormalityPicker), wire @click to trigger selection.
  In read-only contexts (LibrarianManager), pass :readonly="true" to suppress
  the cursor and hover animation.

  Parent components are responsible for layout (flex sizing, container widths).
  This component always sets flex:1 so it fills its grid cell naturally.

  Props:
    name         – card display name
    state        – "Positive" (green) or "Negative" (red)
    emotionLevel – emotion-coin threshold shown in the level badge
    targetType   – "All" | "SelectOne" | "AllIncludingEnemy"
    desc         – ability description text (optional)
    flavorText   – italic flavour text shown below desc (optional)
    readonly     – disables cursor and hover lift (default false)

  Emits:
    click – fired when the card is clicked and readonly is false
-->
<script setup lang="ts">
const props = defineProps<{
  name: string;
  state: string;
  emotionLevel: number;
  targetType: string;
  desc?: string;
  flavorText?: string;
  readonly?: boolean;
}>();

const emit = defineEmits<{ click: [] }>();

/** Accent color for the card border and level badge, driven by state. */
function stateColor(state: string): string {
  return state === "Positive" ? "#4caf50" : "#c62828";
}

/** Short targeting hint shown top-right; null for the common "All" case. */
function targetHint(targetType: string): string | null {
  if (targetType === "SelectOne") return "1 ally";
  if (targetType === "AllIncludingEnemy") return "all+enemies";
  return null;
}
</script>

<template>
  <div
    class="ep-card"
    :class="{ 'ep-card--readonly': readonly }"
    :style="{ borderColor: stateColor(state) }"
    @click="!readonly && emit('click')"
  >
    <div class="ep-header">
      <span
        class="ep-badge"
        :style="{
          background: stateColor(state) + '33',
          color: stateColor(state),
        }"
        >{{ toRoman(emotionLevel) }}</span
      >
      <span v-if="targetHint(targetType)" class="ep-target">
        {{ targetHint(targetType) }}
      </span>
    </div>
    <span class="ep-name">{{ name }}</span>
    <p v-if="desc" class="ep-desc">{{ desc }}</p>
    <p v-if="flavorText" class="ep-flavor">{{ flavorText }}</p>
  </div>
</template>

<style scoped>
.ep-card {
  flex: 1;
  min-width: 130px;
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
  text-align: left;
  transition:
    box-shadow 0.15s,
    transform 0.1s;
}

.ep-card:not(.ep-card--readonly) {
  cursor: pointer;
}

.ep-card:not(.ep-card--readonly):hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 16px color-mix(in srgb, currentColor 20%, transparent);
}

.ep-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.3rem;
}

/* Emotion level badge — mirrors AbnormalityPicker's cost-badge convention */
.ep-badge {
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

.ep-target {
  font-family: var(--font-body);
  font-size: 0.55rem;
  color: var(--text-3);
  font-style: italic;
  white-space: nowrap;
}

.ep-name {
  font-family: var(--font-display);
  font-size: 0.75rem;
  color: var(--text-1);
  letter-spacing: 0.03em;
  line-height: 1.3;
}

.ep-desc {
  font-family: var(--font-body);
  font-size: 0.65rem;
  color: var(--text-2);
  margin: 0;
  line-height: 1.45;
}

.ep-flavor {
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
</style>
