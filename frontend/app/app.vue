<script setup lang="ts">
import { LIBRARIAN_ACTIONS } from "~/composables/useLibrarianActions";
import { ASSETS_READY } from "~/composables/useAssetsReady";
import { FACE_CANVAS_DIMS } from "~/composables/useFaceCanvasDims";
import { STATE_GENERATION } from "~/composables/useStateGeneration";

// Vite replaces `import.meta.dev` with a literal boolean at build time; in
// production the ternary collapses to `null` and the `import()` call lives
// in a dead branch, so Rollup never emits the dev-picker chunk and every
// module reachable through ~/dev/ tree-shakes out of the production bundle.
const DevPicker = import.meta.dev
  ? defineAsyncComponent(() => import("./dev/DevFixturePicker.vue"))
  : null;

// DiagnosticPanel ships in every build (mod's HTTP server only serves the
// production SPA, so a build-flag gate would never enable it). Toggled at
// runtime via `?debug=1` (persisted in localStorage) — see debugEnabled
// below. defineAsyncComponent keeps the chunk lazy: the module isn't loaded
// until the panel actually renders, so no runtime cost when toggled off.
const DiagnosticPanel = defineAsyncComponent(
  () => import("./dev/DiagnosticPanel.vue"),
);

type ConnectionStatus = "connecting" | "connected" | "disconnected";

const statusTitle: Record<ConnectionStatus, string> = {
  connecting: "Connecting to server…",
  connected: "Connected",
  disconnected: "Disconnected — attempting to reconnect",
};

// Fonts are self-hosted under /fonts/ (see public/fonts/) so the app stays
// fully usable on a LAN-only host with no internet route to fonts.gstatic.com.
// The @font-face declarations live in the global <style> block below.

const {
  gameState,
  session,
  status,
  players,
  stateGeneration,
  inflightCount,
  lastSeqRef,
  resyncCount,
  lastResyncAt,
  sendAction,
  claimUnit,
  releaseUnit,
  renamePlayer,
  lockLibrarian,
  unlockLibrarian,
  renameLibrarian,
  equipKeyPage,
  unequipKeyPage,
  addCardToDeck,
  removeCardFromDeck,
  equipSourceBook,
  unequipSourceBook,
  attributePassive,
  removeAttributedPassive,
} = useWebSocket();

// Runtime debug-tools flag — opt-in per page load via `?debug=1` in the URL.
// Intentionally NOT persisted, so debug tooling is never on by accident on
// a normal play session. Survives the production build; the mod's HTTP
// server only ships the static SPA, so `import.meta.dev` would never be
// true at play-time. Drives the diagnostic panel + spam harness on demand.
const debugEnabled = ref(false);
onMounted(() => {
  const params = new URLSearchParams(window.location.search);
  debugEnabled.value = params.get("debug") === "1";

  if (debugEnabled.value) {
    import("~/dev/useSpamHarness").then(({ installSpamHarness }) => {
      installSpamHarness({
        gameState,
        session,
        players,
        addCardToDeck,
        removeCardFromDeck,
      });
    });
  }
});

// Close the connection panel when clicking outside of it.
const handleDocumentClick = (e: PointerEvent) => {
  if (connPanelOpen.value) {
    const wrapper = document.querySelector(".conn-status-inline") as HTMLElement;
    if (wrapper && !wrapper.contains(e.target as Node)) {
      connPanelOpen.value = false;
    }
  }
};
document.addEventListener("pointerdown", handleDocumentClick);
onBeforeUnmount(() => {
  document.removeEventListener("pointerdown", handleDocumentClick);
});

// Provide librarian-specific action callbacks via injection so that
// LibrarianManager and its descendants can access them without prop drilling.
provide(LIBRARIAN_ACTIONS, {
  sendAction,
  lockLibrarian,
  unlockLibrarian,
  renameLibrarian,
  equipKeyPage,
  unequipKeyPage,
  addCardToDeck,
  removeCardFromDeck,
  equipSourceBook,
  unequipSourceBook,
  attributePassive,
  removeAttributedPassive,
});

// Provide assetsReady so descendants (e.g. AppearancePreview) can react when
// the server finishes extracting appearance sprites — clearing cached 404s.
const assetsReady = computed(() => gameState.value?.assetsReady === true);
provide(ASSETS_READY, assetsReady);

