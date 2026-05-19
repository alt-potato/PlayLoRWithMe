## Context

The base game gates per-die description text for enemy-owned card previews via `StageController.IsHideEnemyDiceAbilityInfo()`, which delegates to the active `EnemyTeamStageManager`. `EnemyTeamStageManager_TheCrying.IsHideDiceAbilityInfo()` returns `true` while any alive enemy carries an undestroyed `PassiveAbility_240328` (Unseeing Child). When the gate is closed and the previewed card belongs to an enemy, vanilla `BattleUnitProfileInfoUI` calls `BattleDiceCardUI.SetCard(card, default(BattleDiceCardUI.Option))` (i.e. `Option.HideDiceAbilityInfo`), and the per-die child `BattleDiceCard_BehaviourDescUI.SetBehaviourInfo` replaces the die's effect description text with `"???"`. Card name, cost, range, the card-level `abilityDesc`, and dice icons/values are untouched.

The mod's `GameStateSerializer` currently emits the real per-die `desc` for every slotted card on every unit (ally and enemy alike). The frontend's `HandCard` / `SlottedCard` / `CardDetail` surfaces render whatever string the server sends. Players in the co-op session therefore see the hidden text. This change mirrors the vanilla mask on the server side so the wire never carries the hidden information in the first place.

The mod already has a sibling gate threaded through the same code path: `enemyTargetsHidden` (archived change `2026-05-19-hide-enemy-targets-when-gated`). This change follows the identical plumbing pattern so the two gates stay symmetrical and easy to maintain together.

## Goals / Non-Goals

**Goals:**

- When `IsHideEnemyDiceAbilityInfo()` is `true` during the `battle` scene, every enemy unit's slotted card emits `"???"` as the `desc` of every die, rather than the real description.
- Ally slotted cards remain untouched (vanilla only masks enemy-owned card previews).
- All other card fields (name, cost, range, `abilityDesc`, `dice.type/detail/min/max`, `bufs`, `options`) emit unchanged.
- Wire schema is unchanged — `desc` is already `z.optional(z.string())`.
- Frontend requires no changes — existing surfaces render the masked string verbatim.

**Non-Goals:**

- Masking enemy hand-card descriptions. The mod never emits enemy hand/deck cards, so there is nothing to mask there.
- Masking the `abilityDesc` field. Vanilla's `SetCard(card, Option.HideDiceAbilityInfo)` only passes `isHide` to the per-die child; the card-level `abilityDesc` is still rendered in-game.
- Masking enemy unit names, dice icons, or die roll ranges. None of those are gated by `IsHideDiceAbilityInfo`.
- Adding any new wire fields. The mask is implemented purely by substituting the `desc` value.

## Decisions

### Where to apply the mask

Apply at the same site as `enemyTargetsHidden`: probe `Singleton<StageController>.Instance.IsHideEnemyDiceAbilityInfo()` once at the battle-scene entry point in `BuildJson`, store the result in a local boolean (`dieDescriptionsHidden`), and thread it through `WriteUnit(..., dieDescriptionsHidden)` → `WriteSlottedCards(..., dieDescriptionsHidden)` → `WriteCardFields(..., maskDie: dieDescriptionsHidden && !isAlly)` → `WriteDiceBehaviours(..., maskDie)`.

**Why not branch inside `WriteDiceBehaviours` based on faction?** `WriteDiceBehaviours` is also called from ally-hand and EGO emission sites where the faction context is implicit. Threading an explicit `maskDie` boolean keeps each call-site's intent visible at the call site instead of hiding it behind a faction lookup on the card model.

**Why not apply the mask only to specific cards (e.g. the ones the Crying Children themselves are playing)?** Vanilla applies the gate to every enemy-owned card preview, not just the gating enemy's cards. We mirror that exactly.

### What to emit for the masked string

Emit the literal string `"???"`. This matches `BattleDiceCard_BehaviourDescUI.SetBehaviourInfo` (`text = "???"` when `isHide`). The frontend renders the string verbatim, so the player sees `???` exactly as in vanilla.

**Why not omit the `desc` field entirely?** That would change the wire shape for masked slots vs. unmasked ones, and existing frontend surfaces would render a blank space instead of the `???` cue. Emitting `"???"` keeps every surface visually equivalent to vanilla without any frontend change.

### Fail-open on probe failure

If `Singleton<StageController>.Instance` is null or `IsHideEnemyDiceAbilityInfo()` throws, treat the gate as `false` (no masking). This matches the vanilla `try/catch` inside `StageController.IsHideEnemyDiceAbilityInfo` itself and prevents unrelated runtime errors from blanking enemy descriptions outside the gated encounter.

## Risks / Trade-offs

- **Probe overhead per battle state push** → trivial; the call walks the alive-enemy list once per push (same cost as the already-existing `IsVisibleEnemyTarget` probe).
- **Future passives that hide additional card info** → out of scope for this change. The hook point (per-die `desc`) is the only thing the in-game `HideDiceAbilityInfo` gate masks, so we mirror exactly that. If a future passive masks something else, that's a separate proposal.
- **Frontend assumes `???` is a hidden-info cue** → trivially: the string is opaque to the frontend, which just renders it. Players already learn the meaning from playing the encounter in vanilla.
