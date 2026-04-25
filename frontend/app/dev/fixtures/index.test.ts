/**
 * Locks the contract for FIXTURE_LOADERS itself: every registered loader must
 * return without throwing. Because each loader internally invokes
 * `GameStateSchema.parse`, this catches new fixture entries that ship without
 * being verified against the schema (the existing `fixtures.test.ts`
 * `safeParse`s the same payloads, but this asserts the loader API contract
 * — useful when adding fixtures that wrap the JSON in pre-processing logic).
 */

import { describe, it, expect } from "vitest";
import { FIXTURE_LOADERS } from "./index";

describe("FIXTURE_LOADERS", () => {
  for (const [name, loader] of Object.entries(FIXTURE_LOADERS)) {
    it(`loader "${name}" returns without throwing`, () => {
      expect(() => loader()).not.toThrow();
    });
  }
});