// Provide the shared face/hair canvas dimensions so AppearancePreview can
// compute its head-tilt transform origin synchronously across remounts.
const faceCanvasDims = computed<{ w: number; h: number } | null>(() => {
  const co = gameState.value?.customizeOptions;
  return co?.faceCanvasW && co?.faceCanvasH
    ? { w: co.faceCanvasW, h: co.faceCanvasH }
    : null;
});
provide(FACE_CANVAS_DIMS, faceCanvasDims);

// Provide stateGeneration so descendants holding optimistic UI state (e.g.
// DeckTab's pending-add/remove tiles) can drop phantoms when a fresh full
// state replaces the previous one across a connection boundary (initial
// connect, reconnect). Mid-session resyncs no longer bump this — they are
// reconciled by per-feature diff watchers instead, so a brief WebSocket gap
// during play doesn't wipe the user's queued tile taps.
provide(STATE_GENERATION, stateGeneration);


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

// Connection panel state.
const connPanelOpen = ref(false);
</script>

<template>
  <div class="app">
    <header class="topbar">
      <div class="topbar-left">
        <span class="brand-mark" aria-hidden="true" />
        <span class="title">PlayLoRWithMe</span>
      </div>

      <div class="topbar-right">
        <div class="conn-status-inline">
          <button
            class="conn-panel"
            :class="[status, { open: connPanelOpen }]"
            @click="connPanelOpen = !connPanelOpen"
            :title="statusTitle[status]"
            :aria-label="statusTitle[status]"
            :aria-expanded="connPanelOpen"
          >
            <span class="conn-label">{{ statusTitle[status] }}</span>
            <span class="conn-dot" aria-hidden="true" />
          </button>
        </div>
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
      <LazyBattleSettingView
        v-if="
          gameState?.scene === 'main' && gameState.uiPhase === 'BattleSetting'
        "
        :state="gameState"
        :session="session"
        :players="players"
        :send-action="sendAction"
        :claim-unit="claimUnit"
        :release-unit="releaseUnit"
      />

      <LazyBattleStage
        v-else-if="gameState?.scene === 'battle'"
        :state="gameState"
        :session="session"
        :players="players"
        :send-action="sendAction"
        :claim-unit="claimUnit"
        :release-unit="releaseUnit"
        :rename-player="renamePlayer"
      />

      <LazyLibrarianManager
        v-else-if="gameState?.scene === 'main'"
        :state="gameState"
        :session="session"
        :players="players"
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

    <details
      v-if="debugEnabled"
      class="debug-info"
      @toggle="isDebugOpen = ($event.target as HTMLDetailsElement).open"
    >
      <summary><span class="chevron">▸</span>debug info</summary>
      <pre>{{ rawJson }}</pre>
    </details>

    <ClientOnly v-if="DevPicker">
      <component :is="DevPicker" />
    </ClientOnly>

    <ClientOnly v-if="debugEnabled">
      <component
        :is="DiagnosticPanel"
        :status="status"
        :inflight-count="inflightCount"
        :last-seq="lastSeqRef"
        :resync-count="resyncCount"
        :last-resync-at="lastResyncAt"
      />
    </ClientOnly>
  </div>
</template>

<style>
/* ==========================================================================
   Self-hosted fonts
   ==========================================================================

   Cinzel and Noto Sans served from the mod's HTTP server (public/fonts/) so
   the app stays fully usable when the host machine has no route to
   fonts.gstatic.com (LAN-only co-op, captive portal, etc.). Both are variable
   fonts; each subset is one woff2 file shared across the weight range, so
   four files cover the entire weight palette the UI uses.
   ========================================================================== */
