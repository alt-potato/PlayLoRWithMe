import type { Ref } from "vue";

/**
 * Toggle membership of `value` in a reactive Set ref. If present, removes it;
 * otherwise adds it. Always reassigns .value with a new Set so Vue reactivity fires.
 */
export function toggleSet<T>(ref: Ref<Set<T>>, value: T): void {
  const next = new Set(ref.value);
  if (next.has(value)) next.delete(value);
  else next.add(value);
  ref.value = next;
}
