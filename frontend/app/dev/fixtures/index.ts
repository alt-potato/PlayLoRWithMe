/**
 * Registry of mock fixture loaders keyed by fixture name. Each loader returns
 * a full `GameState` snapshot. JSON files are statically imported so Nuxt
 * inlines them at build time and the dev bundle participates in HMR.
 *
 * Populated by the `dev-mock-backend` change; the entire file tree-shakes
 * out of production because every call-site is gated by `import.meta.dev`.
 */

import type { GameState } from "../../types/game";

import battleSampler from "./battle-sampler.json";
import mainLibrarian from "./main-librarian.json";
import battleSetting from "./battle-setting.json";
import emotionUpgrade from "./emotion-upgrade.json";

// json-import types do not flow into z.infer<> shapes (enums widen to string),
// so a double-cast is required. Schema conformance is enforced by the test
// at `fixtures.test.ts`, which parses every registered fixture.
export const FIXTURE_LOADERS: Record<string, () => GameState> = {
  "battle-sampler": () => battleSampler as unknown as GameState,
  "main-librarian": () => mainLibrarian as unknown as GameState,
  "battle-setting": () => battleSetting as unknown as GameState,
  "emotion-upgrade": () => emotionUpgrade as unknown as GameState,
};
