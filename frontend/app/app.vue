<script setup lang="ts">
type ConnectionStatus = "connecting" | "connected" | "disconnected";

const statusTitle: Record<ConnectionStatus, string> = {
  connecting: "Connecting to server…",
  connected: "Connected",
  disconnected: "Disconnected — attempting to reconnect",
};

useHead({
  link: [
    { rel: "preconnect", href: "https://fonts.googleapis.com" },
    { rel: "preconnect", href: "https://fonts.gstatic.com", crossorigin: "" },
    {
      rel: "stylesheet",
      href: "https://fonts.googleapis.com/css2?family=Cinzel:wght@400;600;700&family=Noto+Sans:wght@300;400;500;600&display=swap",
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
  lockLibrarian,
  unlockLibrarian,
  renameLibrarian,
  equipKeyPage,
  addCardToDeck,
  removeCardFromDeck,
} = useWebSocket();

// SessionPanel is hoisted into the global header so it is reachable from
// every scene (title, main/librarian manager, battle setup, battle).
const allies = computed(() => gameState.value?.allies ?? []);
const allyColors = computed(() => buildAllyColors(allies.value));
// Only surface the "Librarians" claim/release tab when there are actual
// battle units — otherwise it would render an empty list.
const showLibrarians = computed(
  () =>
    gameState.value?.scene === "battle" ||
    (gameState.value?.scene === "main" &&
      gameState.value.uiPhase === "BattleSetting"),
);

const isDebugOpen = ref(false);
const rawJson = computed(() =>
  isDebugOpen.value && gameState.value ? JSON.stringify(gameState.value, null, 2) : "—",
);
</script>

<template>
  <div class="app">
    <header class="topbar">
      <div class="topbar-left">
        <span class="brand-mark" aria-hidden="true" />
        <span class="title">PlayLoRWithMe</span>
      </div>

      <div class="topbar-right">
        <span
          class="conn-dot"
          :class="status"
          :title="statusTitle[status]"
          :aria-label="statusTitle[status]"
          role="status"
        />
        <SessionPanel
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

      <LibrarianManager
        v-else-if="gameState?.scene === 'main'"
        :state="gameState"
        :session="session"
        :players="players"
        :send-action="sendAction"
        :claim-unit="claimUnit"
        :release-unit="releaseUnit"
        :rename-player="renamePlayer"
        :lock-librarian="lockLibrarian"
        :unlock-librarian="unlockLibrarian"
        :rename-librarian="renameLibrarian"
        :equip-key-page="equipKeyPage"
        :add-card-to-deck="addCardToDeck"
        :remove-card-from-deck="removeCardFromDeck"
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

    <details class="debug-info" @toggle="isDebugOpen = ($event.target as HTMLDetailsElement).open">
      <summary><span class="chevron">▸</span>debug info</summary>
      <pre>{{ rawJson }}</pre>
    </details>
  </div>
</template>

<style>
/* ==========================================================================
   Global design tokens
   ==========================================================================

   Aesthetic target: Library of Ruina's in-game UI — near-black canvas with
   a very faint cool undertone, hairline gold rules, ornamental Cinzel
   display type, warm parchment body text. Deep blacks, not navy, not purple.
   ========================================================================== */
:root {
  /* ── Canvas: near-black with a faint cool undertone ──
     References the desaturated shadow of LoR's in-game backdrop. Saturation
     is kept very low so the gold/parchment accents remain the warm focal
     point; only the value ramps up between tiers. */
  --bg: #05070a;
  --bg-surface: #0a0d11;
  --bg-card: #0e1217;
  --bg-card-2: #131820;
  --bg-card-3: #1b2028;

  /* ── Borders ── */
  --border: #232934;
  --border-mid: #323944;
  --border-hi: #475060;
  /* faint gold hairline for ornamental dividers */
  --border-gold: rgba(201, 162, 39, 0.28);
  --border-gold-hi: rgba(232, 194, 71, 0.5);

  /* ── Primary accent: antique gold ── */
  --gold: #c9a227;
  --gold-dim: #7a6118;
  --gold-bright: #e8c247;
  --gold-ink: #2a2210;
  --gold-glow: rgba(201, 162, 39, 0.15);

  /* ── Combat red ── */
  --crimson: #8b1a1a;
  --crimson-hi: #c62828;
  --crimson-dim: #3d0a0a;

  /* ── Text: warm parchment hierarchy ── */
  --text-1: #e8dfc6;
  --text-2: #8b7f68;
  --text-3: #4a4338;

  /* ── State colors ── */
  --green: #4caf50;
  --green-hi: #2e7d32;
  --text-green: #81c784;
  --bg-green: #0a1a0a;
  --bg-green-2: #0c1e0c;
  --bg-green-3: #102010;
  --amber: #f0a020;
  --red-hi: #e53935;
  --text-red: #ef9a9a;
  --orange: #ff9800;
  --orange-dim: #a05000;
  --bg-gold: #1a1400;

  /* ── Info (card tokens, status chips) ── */
  --bg-info: #0d1a2e;
  --border-info: #3d5a80;
  --text-info: #90a4ae;
  --text-info-hi: #b0bec5;

  /* ── Rarity accents ── */
  --rarity-uncommon: #56a348;
  --rarity-rare: #4169c4;

  --blue-hi: #1976d2;
  --cyan: #4fc3f7;

  /* ── Typography scale (base 18px) ──
     Components using the scale should reference --fs-* tokens. Legacy rem
     values still scale up proportionally because of the increased base. */
  --fs-3xs: 0.68rem;   /* micro labels */
  --fs-2xs: 0.78rem;   /* small badges */
  --fs-xs:  0.86rem;   /* captions, chips */
  --fs-sm:  0.95rem;   /* secondary text */
  --fs-md:  1rem;      /* body */
  --fs-lg:  1.15rem;   /* emphasized body */
  --fs-xl:  1.4rem;    /* section headers */
  --fs-2xl: 1.75rem;   /* panel titles */
  --fs-3xl: 2.4rem;    /* display */

  /* ── Spacing scale ── */
  --sp-1: 0.25rem;
  --sp-2: 0.5rem;
  --sp-3: 0.75rem;
  --sp-4: 1rem;
  --sp-5: 1.5rem;
  --sp-6: 2rem;
  --sp-7: 3rem;

  /* ── Radii (LoR UI is sharp — keep radii subtle) ── */
  --radius-sm: 2px;
  --radius-md: 4px;
  --radius-lg: 6px;
  --radius-pill: 999px;

  /* ── Shadows ── */
  --shadow-sm: 0 1px 0 rgba(0, 0, 0, 0.4);
  --shadow-md: 0 2px 10px rgba(0, 0, 0, 0.55);
  --shadow-lg: 0 6px 24px rgba(0, 0, 0, 0.65);
  --shadow-gold: 0 0 0 1px rgba(201, 162, 39, 0.18),
    0 0 18px rgba(201, 162, 39, 0.08);
  --shadow-inset: inset 0 1px 0 rgba(255, 255, 255, 0.03);

  /* ── Motion ── */
  --ease-out: cubic-bezier(0.22, 1, 0.36, 1);
  --duration-fast: 0.12s;
  --duration-base: 0.18s;
  --duration-slow: 0.3s;

  /* ── Fonts ── */
  --font-display: "Cinzel", "Palatino Linotype", serif;
  --font-body: "Noto Sans", system-ui, sans-serif;
  --font-mono: "Courier New", Courier, monospace;

  /* Flat-top hexagon: wider than tall, pointy sides */
  --hex: polygon(25% 0%, 75% 0%, 100% 50%, 75% 100%, 25% 100%, 0% 50%);

  /* ── Clash/stagger ── */
  --health: #e56031;
  --health-bar: #ed372c;
  --stagger: #f0f464;
  --stagger-bar: #e9f762;

  /* Clash-specific colors (enemy -> ally: incoming, enemy <- ally: outgoing) */
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

html {
  /* 18px base — raises the floor for the entire rem scale without requiring
     every component to change individually. Components that were previously
     at 0.5–0.7rem now sit in a more readable range automatically. */
  font-size: 18px;
}

body {
  font-family: var(--font-body);
  color: var(--text-1);
  min-height: 100dvh;
  -webkit-font-smoothing: antialiased;
  /* Very faint gold pool at the top of the canvas — candlelight in a
     darkened hall. Intensity kept low so the near-black base dominates. */
  background-color: var(--bg);
  background-image:
    radial-gradient(
      ellipse 80% 60% at 50% 0%,
      rgba(201, 162, 39, 0.03),
      transparent 70%
    );
  background-attachment: fixed;
}

::-webkit-scrollbar {
  width: 6px;
  height: 6px;
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
  font-size: var(--fs-2xs);
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
  padding: var(--sp-2);
  gap: var(--sp-2);
}

/* ── Top ribbon ──
   Hairline gold top border reinforces the book-ribbon feel. The inset
   shadow on the bottom creates a subtle page-edge lift without adding
   heavy drop shadows. */
.topbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--sp-2) var(--sp-4);
  background: var(--bg-surface);
  border: 1px solid var(--border-mid);
  border-top: 2px solid var(--gold-dim);
  box-shadow: var(--shadow-inset), var(--shadow-md);
}

.topbar-left {
  display: flex;
  align-items: center;
  gap: var(--sp-3);
}

/* A small diamond/lozenge mark next to the title — evokes the faceted
   ornaments used in LoR's menu chrome. CSS-only, no asset required. */
.brand-mark {
  width: 10px;
  height: 10px;
  background: var(--gold);
  transform: rotate(45deg);
  box-shadow: 0 0 8px var(--gold-glow);
}

.title {
  font-family: var(--font-display);
  color: var(--gold);
  font-size: var(--fs-lg);
  font-weight: 700;
  letter-spacing: 0.22em;
  text-transform: uppercase;
}

.topbar-right {
  display: flex;
  align-items: center;
  gap: var(--sp-4);
}

/* ── Connection dot ──
   Replaces the old "Connecting…/Connected/Disconnected" text label. A
   single solid circle with a tooltip is more glanceable than a thrashing
   text state and avoids the visual noise of a pulsing animation. */
.conn-dot {
  width: 11px;
  height: 11px;
  border-radius: 50%;
  flex-shrink: 0;
  transition: background var(--duration-base) var(--ease-out),
    box-shadow var(--duration-base) var(--ease-out);
}
.conn-dot.connecting {
  background: var(--amber);
  box-shadow: 0 0 0 2px rgba(240, 160, 32, 0.18);
}
.conn-dot.connected {
  background: var(--green);
  box-shadow: 0 0 0 2px rgba(76, 175, 80, 0.15);
}
.conn-dot.disconnected {
  background: var(--crimson-hi);
  box-shadow: 0 0 0 2px rgba(198, 40, 40, 0.18);
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
  gap: var(--sp-3);
}
.scene-name {
  font-family: var(--font-display);
  font-size: var(--fs-3xl);
  color: var(--gold);
  text-transform: uppercase;
  letter-spacing: 0.28em;
  font-weight: 700;
  text-shadow: 0 0 24px var(--gold-glow);
}
.scene-sub {
  font-size: var(--fs-md);
  color: var(--text-2);
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.debug-info {
  background: var(--bg-surface);
  border: 1px solid var(--border);
  padding: var(--sp-2) var(--sp-3);
}
.chevron {
  font-size: var(--fs-2xs);
  color: var(--text-3);
  display: inline-block;
  margin-right: 0.3em;
  transition: transform var(--duration-base) var(--ease-out);
}
.debug-info summary {
  cursor: pointer;
  color: var(--text-3);
  font-size: var(--fs-2xs);
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
  margin-top: var(--sp-2);
  font-size: var(--fs-3xs);
  color: var(--text-3);
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 400px;
  overflow-y: auto;
  font-family: var(--font-mono);
}

/* ── Responsive: tighter on small screens ── */
@media (max-width: 600px) {
  .app {
    padding: var(--sp-1);
    gap: var(--sp-1);
  }
  .topbar {
    padding: var(--sp-2) var(--sp-3);
  }
  .title {
    font-size: var(--fs-md);
    letter-spacing: 0.14em;
  }
  .topbar-right {
    gap: var(--sp-3);
  }
}
</style>
