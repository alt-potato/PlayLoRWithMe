<!--
  DiagnosticPanel.vue (dev only)

  Floating overlay showing live WebSocket diagnostics: in-flight action
  count, lastSeq, total resyncs, time since last resync, and connection
  status. Used to diagnose the spam-induced lockup and similar issues.
  Mounted only when `import.meta.dev`, so it tree-shakes from production.
-->
<script setup lang="ts">
const props = defineProps<{
  status: "connecting" | "connected" | "disconnected";
  inflightCount: number;
  lastSeq: number;
  resyncCount: number;
  lastResyncAt: number | null;
}>();

// "now" tick so "X s ago" updates without external triggers.
const now = ref(Date.now());
let tickTimer: ReturnType<typeof setInterval> | null = null;
onMounted(() => {
  tickTimer = setInterval(() => {
    now.value = Date.now();
  }, 500);
});
onBeforeUnmount(() => {
  if (tickTimer) clearInterval(tickTimer);
});

const lastResyncAgo = computed(() => {
  if (props.lastResyncAt === null) return "—";
  const diffMs = now.value - props.lastResyncAt;
  if (diffMs < 1000) return `${diffMs} ms ago`;
  return `${Math.floor(diffMs / 1000)} s ago`;
});

// Minimize/expand toggle so the panel doesn't permanently block the UI.
const collapsed = ref(false);
</script>

<template>
  <div class="diag-panel" :class="{ 'is-collapsed': collapsed }">
    <button
      type="button"
      class="diag-toggle"
      @click="collapsed = !collapsed"
      :title="collapsed ? 'Expand diagnostics' : 'Collapse diagnostics'"
    >
      {{ collapsed ? "+" : "−" }}
    </button>
    <div v-if="!collapsed" class="diag-rows">
      <div class="diag-title">WS DIAG</div>
      <div class="diag-row">
        <span class="diag-label">status</span>
        <span class="diag-value" :class="`status-${status}`">{{ status }}</span>
      </div>
      <div class="diag-row">
        <span class="diag-label">in-flight</span>
        <span class="diag-value" :class="{ 'value-warn': inflightCount > 5 }">{{ inflightCount }}</span>
      </div>
      <div class="diag-row">
        <span class="diag-label">lastSeq</span>
        <span class="diag-value">{{ lastSeq }}</span>
      </div>
      <div class="diag-row">
        <span class="diag-label">resyncs</span>
        <span class="diag-value" :class="{ 'value-warn': resyncCount > 0 }">{{ resyncCount }}</span>
      </div>
      <div class="diag-row">
        <span class="diag-label">last resync</span>
        <span class="diag-value">{{ lastResyncAgo }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.diag-panel {
  position: fixed;
  bottom: 0.5rem;
  right: 0.5rem;
  z-index: 9999;
  background: rgba(0, 0, 0, 0.85);
  color: #ddd;
  font-family: ui-monospace, "Cascadia Code", Menlo, Consolas, monospace;
  font-size: 0.7rem;
  border: 1px solid #555;
  padding: 0.3rem 0.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 9rem;
  pointer-events: auto;
  user-select: none;
}

.diag-panel.is-collapsed {
  min-width: 0;
  padding: 0.1rem 0.25rem;
}

.diag-toggle {
  position: absolute;
  top: 0.05rem;
  right: 0.15rem;
  background: transparent;
  border: none;
  color: #ddd;
  font-size: 0.85rem;
  font-family: inherit;
  cursor: pointer;
  padding: 0 0.2rem;
  line-height: 1;
}

.diag-title {
  font-size: 0.6rem;
  letter-spacing: 0.1em;
  color: #999;
  margin-bottom: 0.15rem;
  padding-right: 1rem;
}

.diag-rows {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
}

.diag-row {
  display: flex;
  justify-content: space-between;
  gap: 0.5rem;
}

.diag-label {
  color: #888;
}

.diag-value {
  color: #ddd;
  font-variant-numeric: tabular-nums;
}

.status-connected {
  color: #6ecf6e;
}
.status-connecting {
  color: #ddc26e;
}
.status-disconnected {
  color: #e26666;
}

.value-warn {
  color: #ff9347;
}
</style>
