/**
 * Drift test for the canonical JSON Schema artifact.
 *
 * Rebuilds the schema in-memory from the Zod sources and compares it against
 * the committed `schema/gamestate.schema.json`. Any divergence (new field in
 * the Zod source that was not regenerated into the artifact, or vice versa)
 * fails the test with the exact diff so the developer knows to run
 * `npm run generate-schema` and commit.
 */

import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { buildJsonSchema, serializeJsonSchema } from "./schemaGen.ts";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const SCHEMA_PATH = path.resolve(__dirname, "../../schema/gamestate.schema.json");

describe("canonical JSON schema artifact", () => {
  it("matches the committed schema/gamestate.schema.json", () => {
    const committed = fs.readFileSync(SCHEMA_PATH, "utf8");
    const rebuilt = serializeJsonSchema(buildJsonSchema());
    if (committed !== rebuilt) {
      throw new Error(
        `schema/gamestate.schema.json is stale. Run \`npm run generate-schema\` and commit the result.\n\n` +
        `--- committed (first mismatch context) ---\n` +
        diffPreview(committed, rebuilt),
      );
    }
    expect(committed).toBe(rebuilt);
  });
});

// Small visual aid: show the first 3 differing lines on either side.
function diffPreview(a: string, b: string): string {
  const aLines = a.split("\n");
  const bLines = b.split("\n");
  const max = Math.max(aLines.length, bLines.length);
  const out: string[] = [];
  let shown = 0;
  for (let i = 0; i < max && shown < 6; i++) {
    if (aLines[i] !== bLines[i]) {
      out.push(`  line ${i + 1}:`);
      out.push(`    committed: ${JSON.stringify(aLines[i])}`);
      out.push(`    rebuilt:   ${JSON.stringify(bLines[i])}`);
      shown++;
    }
  }
  return out.join("\n");
}
