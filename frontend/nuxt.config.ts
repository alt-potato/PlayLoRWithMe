// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: "2026-03-12",
  devtools: { enabled: true },

  // The app is a SPA that bootstraps from a live WebSocket — there is no
  // meaningful HTML to render before the first server message arrives.
  // SSR would just produce an empty shell anyway, while dragging in browser
  // globals (localStorage, matchMedia) at module-init time and surfacing
  // hard-to-debug errors during `npm run dev`. `nuxt generate` still emits
  // the static SPA shell that the mod's HTTP server serves as wwwroot/.
  ssr: false,

  vite: {
    // Pre-bundle runtime deps Vite would otherwise discover mid-session and
    // trigger a full-page reload for. zod lands here because types/game.ts
    // imports it for wire-contract validation; the devtools modules are
    // pulled in by Nuxt devtools when any dev-only page opens them.
    optimizeDeps: {
      include: [
        "@vue/devtools-core",
        "@vue/devtools-kit",
        "zod",
      ],
    },
  },

  nitro: {
    // Nuxt nitro websocket proxy does not work T-T
    experimental: {
      websocket: true,
    },
    devProxy: {
      "/action": {
        target: "http://localhost:8080/action",
        changeOrigin: true,
        ws: false,
      },
      "/events": {
        target: "http://localhost:8080/events",
        changeOrigin: true,
        ws: false,
      },
      "/state": {
        target: "http://localhost:8080/state",
        changeOrigin: true,
        ws: false,
      },
      "/ws": {
        target: "ws://localhost:8080",
        changeOrigin: true,
        ws: true,
      },
      "/assets": {
        target: "http://localhost:8080/assets",
        changeOrigin: true,
        ws: false,
      },
    },
  },
});
