import type { InjectionKey, Ref } from "vue";

/**
 * Injection key for the reactive assetsReady flag from the game state.
 * Provided by app.vue; consumed by AppearancePreview to clear cached 404s
 * when the server finishes extracting appearance sprites.
 */
export const ASSETS_READY: InjectionKey<Ref<boolean>> = Symbol("AssetsReady");
