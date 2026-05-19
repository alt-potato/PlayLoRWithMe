## Context

The Library of Ruina base game gates enemy-side targeting visualisation through `StageController.IsVisibleEnemyTarget()`, which delegates to the active `EnemyTeamStageManager.HideEnemyTarget()` override (default base-class returns `false`). `BattleUnitTargetArrowManagerUI` then short-circuits `ShowEnemyArrow` and `ShowParryingArrow` when that gate is closed:

```csharp
public void ShowEnemyArrow(bool show)
{
    if (show && Singleton<StageController>.Instance.IsVisibleEnemyTarget())
        ShowTargetLines(enemyList, 1);
    else
        OffArrows(enemyList);
}

public void ShowParryingArrow(bool show)
{
    if (show && Singleton<StageController>.Instance.IsVisibleEnemyTarget())
        ShowTargetLines(parryingList, 2);
    else
        OffArrows(parryingList);
}
```

The relevant override here is `EnemyTeamStageManager_TheCrying.HideEnemyTarget()`, which returns `true` while any alive enemy holds an undestroyed `PassiveAbility_240428` (the Unhearing Child passive on The Crying Children's Page, book 142003). The passive's `OnBreakGageZero` flips `destroyed = true`, so the gate opens again the moment the unit is staggered.

The mod's wire output via `GameStateSerializer.WriteSlottedCards` does not consult this gate — it always emits every slotted card's `targetUnitId`, `targetSlot`, `clash`, and `subTargets` whenever `slot.target != null`. The frontend then renders the full incoming-attack graph and clash markers, leaking information the base game has deliberately hidden.

## Goals / Non-Goals

**Goals:**

- Wire output mirrors the base game: when `IsVisibleEnemyTarget()` is false, the frontend cannot see enemy-side outgoing arrows or clash markers.
- Player still sees their own outgoing blue arrows pointing at enemy dice (the base game keeps these visible too), so the cooperative-play flow remains uninterrupted.
- Zero schema breakage. All affected JSON fields were already optional (existed only `if (slot.target != null)`), so we are simply adding more conditions for omission.
- No frontend template changes. Existing helpers already handle missing `targetUnitId` / falsy `clash` as "no incoming arrow" / "no clash highlight."

**Non-Goals:**

- Other `EnemyTeamStageManager` virtual gates (`BlockEnemyAggroChange`, `IsHideDiceAbilityInfo`) are out of scope for this change. They could leak related info but each requires distinct serializer handling and its own test coverage — track separately.
- We do **not** suppress enemy speed-die data (ATK/DEF type, value, staggered/locked state). The base game still shows enemy dice; only the directional/clash arrows are hidden.
- We do **not** suppress ally `subTargets[]` (ally mass-attack secondary hits). These originate from the player side and the base game still draws them when the player hovers a player die — they are not part of what the gate suppresses.
- No new wire fields are introduced. A boolean like `enemyTargetsHidden` on `BattleState` is *not* needed because the frontend's existing "no target = no arrow" rendering path is sufficient; adding state surface only invites drift.

## Decisions

### Gate at the serializer, not the frontend

The mod is the only side that has reliable, real-time access to `StageController.Instance.IsVisibleEnemyTarget()`. Reproducing the gate on the frontend would require either re-implementing `EnemyTeamStageManager` lookups or shipping an explicit `enemyTargetsHidden: true` flag on every state push. The serializer-side fix is one localised conditional and matches the project's existing pattern (e.g. `SerializeForSession` already filters unowned ally hand/deck/ego at the serializer).

**Alternative considered:** Add `battle.enemyTargetsHidden: bool` to the wire and let the frontend conditionally hide arrows. Rejected — adds a redundant control surface and a new schema field for no observable user-facing benefit. The serializer already has full context here.

### Cache `IsVisibleEnemyTarget()` once per battle-state build

`WriteSlottedCards` is called once per unit. Reading `Singleton<StageController>.Instance.IsVisibleEnemyTarget()` per call would (a) repeat the same singleton lookup N times per push, and (b) require each call site to handle a possible exception from the same singleton. We cache the boolean once at the top of the battle branch of `BuildJson` (or pass it through to `WriteUnit` / `WriteSlottedCards` as a parameter) so every unit sees a consistent value for the snapshot.

The vanilla `IsVisibleEnemyTarget` already catches its own exceptions and returns `true` on error — that "fail open" stance is the right default for our gate too (if reading the singleton fails, we keep emitting target info just like vanilla keeps drawing arrows). No additional try/catch needed.

### Ally clash flag

When `IsVisibleEnemyTarget()` is false, the ally slot's `clash: bool` must be forced to `false` even though the player side could in principle compute it from the ally's own slot data — because the *check* compares the ally slot's target with the enemy slot's target, and the enemy side of that pair is exactly what the gate is hiding. Emitting `clash: true` on the ally side would visibly mark a clash on the frontend's `DieRow` / `DisplayCard` (`⚔` glyph + `.clash` background), recreating the leak we're trying to plug.

We do **not** suppress the ally's `targetUnitId`/`targetSlot` — the base game keeps the blue outgoing arrow visible (`ShowAllyArrow` does not check the gate), and the player needs that feedback to know where their own attack lands.

### Enemy-side suppression scope

For enemy slots, omit the entire `targetUnitId` / `targetSlot` / `clash` / `subTargets` quadruple. This mirrors the base game suppressing both `ShowEnemyArrow` (1) and `ShowParryingArrow` (2). Partial suppression (e.g. emit `targetUnitId` but not `clash`) would not match — the player would still see arrow chips in `attackMap`-driven incoming indicators (`SlottedCard.vue` chip display on `DieRow`).

### Test approach

Add unit coverage via the existing reference-fixture pattern in `frontend/schema/`:

1. A new wire fixture for `battle` + `enemyTargetsHidden`-style state (enemy slot has `card` but no `targetUnitId`; ally slot has `targetUnitId` set with `clash: false`) parses cleanly through `GameStateSchema`.
2. A frontend display test asserts the corresponding rendering invariants: `attackMap` derived from such a state has no entries pointing at ally units; `DieRow` / `DisplayCard` produce no `⚔` / `.clash` decoration for ally slots in that state.

C#-side: no automated test infrastructure exists in `mod/`. Manual verification via `build_and_run.sh` against the 142003 encounter is the only practical end-to-end check. The proposal accepts this gap; tasks.md will note that the encounter must be loaded and observed.

## Risks / Trade-offs

- **[Risk]** A future encounter overrides `HideEnemyTarget()` partially (e.g. only hides during certain phases) and the cached-once value goes stale mid-frame → Mitigation: the cache lifetime is one `BuildJson` call, i.e. one state snapshot. Subsequent snapshots re-read the singleton. Any phase change within one render frame is not user-visible.
- **[Risk]** `Singleton<StageController>.Instance` could be null between battles → Mitigation: gated by the `battle` scene branch in `BuildJson` (same branch that already accesses `StageController.Instance`), so we never read it outside its established lifetime. Defensive null-coalescing keeps the gate "open" (fields emitted as today) if Unity is in an unexpected state.
- **[Risk]** Subagent / contributor confuses "hide enemy targets" with "hide enemy dice" → Mitigation: design and tasks both explicitly call out that enemy speed dice, hand chips, and any non-target info remain visible; only the four target/clash JSON fields on enemy slots change.
- **[Trade-off]** Frontend doesn't get an explicit "this is the cause" signal for why an arrow is missing. Acceptable — the base game also gives the player no UI hint that targeting is hidden by a passive; they learn it from the encounter itself.

## Migration Plan

No migration. Frontend behavior is forward-compatible because the gated fields are already optional in the schema. Old frontends will simply render fewer arrows on this encounter — which is the desired outcome.

Rollback: revert the single serializer change. No persisted state, no client-version bump.

## Open Questions

None. The Crying Children encounter is the only currently-shipping use of `HideEnemyTarget()` we have identified, but the gate is a general mechanism — applying it consistently for every override is the intent.
