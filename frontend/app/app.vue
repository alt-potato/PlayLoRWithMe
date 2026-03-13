<script setup lang="ts">
type ConnectionStatus = "connecting" | "connected" | "disconnected";

const statusLabel: Record<ConnectionStatus, string> = {
  connecting: "Connecting…",
  connected: "Connected",
  disconnected: "Disconnected",
};

useHead({
  link: [
    { rel: "preconnect", href: "https://fonts.googleapis.com" },
    { rel: "preconnect", href: "https://fonts.gstatic.com", crossorigin: "" },
    {
      rel: "stylesheet",
      href: "https://fonts.googleapis.com/css2?family=Cinzel:wght@400;600;700&family=Noto+Sans:wght@300;400;500&display=swap",
    },
  ],
});

const {
  gameState,
  session,
  status,
  players,
  sendAction,
  claimUnit,
  releaseUnit,
  renamePlayer,
} = useWebSocket();

const rawJson = computed(() =>
  gameState.value ? JSON.stringify(gameState.value, null, 2) : "—",
);
</script>

<template>
  <div class="app">
    <header>
      <span class="title">PlayLoRWithMe</span>
      <span :class="['conn', status]">{{ statusLabel[status] }}</span>
    </header>

    <main>
      <!--
        BattleSetting phase: pre-battle formation/claim screen shown while the
        main menu is in its BattleSetting UI phase (before the battle scene loads).
      -->
      <BattleSettingView
        v-if="
          gameState?.scene === 'main' && gameState.uiPhase === 'BattleSetting'
        "
        :state="gameState"
        :session="session"
        :players="players"
        :send-action="sendAction"
        :claim-unit="claimUnit"
        :release-unit="releaseUnit"
        :rename-player="renamePlayer"
      />

      <BattleStage
        v-else-if="gameState?.scene === 'battle'"
        :state="gameState"
        :session="session"
        :players="players"
        :send-action="sendAction"
        :claim-unit="claimUnit"
        :release-unit="releaseUnit"
        :rename-player="renamePlayer"
      />

      <div v-else-if="gameState" class="scene-idle">
        <div class="scene-name">{{ gameState.scene }}</div>
        <div v-if="gameState.uiPhase" class="scene-sub">
          {{ gameState.uiPhase }}
        </div>
      </div>

      <div v-else class="scene-idle">
        <div class="scene-name">—</div>
      </div>
    </main>

    <details class="debug-info">
      <summary><span class="chevron">▸</span>debug info</summary>
      <pre>{{ rawJson }}</pre>
    </details>
  </div>
</template>

<style>
/* ── Global design tokens ─────────────────────────────────────────────────── */
:root {
  --bg: #050812;
  --bg-surface: #0b0d1e;
  --bg-card: #0e1026;
  --bg-card-2: #131628;
  --bg-card-3: #191c30;

  --border: #1c1f3c;
  --border-mid: #252849;
  --border-hi: #31355c;

  --gold: #c9a227;
  --gold-dim: #7a6118;
  --gold-bright: #e0bb38;

  --crimson: #8b1a1a;
  --crimson-hi: #c62828;
  --crimson-dim: #3d0a0a;

  --text-1: #cdc6b5;
  --text-2: #786e5e;
  --text-3: #3c3830;

  --green: #4caf50;
  --green-hi: #2e7d32;
  --text-green: #81c784;
  --bg-green: #0a1a0a;
  --bg-green-2: #0c1e0c;
  --bg-green-3: #102010;

  /* Brighter red for dead units / error states (more saturated than --crimson-hi) */
  --red-hi: #e53935;
  --text-red: #ef9a9a;

  /* Warning / exhausted / orange */
  --orange: #ff9800;
  --orange-dim: #a05000;

  /* Pending / targeting banner background */
  --bg-gold: #1a1400;

  /* Info blocks (card tokens, status chips) */
  --bg-info: #0d1a2e;
  --border-info: #3d5a80;
  --text-info: #90a4ae;
  --text-info-hi: #b0bec5;

  /* Passive rarity accent colors */
  --rarity-uncommon: #56a348;
  --rarity-rare: #4169c4;

  --blue-hi: #1976d2;
  --cyan: #4fc3f7;

  --font-display: "Cinzel", "Palatino Linotype", serif;
  --font-body: "Noto Sans", system-ui, sans-serif;
  --font-mono: "Courier New", Courier, monospace;

  /* Flat-top hexagon: wider than tall, pointy sides */
  --hex: polygon(25% 0%, 75% 0%, 100% 50%, 75% 100%, 25% 100%, 0% 50%);

  /* clash/stagger colors */
  --health: #e56031;
  --health-bar: #ed372c;
  --stagger: #f0f464;
  --stagger-bar: #e9f762;

  /* Clash-specific colors */
  /* 
    enemy -> ally: incoming
    enemy <- ally: outgoing
  */
  --incoming: var(--crimson-hi);
  --clash: var(--gold);
  --outgoing: var(--cyan);
}

*,
*::before,
*::after {
  box-sizing: border-box;
  margin: 0;
  padding: 0;
}

body {
  font-family: var(--font-body);
  background: var(--bg);
  color: var(--text-1);
  min-height: 100dvh;
  -webkit-font-smoothing: antialiased;
  font-size: 16px;
}

::-webkit-scrollbar {
  width: 4px;
  height: 4px;
}
::-webkit-scrollbar-track {
  background: var(--bg);
}
::-webkit-scrollbar-thumb {
  background: var(--border-mid);
  border-radius: 2px;
}
::-webkit-scrollbar-thumb:hover {
  background: var(--border-hi);
}

/* ── Logic for reversing component order ── */
/* 
  The "normal" side is the ally side (right). 
  Applying "reversed-order" will reverse the order of all components in a "reversible-container".
*/
.reversed-order .reversible-container {
  flex-direction: row-reverse;
}
.reversed-order .reversible-text {
  text-align: left;
}

/* ── Collapsibles ──────────────────────────────────────────────────────────── */
.collapse {
  margin-top: 0.05rem;
}
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
.collapse summary:hover {
  color: var(--text-1);
}
.collapse summary::marker,
.collapse summary::-webkit-details-marker {
  display: none;
}
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

.conn {
  font-size: 0.68rem;
  font-family: var(--font-body);
}
.conn.connecting {
  color: var(--text-2);
}
.conn.connected {
  color: var(--green);
}
.conn.disconnected {
  color: var(--crimson-hi);
}

main {
  flex: 1;
}

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
.chevron {
  font-size: 0.8rem;
  color: var(--text-3);
  display: inline-block;
  margin-right: 0.3em;
  transition: transform 0.18s ease;
}
.debug-info summary {
  cursor: pointer;
  color: var(--text-3);
  font-size: 0.68rem;
  font-family: var(--font-mono);
  user-select: none;
  list-style: none;
}
.debug-info summary::marker,
.debug-info summary::-webkit-details-marker {
  display: none;
}
.debug-info[open] summary .chevron {
  transform: rotate(90deg);
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
