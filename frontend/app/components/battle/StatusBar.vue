<!--
  battle/StatusBar.vue

  Shared top bar used by both Stage.vue (battle) and SettingView.vue (pre-battle).
  Displays stage metadata chips on the left and a confirm button on the right.
  The session/player panel now lives in the global app header (app.vue).

  Props:
    stage          – stage info (Floor / Chapter / Wave; Round only in battle)
    phase          – phase text displayed below the chips (e.g. C# phase class name)
    confirmEnabled – whether the confirm button is interactive
    confirmLabel   – button label ("CONFIRM", "WAITING", "…" etc.)
-->
<script setup lang="ts">
import type { StageInfo } from "~/types/game";

defineProps<{
  stage?: StageInfo;
  phase?: string;
  confirmEnabled: boolean;
  confirmLabel: string;
}>();

const emit = defineEmits<{ confirm: [] }>();
</script>

<template>
  <div class="status-bar">
    <div class="bar-left">
      <div class="stage-chips">
        <span class="chip"
          ><span class="k">Floor</span>
          <strong>{{ stage?.floor ?? "—" }}</strong></span
        >
        <span class="chip-sep">·</span>
        <span class="chip"
          ><span class="k">Ch</span>
          <strong>{{ stage?.chapter ?? "—" }}</strong></span
        >
        <span class="chip-sep">·</span>
        <span class="chip"
          ><span class="k">Act</span>
          <strong>{{ stage?.wave ?? "—" }}</strong></span
        >
        <template v-if="stage?.round != null">
          <span class="chip-sep">·</span>
          <span class="chip"
            ><span class="k">Scene</span>
            <strong>{{ stage.round }}</strong></span
          >
        </template>
      </div>
      <span v-if="phase" class="phase-text">{{ phase }}</span>
    </div>

    <div class="bar-center">
      <button
        class="confirm-btn"
        :disabled="!confirmEnabled"
        @click="emit('confirm')"
      >
        {{ confirmLabel }}
      </button>
    </div>

    <!-- Empty right cell preserves the 1fr/auto/1fr grid so the confirm button stays centered. -->
    <div class="bar-right" />
  </div>
</template>

<style scoped>
.status-bar {
  padding: 0.4rem 0.75rem;
  background: var(--bg-surface);
  border: 1px solid var(--border-mid);
  border-bottom: 1px solid var(--gold-dim);
  margin-bottom: 0.75rem;
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  gap: 0.5rem;
}

.bar-left {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 0;
}

.bar-center {
  display: flex;
  justify-content: center;
}

.bar-right {
  display: flex;
  justify-content: flex-end;
  align-items: center;
}

.stage-chips {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  flex-wrap: wrap;
}

.chip {
  font-size: var(--fs-3xs);
  color: var(--text-2);
  font-family: var(--font-body);
  display: inline-flex;
  align-items: center;
  gap: 0.2rem;
  padding: 0.05rem 0.3rem;
  border: 1px solid var(--border);
  background: var(--bg-card);
}

.chip .k {
  font-family: var(--font-display);
  font-size: var(--fs-4xs);
  letter-spacing: 0.07em;
  text-transform: uppercase;
  color: var(--text-2);
}

.chip strong {
  color: var(--gold);
}

.chip-sep {
  color: var(--border-hi);
  font-size: var(--fs-3xs);
}

.phase-text {
  color: var(--text-2);
  font-size: var(--fs-3xs);
  font-family: var(--font-mono);
  white-space: nowrap;
}

.confirm-btn {
  padding: 0.25rem 1rem;
  background: var(--bg-green);
  border: 1px solid var(--green-hi);
  color: var(--green);
  font-size: var(--fs-3xs);
  font-family: var(--font-display);
  letter-spacing: 0.08em;
  cursor: pointer;
  white-space: nowrap;
  transition:
    background 0.15s,
    color 0.15s;
}

.confirm-btn:hover:not(:disabled) {
  background: var(--green-hi);
  color: #fff;
}

.confirm-btn:disabled {
  border-color: var(--border-mid);
  color: var(--text-3);
  background: transparent;
  cursor: not-allowed;
  opacity: 0.5;
}
</style>
