## 1. Promote light fields onto every unit

- [x] 1.1 In `frontend/app/types/game.ts`, add `light: z.number()`, `maxLight: z.number()`, and `reservedLight: z.number()` to `UnitSchema`. Remove the same three fields from `AllyUnitSchema` (which extends `UnitSchema` and now inherits them). Keep the existing JSDoc rationale near the new home.
- [x] 1.2 In `frontend/app/components/unit/DisplayCard.vue`, drop the `v-if="isAlly"` gate on the `<UnitLightDisplay>` row in the header so allies and enemies share the same mount path. The `current/max/reserved` props now read off `props.unit` directly (cast through `ally.value` is no longer required for those three fields).
- [x] 1.3 Add `light`/`maxLight`/`reservedLight` to every enemy entry in `frontend/app/dev/fixtures/battle-setting.json` and `frontend/app/dev/fixtures/emotion-upgrade.json`, plus the `battle_normal` enemy in `schema/reference-state.json` (this last addition was originally task 3.3 but had to be rolled forward — making `light` required on `UnitSchema` breaks every fixture whose enemies lacked the field). Test fixtures already exercise enemy-light parsing as a result.
- [x] 1.4 Run `npm test` from `frontend/` and `cd mod && dotnet build`. Expect green tests and `0 Warning(s)  0 Error(s)`. Commit as one INVEST batch.

## 2. Drop vestigial SpeedDie schema fields

- [x] 2.1 In `frontend/app/types/game.ts`, remove `type: z.string()` and `detail: z.string()` from `SpeedDieSchema`. Leave the surrounding fields and JSDoc untouched. Confirm no consumer reads `speedDie.type` or `speedDie.detail` via repo-wide search (`grep -nE "speedDice.*\.(type|detail)|\.die\.(type|detail)"`).
- [x] 2.2 Run `npm test` from `frontend/` and `cd mod && dotnet build`. Expect green tests and `0 Warning(s)  0 Error(s)`. Commit as one INVEST batch.

## 3. Realign battle-sampler fixture with wire enum names

- [x] 3.1 In `frontend/app/dev/fixtures/battle-sampler.json`, replace every `detail: "Pierce"` with `detail: "Penetrate"` and every card-die `detail: "Blunt"` with `detail: "Hit"`. Leave `detail: "Slash"`, `detail: "Guard"`, `detail: "Evasion"`, and `detail: "Hit"` (already correct) alone.
- [x] 3.2 In the same fixture, strip the `type` and `detail` keys from every entry inside any `speedDice` array (they're now absent from `SpeedDieSchema`). Drop the invented `detail: "Counter"` on the Standby speed die in particular.
- [x] 3.3 In `schema/reference-state.json`, strip `type`/`detail` from every `speedDice` entry. (The enemy-light addition originally planned here was rolled into 1.3 because the schema change requires it for tests to pass.)
- [x] 3.4 Run `npm test` from `frontend/` and `cd mod && dotnet build`. Expect green tests and `0 Warning(s)  0 Error(s)`. Commit as one INVEST batch.

## 4. Regenerate canonical schema artifact

- [x] 4.1 From `frontend/`, run `npm run generate-schema` so `schema/gamestate.schema.json` reflects the relocated light fields and the slimmed `SpeedDieSchema`. The schema-drift test that runs under `npm test` confirms the committed artifact is current. (No-op in practice — the `pretest` hook regenerated the artifact in lockstep with each schema edit during §1 and §2, so this manual rerun produced no diff.)
- [x] 4.2 Run `npm test` from `frontend/` and `cd mod && dotnet build`. Expect green tests and `0 Warning(s)  0 Error(s)`. (Already validated as part of §1, §2, and §3; no fresh changes to commit here.)

## 5. Validate end-to-end

- [x] 5.1 Load the battle-sampler fixture in a dev build (`npm run dev` then `/?mock=battle-sampler`) and confirm: (a) every card die in hand and slotted card rows renders a real icon (no `·` placeholders for `Penetrate`/`Hit`/`Slash`/`Guard`/`Evasion` dice); (b) Melting Love's light pip row renders with 5/7 lit (5 gold + 2 unlit); (c) Pink Slimes show no light row (their `maxLight: 3` and `light: 0` mean 3 unlit pips — still acceptable, but verify the row renders rather than being absent).
- [x] 5.2 Run a live mod build (`./build_and_run.sh` if configured, otherwise `cd mod && dotnet build` and manually deploy). Open the dev console and confirm no `[wire-contract]` log entries fire on incoming `state` messages (the `SpeedDie` validation noise is gone).
- [x] 5.3 Final pass: `npm test` (frontend) and `dotnet build` (mod) both green. Open `openspec validate correct-enemy-light-and-die-details --strict --type change` and confirm the change validates.
