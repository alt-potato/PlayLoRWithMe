## Why

In the Crying Children's Page encounter, the base game hides every enemy combat-page die's effect description (replacing each die's desc text with `???`) while any undestroyed `PassiveAbility_240328` (Unseeing Child) is alive on an enemy. The mod's serializer currently emits the real per-die `desc` for every enemy slotted card unconditionally, so the frontend reveals information that vanilla deliberately hides. This is analogous to the already-fixed `HideEnemyTarget` leak (archived 2026-05-19) and needs the same kind of mod-side mirror so the frontend cannot show data the player should not see.

## What Changes

- `mod/GameStateSerializer.cs`: detect `Singleton<StageController>.Instance.IsHideEnemyDiceAbilityInfo()` once at the battle-scene entry point and thread the resulting flag through `WriteUnit` → `WriteSlottedCards` → `WriteCardFields` → `WriteDiceBehaviours`.
- When the gate is on AND the slotted card belongs to an enemy unit, emit `"???"` as each per-die `desc` instead of the real description. All other fields on the card (name, cost, range, `abilityDesc`, dice `type`/`detail`/`min`/`max`) are unaffected, mirroring `BattleDiceCard_BehaviourDescUI.SetBehaviourInfo`'s `isHide` branch verbatim.
- Frontend: no rendering changes required — `desc` is already optional on the wire and the existing `HandCard` / `SlottedCard` / `CardDetail` surfaces render whatever string the server sends.
- Tests: a new schema reference fixture exercises the gated wire shape (`???` as the only die-desc value on enemy slots; real text on ally slots).

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `combat-card-display`: adds a mod-side serializer requirement that masks per-die descriptions for enemy slotted cards when the vanilla die-info gate is closed, plus a frontend rendering invariant that no extra gating is needed (the existing surfaces just render the masked string).

## Impact

- C# mod: `GameStateSerializer.WriteSlottedCards`, `WriteCardFields`, `WriteDiceBehaviours` signatures gain an optional `bool maskDie` parameter; battle-scene entry probes `IsHideEnemyDiceAbilityInfo()` alongside the existing `IsVisibleEnemyTarget()` probe.
- Frontend: no source changes. Existing surfaces (`HandCard.vue`, `SlottedCard.vue`, `CardDetail.vue`, `unit/DisplayCard.vue` detail pane) render the `"???"` string transparently.
- Wire schema: no shape change. `desc` is already `z.optional(z.string())` on the per-die schema; emitting `"???"` is within the existing contract. No client-version bump.
- Tests: `frontend/schema/reference-state.json` gains a `battle_hiddenEnemyDieDescriptions` case for round-trip validation.
