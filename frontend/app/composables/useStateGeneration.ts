import type { InjectionKey, Ref } from "vue";

/**
 * Injection key for a counter that bumps each time the client receives a
 * fresh full-state payload (initial connect, reconnect, or resync). Provided
 * by app.vue; consumed by surfaces that hold optimistic UI state and need
 * to discard it when a fresh state replaces the previous one — any pending
 * client-side edits would be phantoms against the new authoritative state.
 */
export const STATE_GENERATION: InjectionKey<Ref<number>> = Symbol("StateGeneration");
