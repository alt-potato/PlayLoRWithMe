/**
 * Parses every case in `schema/reference-state.json` through `GameStateSchema`.
 * Fails the test with a Zod error path on any violation. The fixture is hand-
 * curated; when the schema tightens (a new required field, a literal added
 * to an enum), the fixture is the first thing that fails — re-capture from a
 * live mod session and update.
 */

import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { GameStateSchema } from "../app/types/game.ts";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const FIXTURE_PATH = path.resolve(__dirname, "../../schema/reference-state.json");

interface ReferenceFixture {
  cases: Record<string, unknown>;
}

const fixture = JSON.parse(fs.readFileSync(FIXTURE_PATH, "utf8")) as ReferenceFixture;

describe("schema/reference-state.json", () => {
  for (const [name, payload] of Object.entries(fixture.cases)) {
    it(`case "${name}" parses against GameStateSchema`, () => {
      const result = GameStateSchema.safeParse(payload);
      if (!result.success) {
        const formatted = JSON.stringify(result.error.issues, null, 2);
        throw new Error(
          `Reference fixture case "${name}" violates GameStateSchema.\n` +
          `Re-capture the fixture from a live mod session if the schema tightened.\n\n` +
          formatted,
        );
      }
      expect(result.success).toBe(true);
    });
  }
});
