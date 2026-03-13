<!--
  battle/StatusBar.vue

  Shared top bar used by both Stage.vue (battle) and SettingView.vue (pre-battle).
  Displays stage metadata chips on the left, a confirm button in the center,
  and the session/player panel on the right.

  Props:
    stage          – stage info (Floor / Chapter / Wave; Round only in battle)
    phase          – phase text displayed below the chips (e.g. C# phase class name)
    confirmEnabled – whether the confirm button is interactive
    confirmLabel   – button label ("CONFIRM", "WAITING", "…" etc.)
    players        – connected player list
    allies         – allied units forwarded to SessionPanel
    session        – current session identity (null if not yet connected)
    allyColors     – unitId → hex color map forwarded to SessionPanel
    showLibrarians – true in battle: enables the Librarians tab in SessionPanel
    claimUnit      – claim a librarian unit
    releaseUnit    – release a librarian unit
    renamePlayer   – rename the current player
-->
<script setup lang="ts">
import type {
  AllyUnit,
  PlayerInfo,
  SessionState,
  StageInfo,
  ActionResult,
} from "~/types/game";

defineProps<{
  stage?: StageInfo;
  phase?: string;
  confirmEnabled: boolean;
  confirmLabel: string;
  players: PlayerInfo[];
  allies: AllyUnit[];
  session: SessionState | null;
  allyColors: Record<number, string>;
  showLibrarians: boolean;
  claimUnit: (unitId: number) => Promise<ActionResult>;
  releaseUnit: (unitId: number) => Promise<ActionResult>;
  renamePlayer: (name: string) => Promise<ActionResult>;
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

    <div class="bar-right">
      <SessionPanel
        v-if="players.length > 0"
        :allies="allies"
        :players="players"
        :session="session"
        :ally-colors="allyColors"
        :show-librarians="showLibrarians"
        :claim-unit="claimUnit"
        :release-unit="releaseUnit"
        :rename-player="renamePlayer"
      />
    </div>
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
  font-size: 0.72rem;
  color: var(--text-2);
  font-family: var(--font-body);
}

.chip .k {
  font-family: var(--font-display);
  font-size: 0.58rem;
  letter-spacing: 0.07em;
  text-transform: uppercase;
  color: var(--text-2);
}

.chip strong {
  color: var(--gold);
}

.chip-sep {
  color: var(--border-hi);
  font-size: 0.65rem;
}

.phase-text {
  color: var(--text-2);
  font-size: 0.65rem;
  font-family: var(--font-mono);
  white-space: nowrap;
}

.confirm-btn {
  padding: 0.25rem 1rem;
  background: var(--bg-green);
  border: 1px solid var(--green-hi);
  color: var(--green);
  font-size: 0.7rem;
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
