// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: "2026-03-12",
  devtools: { enabled: true },

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
