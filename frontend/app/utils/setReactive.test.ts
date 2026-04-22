import { ref } from "vue";
import { describe, it, expect } from "vitest";
import { toggleSet } from "./setReactive";

describe("toggleSet", () => {
  it("adds a value not present in the set", () => {
    const r = ref(new Set<string>(["a"]));
    toggleSet(r, "b");
    expect([...r.value]).toEqual(["a", "b"]);
  });

  it("removes a value present in the set", () => {
    const r = ref(new Set<string>(["a", "b"]));
    toggleSet(r, "a");
    expect([...r.value]).toEqual(["b"]);
  });

  it("assigns a new Set instance so reactivity fires", () => {
    // The whole point of the helper — Vue's reactivity tracks Set identity,
    // not internal mutation, so the helper must reassign .value.
    const before = new Set<string>(["a"]);
    const r = ref(before);
    toggleSet(r, "b");
    expect(r.value).not.toBe(before);
  });
});
