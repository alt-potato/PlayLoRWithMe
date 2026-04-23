/**
 * Regenerates schema/gamestate.schema.json from the Zod schemas defined in
 * app/types/game.ts. Wired into the frontend's pre-build, pre-generate, and
 * pre-test hooks so a stale schema can never ship.
 *
 * The canonical output lives at the repo root (schema/), not under frontend/,
 * because it is a shared-contract artifact between the mod and the frontend.
 *
 * Requires Node >= 23.6 (native TypeScript stripping). Nuxt 4 already ships
 * on this baseline.
 */

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { buildJsonSchema, serializeJsonSchema } from "./schemaGen.ts";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const SCHEMA_OUT = path.resolve(__dirname, "../../schema/gamestate.schema.json");

await fs.writeFile(SCHEMA_OUT, serializeJsonSchema(buildJsonSchema()), "utf8");
console.log(`[generate-schema] Wrote ${path.relative(process.cwd(), SCHEMA_OUT)}`);
