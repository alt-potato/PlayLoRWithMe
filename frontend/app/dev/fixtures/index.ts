/**
 * Registry of mock fixture loaders keyed by fixture name. Each loader returns
 * a full `GameState` snapshot. JSON files are statically imported so Nuxt
 * inlines them at build time and the dev bundle participates in HMR.
 *
 * Populated by the `dev-mock-backend` change; the entire file tree-shakes
 * out of production because every call-site is gated by `import.meta.dev`.
 */

import { GameStateSchema, type GameState } from "../../types/game";

import battleSampler from "./battle-sampler.json";
import mainLibrarian from "./main-librarian.json";
import battleSetting from "./battle-setting.json";
import emotionUpgrade from "./emotion-upgrade.json";

// Each loader runs the JSON through `GameStateSchema.parse` so a fixture that
// drifts from the wire contract throws with a Zod path at load time rather
// than producing a silent shape mismatch deep inside a component render. The
// fixture-parse test in `index.test.ts` locks this contract for new fixtures.
export const FIXTURE_LOADERS: Record<string, () => GameState> = {
  "battle-sampler": () => GameStateSchema.parse(battleSampler),
  "main-librarian": () => GameStateSchema.parse(mainLibrarian),
  "battle-setting": () => GameStateSchema.parse(battleSetting),
  "emotion-upgrade": () => GameStateSchema.parse(emotionUpgrade),
};
