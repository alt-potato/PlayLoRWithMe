## Context

The wire contract between the mod and the frontend is a large, nested JSON shape. `types/game.ts` describes it well on the frontend, but there is no machine-checkable artifact that either side must respect. Current constraints shape the design:

- **C# side** uses a hand-written `JsonWriter` (`mod/JsonWriter.cs`, ~4.5KB) and has no DTO layer — `GameStateSerializer.cs` writes field by field. The mod DLL is ~152KB total, and the project charter avoids NuGet dependencies (no `Newtonsoft.Json`, no `System.Text.Json` back-port).
- **Frontend side** already carries comprehensive hand-written types in `types/game.ts`. Consumers (`useWebSocket`, every Vue component, the battle context) import concrete names like `GameState`, `AllyUnit`, `Card`.
- **No C# tests exist.** `npm test` (vitest) is the only automated test runner in the repo. Any CI validation needs to run inside the frontend toolchain.
- **Build pipeline:** `cd mod && dotnet build` runs `npm run generate` via AfterBuild. Schema export needs to hook into that so a stale `schema.json` can't ship.

## Goals / Non-Goals

**Goals:**

- One canonical wire-contract artifact (`schema.json`) that both sides agree is authoritative.
- Zero mod-DLL size impact (no runtime validation lib in the C# mod).
- Consumer-facing TS types (`GameState`, `AllyUnit`, …) are unchanged — the migration is internal to `types/game.ts`.
- Dev-mode runtime validation of incoming WebSocket payloads so drift is visible in DevTools during development.
- CI failure when `schema.json` drifts from the Zod source, or when the reference fixture violates the schema.

**Non-Goals:**

- **Generated C# DTOs / serializer refactor.** Out of scope here; deferred to a follow-up proposal once the schema artifact exists to generate from.
- **Runtime validation in the mod.** Would require `Newtonsoft.Json` or equivalent (~700KB, ~6× the current mod DLL); rejected on size grounds. The frontend is the validation point of record.
- **Replacing `JsonWriter.cs` or `DeltaEngine.cs`.** They are fit for purpose and not in conflict with a schema contract.
- **Full-state validation in production.** Parsing every WebSocket frame through Zod in prod adds CPU cost for no user-visible benefit. Dev-only.
- **Breaking changes to `GameState`.** Zod schemas must infer to the same structural shape consumers rely on today.

## Decisions

### 1. Authoring source: Zod

`frontend/app/types/game.ts` becomes:

```ts
import { z } from "zod";

export const DieSchema = z.object({
  type: z.string(),
  detail: z.string(),
  min: z.number(),
  max: z.number(),
  desc: z.string().optional(),
});
export type Die = z.infer<typeof DieSchema>;

// ... etc for every existing interface ...

export const GameStateSchema = z.object({
  scene: SceneNameSchema,
  assetsReady: z.boolean().optional(),
  // ...
});
export type GameState = z.infer<typeof GameStateSchema>;
```

Consumers importing `GameState`, `AllyUnit`, etc. are unchanged. Discriminated unions like `ClientAction` use `z.discriminatedUnion("type", [...])`.

**Why Zod (vs. io-ts, ArkType, Valibot):** Zod has the largest ecosystem, cleanest discriminated-union syntax, a native `z.toJSONSchema()` converter (Zod v4+) emitting JSON Schema 2020-12, and composes well with Nuxt's auto-import. Bundle cost (~12KB gzipped) is acceptable.

### 2. Exported artifact: `schema/gamestate.schema.json`

A build script emits JSON Schema 2020-12 from the Zod root:

```js
// frontend/scripts/generate-schema.mjs
import { z } from "zod";
import { GameStateSchema } from "../app/types/game.js";
import fs from "node:fs/promises";

// zod v4 ships a native JSON Schema 2020-12 converter — no external dep needed.
const schema = z.toJSONSchema(GameStateSchema);
await fs.writeFile("../schema/gamestate.schema.json", JSON.stringify(schema, null, 2));
```

Wired into `package.json` as `"prebuild"` / `"pretest"` / `"generate"` so the file can never be stale. The exporter also produces a deterministic `.lock` (or in-file comment) with the Zod source hash so a drift-detection test can compare without re-running the generator.

**Location at repo root (`schema/`)**, not under `frontend/` — it's a shared-contract artifact, consumed by both mod and frontend.

### 3. Drift detection

Two automated checks, both running in `npm test`:

1. **Schema-drift test.** Runs the exporter in-memory, diffs against `schema/gamestate.schema.json`. Fails loudly with the diff if they disagree. Prompts the developer to re-run `npm run generate-schema` and commit.
2. **Reference-fixture test.** Parses `schema/reference-state.json` through the Zod schema. Fails if the fixture violates the schema (either the schema tightened, or the fixture is stale and needs re-capture from live output).

### 4. Dev-mode WebSocket validation

```ts
// useWebSocket.ts
ws.onmessage = (ev) => {
  try {
    const raw = JSON.parse(ev.data);
    if (import.meta.dev) {
      const result = ServerMessageSchema.safeParse(raw);
      if (!result.success) {
        console.error("[wire-contract] WebSocket payload violates schema:", result.error.format());
      }
    }
    handleMessage(raw);
  } catch { /* ... */ }
};
```

Deliberately uses `safeParse` + `console.error` rather than `parse` + throw so a schema drift does not crash the frontend in dev — developers can keep working while they diagnose. The `import.meta.dev` branch compiles out of production.

### 5. C# side: no code changes, contract by observation

The mod's `GameStateSerializer.cs` is untouched. It remains the production source of wire JSON. Its compliance with the schema is enforced by:

- Dev-mode validation in the frontend (any mismatch surfaces the moment a developer connects a live mod).
- The reference fixture test (any serializer change that isn't reflected in the reference fixture eventually fails there when the fixture is re-captured).

A future proposal may add generated DTOs + serializer refactor for preventive enforcement. That change can be undertaken freely once `schema.json` exists as the contract.

### 6. Migration strategy

Large single refactor is risky. Incremental plan:

1. **Add Zod + scaffold:** install deps, add an empty `schema/` directory, wire the exporter script but don't run it yet.
2. **Migrate leaf types first:** `Die`, `CardToken`, `EntryId`, `SubTarget`, `SpeedDie`, `StageInfo`, `EmotionCoins`. Low coupling, low risk.
3. **Migrate mid-level types:** `Card`, `SlottedCardEntry`, `KeyPage`, `CustomizeOptions`, etc.
4. **Migrate top-level types:** `Unit`, `AllyUnit`, `SessionState`, `GameState`, message envelopes.
5. **Migrate discriminated unions:** `ServerMessage`, `ClientAction`.
6. **Enable exporter + drift test:** generate the first `schema.json`, commit it, add the drift test.
7. **Add reference fixture + dev-mode validator.**

Each phase keeps the exported type names and shapes identical. Vue components and composables never see a change.

## Risks / Trade-offs

- **Zod bundle cost.** ~12KB gzipped on the frontend runtime. Acceptable given the contract-safety benefit and the absence of any alternative that achieves the stated goal without runtime code.
- **`z.toJSONSchema()` edge cases.** A handful of Zod features (branded types, `z.preprocess`, complex refinements) don't translate cleanly to JSON Schema. Mitigation: stick to the Zod subset that maps cleanly (objects, arrays, primitives, enums, discriminated unions, optional/nullable). The existing `types/game.ts` doesn't need any of the awkward features, so this isn't constraining.
- **Reference fixture staleness.** The hand-curated fixture can lag behind live output, masking backend drift until re-capture. Mitigation: dev-mode WebSocket validation catches drift the moment a developer runs a live mod. The fixture is the backup, not the primary check.
- **`schema.json` merge conflicts.** Two concurrent changes to `types/game.ts` will both want to regenerate the schema. The schema is generated from source, so conflicts resolve by regenerating. Annoying but not dangerous.
- **Future C# DTO generation is not free.** Deferring it to a follow-up is pragmatic but leaves the "preventive enforcement" hole open until that work happens. Accepted: the reactive path (dev-mode validation + CI fixture test) is sufficient for a small-team project.

## Migration Plan

No migration required on the consumer side. The Zod schema refactor happens inside `types/game.ts`; the published types keep their names. On merge, everyone runs `npm install` (for the new deps) and `npm run dev` continues to work. The committed `schema.json` and `reference-state.json` become durable artifacts; changes to either require re-running the generator or re-capturing the fixture.
