<script setup lang="ts">
type ConnectionStatus = 'connecting' | 'connected' | 'disconnected'

const status = ref<ConnectionStatus>('connecting')
const gameState = ref<any>(null)
const rawJson = ref<string>('—')

const statusLabel: Record<ConnectionStatus, string> = {
  connecting: 'Connecting…',
  connected: 'Connected',
  disconnected: 'Disconnected — reconnecting…',
}

useHead({
  link: [
    { rel: 'preconnect', href: 'https://fonts.googleapis.com' },
    { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: '' },
    {
      rel: 'stylesheet',
      href: 'https://fonts.googleapis.com/css2?family=Cinzel:wght@400;600;700&family=Noto+Sans:wght@300;400;500&display=swap',
    },
  ],
})

onMounted(() => {
  const es = new EventSource('/events')
  es.onopen = () => { status.value = 'connected' }
  es.onmessage = (ev: MessageEvent) => {
    try {
      const parsed = JSON.parse(ev.data as string)
      gameState.value = parsed
      rawJson.value = JSON.stringify(parsed, null, 2)
    } catch {
      rawJson.value = ev.data as string
    }
  }
  es.onerror = () => { status.value = 'disconnected' }
})
</script>

<template>
  <div class="app">
    <header>
      <span class="title">PlayLoRWithMe</span>
      <span :class="['conn', status]">{{ statusLabel[status] }}</span>
    </header>

    <main>
      <BattleView v-if="gameState?.scene === 'battle'" :state="gameState" />

      <div v-else-if="gameState" class="scene-idle">
        <div class="scene-name">{{ gameState.scene }}</div>
        <div v-if="gameState.uiPhase" class="scene-sub">{{ gameState.uiPhase }}</div>
      </div>

      <div v-else class="scene-idle">
        <div class="scene-name">—</div>
      </div>
    </main>

    <details class="debug-info">
      <summary>debug info</summary>
      <pre>{{ rawJson }}</pre>
    </details>
  </div>
</template>

<style>
/* ── Global design tokens ─────────────────────────────────────────────────── */
:root {
  --bg:          #050812;
  --bg-surface:  #0b0d1e;
  --bg-card:     #0e1026;
  --bg-card-2:   #131628;
  --bg-card-3:   #191c30;

  --border:      #1c1f3c;
  --border-mid:  #252849;
  --border-hi:   #31355c;

  --gold:        #c9a227;
  --gold-dim:    #7a6118;
  --gold-bright: #e0bb38;

  --crimson:     #8b1a1a;
  --crimson-hi:  #c62828;
  --crimson-dim: #3d0a0a;

  --text-1:      #cdc6b5;
  --text-2:      #786e5e;
  --text-3:      #3c3830;

  --green-hi:    #2e7d32;
  --blue-hi:     #1976d2;

  --font-display: 'Cinzel', 'Palatino Linotype', serif;
  --font-body:    'Noto Sans', system-ui, sans-serif;
  --font-mono:    'Courier New', Courier, monospace;

  /* Flat-top hexagon: wider than tall, pointy sides */
  --hex: polygon(25% 0%, 75% 0%, 100% 50%, 75% 100%, 25% 100%, 0% 50%);
}

*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

body {
  font-family: var(--font-body);
  background: var(--bg);
  color: var(--text-1);
  min-height: 100dvh;
  -webkit-font-smoothing: antialiased;
  font-size: 16px;
}

::-webkit-scrollbar { width: 4px; height: 4px; }
::-webkit-scrollbar-track { background: var(--bg); }
::-webkit-scrollbar-thumb { background: var(--border-mid); border-radius: 2px; }
::-webkit-scrollbar-thumb:hover { background: var(--border-hi); }

