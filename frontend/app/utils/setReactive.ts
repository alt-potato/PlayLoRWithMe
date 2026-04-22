import type { Ref } from "vue";

/**
 * Toggles `value` in a reactive Set ref.
 *
 * Reassigns .value with a new Set rather than mutating in place: Vue's
 * reactivity tracks Set identity, not internal mutation, so `ref.value.add(x)`
 * alone would not trigger dependent re-renders.
 */
export function toggleSet<T>(ref: Ref<Set<T>>, value: T): void {
  const next = new Set(ref.value);
  if (next.has(value)) next.delete(value);
  else next.add(value);
  ref.value = next;
}