@font-face {
  font-family: "Cinzel";
  font-style: normal;
  font-weight: 400 700;
  font-display: swap;
  src: url("/fonts/cinzel-latin.woff2") format("woff2");
  unicode-range: U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD;
}
@font-face {
  font-family: "Cinzel";
  font-style: normal;
  font-weight: 400 700;
  font-display: swap;
  src: url("/fonts/cinzel-latin-ext.woff2") format("woff2");
  unicode-range: U+0100-02BA, U+02BD-02C5, U+02C7-02CC, U+02CE-02D7, U+02DD-02FF, U+0304, U+0308, U+0329, U+1D00-1DBF, U+1E00-1E9F, U+1EF2-1EFF, U+2020, U+20A0-20AB, U+20AD-20C0, U+2113, U+2C60-2C7F, U+A720-A7FF;
}
@font-face {
  font-family: "Noto Sans";
  font-style: normal;
  font-weight: 300 600;
  font-stretch: 100%;
  font-display: swap;
  src: url("/fonts/noto-sans-latin.woff2") format("woff2");
  unicode-range: U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD;
}
@font-face {
  font-family: "Noto Sans";
  font-style: normal;
  font-weight: 300 600;
  font-stretch: 100%;
  font-display: swap;
  src: url("/fonts/noto-sans-latin-ext.woff2") format("woff2");
  unicode-range: U+0100-02BA, U+02BD-02C5, U+02C7-02CC, U+02CE-02D7, U+02DD-02FF, U+0304, U+0308, U+0329, U+1D00-1DBF, U+1E00-1E9F, U+1EF2-1EFF, U+2020, U+20A0-20AB, U+20AD-20C0, U+2113, U+2C60-2C7F, U+A720-A7FF;
}

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
  /* solid buff borders for status-chip outlines */
  --border-gold-buff: #4a2800;
  --border-crimson-buff: #5c1a1a;

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

  /* Deeper crimson surface for ego-tag and error-banner backgrounds */
  --bg-crimson-deep: #1a0505;

  /* ── Text: warm parchment hierarchy ── */
  --text-1: #e8dfc6;
  --text-2: #8b7f68;
  --text-3: #4a4338;

  /* ── Die-type description text tints ── */
  --text-atk: #f0c2c2;      /* light red, mirrors in-game atk card text */
  --text-def: #c2d8f0;      /* light blue, mirrors in-game def card text */
  --text-standby: #f0d8a0;  /* light gold, mirrors in-game counter card text */

  /* Page-level reading text (warmer/brighter than --text-1 body) */
  --text-page: #f5efde;

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

  /* ── Gold panel/slot deeper shades ── */
  --bg-gold-deep: #0d0d00;    /* ab-header background in EmotionUpgradePicker */
  --bg-gold-hover: #141000;   /* available slot hover fill in DieRow */
  --bg-gold-beacon: #3a2c00;  /* hex-beckon animation midpoint in DieRow */
  --text-gold-deep: #4a3800;  /* idle available-slot text colour in DieRow */

  /* ── Mass-target damage badge ── */
  --bg-mass: #2a0e00;      /* fill for the MASS badge in TargetPicker */
  --border-mass: #8b3500;  /* border for the MASS badge in TargetPicker */
  --text-mass: #ff7043;    /* text for the MASS badge in TargetPicker (intentionally redder than --orange) */

  /* ── Info (card tokens, status chips) ── */
  --bg-info: #0d1a2e;
  --border-info: #3d5a80;
  --text-info: #90a4ae;
  --text-info-hi: #b0bec5;

  /* ── Rarity accents ── */
  --rarity-common: #2e7d32;
  --rarity-uncommon: #1565c0;
  --rarity-rare: #6a1b9a;
  --rarity-unique: #c9a227;
  --rarity-special: #c62828;

  /* ── Rarity surface overrides ──
     Sibling vars consumed by rarity-styled surfaces (border, range glyph,
     ability text, bracketed keyword). Components set the inline `--rarity-*`
     value on the card root from a per-rarity class lookup OR from a
     payload-supplied hex override (the CustomRarityUtil soft-dep). These
     defaults match the pre-change visual rendering — gold-on-dark range
     glyphs, default text-2 description text, gold-bright keyword highlights
     — so an absent override leaves the surface looking identical. */
  --rarity-range-icon-color: var(--gold);
  --rarity-ability-color: var(--text-2);
  --rarity-keyword-color: var(--gold-bright);

  /* ── Speed-die faction fills ──
     Defaults approximate vanilla LoR; the mod overrides these at runtime via
     theme.factionDieColors sampled from SpeedDiceUI.Refs. Keep the defaults
     in sync with the in-game prefab so a probe failure stays unobtrusive. */
  --die-ally-fill: #3aaad8;
  --die-enemy-fill: #d83a6d;

  --blue-hi: #1976d2;
  --cyan: #4fc3f7;

  /* ── Typography scale (base 18px) ──
     All components reference these tokens — never hard-code rem/px font
     sizes. --fs-4xs is the floor; any size that would fall below it snaps
     up to --fs-4xs. The floor is intentionally tunable: raise it if the
     dense battle UI proves too small in practice. */
  --fs-4xs: 0.62rem;   /* dense micro-labels (battle/unit UI) — scale floor */
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

  /* ── Combat hex backgrounds ── */
  --bg-incoming: #2a0a0a;        /* incoming die outer hex fill */
  --bg-incoming-hover: #7a1010;  /* incoming die outer hex hover fill */
  --bg-clash: #3d2e00;           /* clash die outer hex fill */
  --bg-clash-inner-hover: #261c00; /* clash die inner hex on hover (not a sibling of --bg-clash; the outer hover uses --gold-dim) */
  --bg-stagger: #220808;         /* staggered die inner hex fill */

  /* Broken die inner hex fill (distinct from --crimson-dim outer) */
  --bg-broken: #230808;
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

