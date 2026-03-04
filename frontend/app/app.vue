<script setup lang="ts">
type ConnectionStatus = 'connecting' | 'connected' | 'disconnected'

const status = ref<ConnectionStatus>('connecting')
const gameState = ref<string>('—')

const statusLabel: Record<ConnectionStatus, string> = {
  connecting: 'Connecting…',
  connected: 'Connected',
  disconnected: 'Disconnected — reconnecting…',
}

onMounted(() => {
  const es = new EventSource('/events')

  es.onopen = () => {
    status.value = 'connected'
  }

  es.onmessage = (ev: MessageEvent) => {
    try {
      gameState.value = JSON.stringify(JSON.parse(ev.data as string), null, 2)
    } catch {
      gameState.value = ev.data as string
    }
  }

  // EventSource reconnects automatically; onopen will fire again when it does
  es.onerror = () => {
    status.value = 'disconnected'
  }
})
</script>

<template>
  <div class="container">
    <h1>PlayLoRWithMe</h1>
    <p :class="['status', status]">{{ statusLabel[status] }}</p>
    <h2>Game State</h2>
    <pre class="state-box">{{ gameState }}</pre>
  </div>
</template>

<style>
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

body {
  font-family: monospace;
  background: #1a1a2e;
  color: #e0e0e0;
  padding: 2rem;
}
</style>

<style scoped>
h1 { color: #c9a227; margin-bottom: 1rem; }
h2 { margin: 1.5rem 0 0.5rem; }

.status { margin-bottom: 0.5rem; }
.status.connecting    { color: #aaaaaa; }
.status.connected     { color: #4caf50; }
.status.disconnected  { color: #f44336; }

.state-box {
  background: #16213e;
  border: 1px solid #0f3460;
  border-radius: 4px;
  padding: 1rem;
  white-space: pre-wrap;
  word-break: break-all;
  min-height: 4rem;
}
</style>
