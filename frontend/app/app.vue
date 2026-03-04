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

    <details class="raw-json">
      <summary>Raw JSON</summary>
      <pre>{{ rawJson }}</pre>
    </details>
  </div>
</template>

<style>
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

body {
  font-family: monospace;
  background: #0f0f1a;
  color: #d0d0d0;
  min-height: 100vh;
}
</style>

<style scoped>
.app {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  padding: 0.75rem;
  gap: 0.75rem;
}

header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.4rem 0.75rem;
  background: #16162a;
  border: 1px solid #2a2a4a;
  border-radius: 4px;
}

.title { color: #c9a227; font-size: 1rem; font-weight: bold; }

.conn { font-size: 0.75rem; }
.conn.connecting    { color: #888; }
.conn.connected     { color: #4caf50; }
.conn.disconnected  { color: #f44336; }

main { flex: 1; }

.scene-idle {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 40vh;
  gap: 0.5rem;
}
.scene-name { font-size: 2rem; color: #c9a227; text-transform: uppercase; letter-spacing: 0.2em; }
.scene-sub  { font-size: 1rem; color: #888; }

.raw-json {
  background: #0a0a15;
  border: 1px solid #2a2a4a;
  border-radius: 4px;
  padding: 0.5rem 0.75rem;
}
.raw-json summary {
  cursor: pointer;
  color: #666;
  font-size: 0.75rem;
  user-select: none;
}
.raw-json pre {
  margin-top: 0.5rem;
  font-size: 0.7rem;
  color: #666;
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 400px;
  overflow-y: auto;
}
</style>