/* ── Connection status: inline expanding panel ──
   When closed, only the dot is visible — the button has no padding and no
   background, so the panel itself is imperceptible. When open, the button
   expands leftward into a pill containing [label, dot]. The dot's right
   edge is anchored to the container's right edge in both states: `right`
   and `padding-right` transition in lockstep so the offsets cancel and the
   dot never shifts. The button is absolutely positioned so the expanding
   panel overlays adjacent topbar items (e.g. SessionPanel) rather than
   shifting them. The label has a generous max-width so the full status
   string is never truncated; max-width is animated for the open/close
   reveal only. */
.conn-status-inline {
  position: relative;
  display: inline-block;
  width: 11px;
  height: 11px;
}

.conn-panel {
  position: absolute;
  top: 50%;
  right: 0;
  transform: translateY(-50%);
  display: inline-flex;
  align-items: center;
  padding: 0;
  background: transparent;
  border: none;
  cursor: pointer;
  transition: background var(--duration-base) var(--ease-out),
    box-shadow var(--duration-base) var(--ease-out),
    padding var(--duration-base) var(--ease-out),
    right var(--duration-base) var(--ease-out);
}

.conn-panel.open {
  right: calc(-1 * var(--sp-2));
  padding: var(--sp-1) var(--sp-2) var(--sp-1) var(--sp-3);
  background: var(--bg-card);
  /* inset box-shadow for the faux-border so adding it does not shift
     interior content (border would, even with box-sizing: border-box). */
  box-shadow: inset 0 0 0 1px var(--border), var(--shadow-md);
  z-index: 100;
}

.conn-label {
  display: inline-block;
  max-width: 0;
  margin-right: 0;
  overflow: hidden;
  white-space: nowrap;
  font-size: var(--fs-xs);
  font-family: var(--font-body);
  color: var(--text-1);
  transition: max-width var(--duration-base) var(--ease-out),
    margin-right var(--duration-base) var(--ease-out);
}

.conn-panel.open .conn-label {
  /* large ceiling so the label is sized by its natural content width;
     max-width is only animated for the reveal. */
  max-width: 400px;
  margin-right: var(--sp-2);
}

.conn-dot {
  display: inline-block;
  width: 11px;
  height: 11px;
  border-radius: 50%;
  flex-shrink: 0;
  transition: background var(--duration-base) var(--ease-out),
    box-shadow var(--duration-base) var(--ease-out);
}
.conn-panel.connecting .conn-dot {
  background: var(--amber);
  box-shadow: 0 0 0 2px rgba(240, 160, 32, 0.18);
}
.conn-panel.connected .conn-dot {
  background: var(--green);
  box-shadow: 0 0 0 2px rgba(76, 175, 80, 0.15);
}
.conn-panel.disconnected .conn-dot {
  background: var(--crimson-hi);
  box-shadow: 0 0 0 2px rgba(198, 40, 40, 0.18);
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





