/**
 * Shared helpers for building and serializing the canonical
 * `schema/gamestate.schema.json` artifact. Imported by both the CLI
 * `generate-schema.ts` (which writes to disk) and the drift test (which
 * compares in-memory output against the committed file).
 */

import { z } from "zod/mini";

import {
  GameStateSchema,
  ServerMessageSchema,
  ClientActionSchema,
} from "../app/types/game.ts";

/** Build the combined JSON Schema 2020-12 object from the Zod root schemas. */
export function buildJsonSchema(): unknown {
  const combined = z.object({
    gameState: GameStateSchema,
    serverMessage: ServerMessageSchema,
    clientAction: ClientActionSchema,
  });
  return z.toJSONSchema(combined, { target: "draft-2020-12" });
}

/** Canonical on-disk representation: 2-space pretty, trailing newline. */
export function serializeJsonSchema(schema: unknown): string {
  return JSON.stringify(schema, null, 2) + "\n";
}
