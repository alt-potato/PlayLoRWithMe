/**
 * Parses every registered mock fixture through `GameStateSchema`. A failure
 * here means the fixture has drifted from the wire contract — fix the JSON
 * (preferred) or widen the schema (only when the live mod started sending
 * the new shape).
 */

import { describe, it, expect } from "vitest";
import { z } from "zod/mini";
import { FIXTURE_LOADERS } from "./index";
import { GameStateSchema } from "../../types/game";

describe("mock fixtures", () => {
  for (const [name, loader] of Object.entries(FIXTURE_LOADERS)) {
    it(`"${name}" parses against GameStateSchema`, () => {
      const result = z.safeParse(GameStateSchema, loader());
      if (!result.success) {
        throw new Error(
          `Fixture "${name}" violates GameStateSchema:\n` +
          JSON.stringify(result.error.issues, null, 2),
        );
      }
      expect(result.success).toBe(true);
    });
  }
});