/* ── Shared unit card ──────────────────────────────────────────────────────── */
.unit-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  padding: 0.6rem;
  display: flex;
  flex-direction: column;
  gap: 0.32rem;
  font-size: 0.78rem;
  font-family: var(--font-body);
  overflow: visible;
  max-width: 24rem;
}
.unit-name {
  font-family: var(--font-display);
  font-size: 0.7rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  color: var(--text-1);
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.state-badge {
  font-family: var(--font-mono);
  font-size: 0.52rem;
  padding: 0.1rem 0.3rem;
  color: #000;
  white-space: nowrap;
  font-weight: bold;
  flex-shrink: 0;
}

/* ── Hexagonal die ─────────────────────────────────────────────────────────── */
/* Two-layer clip-path creates a "border" effect without actual CSS border.
   data-die is on the outer element so ArrowOverlay can locate it correctly. */
.hex-wrap {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.4rem;
  height: 2.1rem;
  clip-path: var(--hex);
  background: var(--border-mid);
  flex-shrink: 0;
}
.hex-inner {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 1.75rem;
  clip-path: var(--hex);
  background: var(--bg-card-2);
  font-family: var(--font-mono);
  font-size: 0.82rem;
  color: var(--text-1);
  pointer-events: none;
}
.hex-wrap.staggered { background: var(--crimson-dim); }
.hex-wrap.staggered .hex-inner { background: #230808; color: var(--crimson-hi); }

/* ── Buffs ─────────────────────────────────────────────────────────────────── */
.buffs { display: flex; flex-wrap: wrap; gap: 0.2rem; }
.buff-tag {
  font-size: 0.6rem;
  padding: 0.08rem 0.28rem;
  background: #1c1000;
  border: 1px solid #4a2800;
  color: #ff9800;
  font-family: var(--font-mono);
}

/* ── Slot card content ─────────────────────────────────────────────────────── */
.slot-filled { background: var(--bg-card-2); }
.sc-name { color: var(--text-1); flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.72rem; }
.sc-target { white-space: nowrap; font-size: 0.62rem; flex-shrink: 0; color: var(--text-2); font-family: var(--font-mono); }
.sc-clash { font-weight: bold; color: var(--crimson-hi); }
.slot-empty { color: var(--text-3); font-style: italic; font-size: 0.68rem; }

/* ── Collapsibles ──────────────────────────────────────────────────────────── */
.collapse { margin-top: 0.05rem; }
.collapse summary {
  cursor: pointer;
  font-size: 0.66rem;
  color: var(--text-2);
  user-select: none;
  padding: 0.1rem 0;
  font-family: var(--font-body);
  letter-spacing: 0.03em;
  list-style: none;
}
.collapse summary:hover { color: var(--text-1); }
.collapse summary::marker, .collapse summary::-webkit-details-marker { display: none; }
.collapse summary::before { content: '▸ '; font-size: 0.55rem; color: var(--text-3); }
details[open] > summary::before { content: '▾ '; }

/* ── Generic card list (hand, EGO, passives, etc.) ────────────────────────── */
.clist { display: flex; flex-direction: column; gap: 0.08rem; margin-top: 0.2rem; }
.centry { display: flex; gap: 0.3rem; align-items: baseline; font-size: 0.7rem; color: var(--text-2); }
.centry.unavailable { color: var(--text-3); }
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
  font-family: var(--font-mono);
}
.centry-range { color: var(--text-3); margin-left: auto; font-size: 0.6rem; font-family: var(--font-mono); }

/* ── Unit header meta (light pips + emotion, inline in header) ─────────────── */
.unit-meta { display: flex; gap: 0.25rem; align-items: center; flex-shrink: 0; }

/* ── Light pips ─────────────────────────────────────────────────────────────── */
.ap-pips { display: flex; gap: 0.08rem; align-items: center; flex-wrap: wrap; }
.ap-pip { width: 0.62rem; height: 0.54rem; clip-path: var(--hex); background: var(--border-hi); flex-shrink: 0; transition: background 0.15s; }
.ap-pip--lit { background: var(--gold); }

/* ── Emotion display ─────────────────────────────────────────────────────────── */
.emotion-meta { display: flex; align-items: center; gap: 0.2rem; }
.em-level { font-family: var(--font-mono); font-size: 0.55rem; color: var(--text-2); white-space: nowrap; flex-shrink: 0; }
.epips { display: flex; gap: 0.09rem; align-items: center; flex-wrap: wrap; }
.epip { width: 0.45rem; height: 0.45rem; border-radius: 50%; flex-shrink: 0; }
.epip--pos { background: #4caf50; }
.epip--neg { background: #e53935; }
.epip--empty { background: var(--border-mid); }
</style>

<style scoped>
.app {
  display: flex;
  flex-direction: column;
  min-height: 100dvh;
  padding: 0.55rem;
  gap: 0.55rem;
}

header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.45rem 1rem;
  background: var(--bg-surface);
  border: 1px solid var(--border-mid);
  border-top: 2px solid var(--gold-dim);
}

.title {
  font-family: var(--font-display);
  color: var(--gold);
  font-size: 0.82rem;
  font-weight: 700;
  letter-spacing: 0.16em;
}

.conn { font-size: 0.68rem; font-family: var(--font-mono); }
.conn.connecting   { color: var(--text-2); }
.conn.connected    { color: #4caf50; }
.conn.disconnected { color: var(--crimson-hi); }

main { flex: 1; }

.scene-idle {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 50vh;
  gap: 0.75rem;
}
.scene-name {
  font-family: var(--font-display);
  font-size: 2.2rem;
  color: var(--gold);
  text-transform: uppercase;
  letter-spacing: 0.25em;
  font-weight: 700;
}
.scene-sub {
  font-size: 1.05rem;
  color: var(--text-2);
  letter-spacing: 0.08em;
}

.debug-info {
  background: var(--bg-surface);
  border: 1px solid var(--border);
  padding: 0.4rem 0.75rem;
}
.debug-info summary {
  cursor: pointer;
  color: var(--text-3);
  font-size: 0.68rem;
  font-family: var(--font-mono);
  user-select: none;
}
.debug-info pre {
  margin-top: 0.4rem;
  font-size: 0.62rem;
  color: var(--text-3);
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 400px;
  overflow-y: auto;
  font-family: var(--font-mono);
}
</style>
