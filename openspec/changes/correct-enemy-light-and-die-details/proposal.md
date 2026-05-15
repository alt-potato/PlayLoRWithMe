## Why

Two pre-existing drifts between the mod serializer, the wire-contract Zod schema, and the dev fixtures: (1) every battle unit's light pool is already serialized on the wire, but the frontend treats `light`/`maxLight`/`reservedLight` as ally-only — the fields are dropped during parsing for enemies and the display gate is `v-if="isAlly"`, so enemies never show their light pips even when `maxLight > 0`; (2) `SpeedDieSchema` requires `type` and `detail` fields that the C# serializer never emits and no Vue component reads (live wire payloads silently fail `safeParse` in dev), and the battle-sampler fixture authors card-die `detail` values using display-side names (`"Pierce"`, `"Blunt"`, `"Counter"`) that don't match the wire `BehaviourDetail` enum (`Slash`, `Penetrate`, `Hit`, `Guard`, `Evasion`, `None`), so `diceIcon()` returns null and SlottedCard/HandCard fall back to `·` placeholders for those dice — making the sampler page misrepresent the deployed mod's behaviour.

## What Changes

- Move `light`, `maxLight`, `reservedLight` from `AllyUnitSchema` to `UnitSchema` so every unit's light pool flows through validation intact, matching what `GameStateSerializer.WriteUnit` has always emitted.
- Drop the `v-if="isAlly"` gate on the `<UnitLightDisplay>` row in `DisplayCard.vue`. `LightDisplay` already short-circuits via `v-if="max > 0"`, so the row stays hidden for units without a light pool (the common enemy case) and renders for the ones that do.
- Remove the vestigial required `type` and `detail` fields from `SpeedDieSchema` — no component consumes them, the serializer never writes them, and current live payloads fail the dev-mode `[wire-contract]` validator on every state push.
- Correct the battle-sampler fixture's card-die `detail` values to the `BehaviourDetail` enum names the wire actually carries (`"Pierce"` → `"Penetrate"`, `"Blunt"` → `"Hit"`). Drop the invented `detail: "Counter"` on the Standby speed die (the wire doesn't carry a `Counter` enum value), and drop `type`/`detail` from speed-die entries to mirror the live payload shape.
- Regenerate `schema/gamestate.schema.json` and update `schema/reference-state.json` so the canonical schema artifact and the reference fixture stay aligned with the corrected schemas.

## Capabilities

### New Capabilities

- `unit-light-display`: Every battle unit's light pool (current / max / reserved) is broadcast on a single unit-level wire field set and is rendered in the unit card header whenever the unit has a non-zero `maxLight` — applying uniformly to allies and enemies. The frontend draws no light row when `maxLight === 0`, so units that don't have a light pool (most enemies) remain visually quiet.

### Modified Capabilities

<!-- None. The SpeedDie schema cleanup, fixture detail-name corrections, and reference-state realignment are all implementation-level fixes under the existing `wire-contract-schema` capability — they bring the schema/fixture/serializer back into alignment without changing any documented spec-level requirement. -->

## Impact

- **Frontend**:
  - `frontend/app/types/game.ts` — `UnitSchema` gains `light`/`maxLight`/`reservedLight`; `AllyUnitSchema` loses them (now inherited). `SpeedDieSchema` loses `type` and `detail`.
  - `frontend/app/components/unit/DisplayCard.vue` — drop `v-if="isAlly"` from the `<UnitLightDisplay>` row.
  - `frontend/app/dev/fixtures/battle-sampler.json` — fix card-die `detail` values; strip `type`/`detail` from speed dice.
  - `schema/gamestate.schema.json` — regenerated artifact reflecting the schema cleanup.
  - `schema/reference-state.json` — extend the battle case so a unit with an enemy light pool is exercised; speed dice no longer carry `type`/`detail`.
- **Mod**: no C# changes required — the serializer already emits the shape we're documenting.
- **Tests**: existing `fixtures.test.ts`, `referenceState.test.ts`, and `useBattleDisplay.test.ts` cover the regressed-into-correctness paths. New scenario tests can extend fixture coverage to assert the enemy-light render path.
- **Dev mode**: `[wire-contract]` validator noise on every state push disappears once `SpeedDieSchema` matches the live payload.
