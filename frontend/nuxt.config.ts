// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: "2025-07-15",
  devtools: { enabled: true },

  nitro: {
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
      "/assets": {
        target: "http://localhost:8080/assets",
        changeOrigin: true,
        ws: false,
      },
    },
  },
});
