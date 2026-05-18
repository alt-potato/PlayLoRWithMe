## Context

The frontend's battle-die interaction layer (`DieRow.vue`) and hand-card surface (`HandCard.vue`) were built incrementally as features landed (ownership gating, BigBird_Eye fixed-targets, Stun lock overlay, clock-EGO per-die lock, full-mode hand). Three behaviors drifted slightly off vanilla LoR's actual gating along the way. This change re-anchors them.

The relevant base-game code paths (decompiled from `Assembly-CSharp.dll`):

- `LOR_BattleUnit_UI.SpeedDiceUI.OnClickSpeedDice()` — the click entry point on any speed die, ally or enemy.
- `BattleUnitModel.IsTargetableUnit(card, actor, target, targetDiceIdx)` — the authoritative cross-/same-faction target validator.
- `BattleUnitModel.IsTargetable(attacker)` — the unit-level targetability predicate, returns `false` for `_isKnockout`.
- `SpeedDiceUI.CheckBlockDice()` — used to grey out dice when `IsTargetable(null)` is false; what powers the "X" block-dice root in vanilla.

Key facts from those:

1. `OnClickSpeedDice` early-returns only on `!view.model.speedDiceResult[idx].isControlable` — the per-die `isControlable` flag (clock-EGO style). It does **not** early-return on `_bBreakedDice` (per-die staggered) or on the Stun lock overlay (which is just `breaked && hasStun` in the renderer).
2. `IsTargetableUnit`'s same-faction branch checks `target.bufListDetail.IsControlable()` and `target.speedDiceResult[targetDiceIdx].isControlable`. Both checks are guarded by `actor.faction == target.faction`, so a player attacking an enemy never touches them.
3. `IsTargetable(null)` returns `false` for `_isKnockout` — vanilla's notion of "dead". The wire's `targetable` field is sourced from this exact call (see `GameStateSerializer.cs:2207`), so the green outline mostly hides itself; the bug is a transient window where `_isKnockout` lags behind `hp <= 0`, plus the lack of a defensive `isDead` guard on the frontend side.

The hand-card hit-area bug is purely a Vue-template issue: `@click.stop` on `.hcard-detail` prevents the click event from bubbling to the root `.hcard`, where `handleClick` is bound.

## Goals / Non-Goals

**Goals:**

- Make enemy-side die-tap targetability mirror vanilla's `OnClickSpeedDice` + `IsTargetableUnit` for the cross-faction case.
- Make the green `hex-target` outline consistent with the rest of the dead-unit affordance convention (no slotted cards, no incoming chips, no arrows on dead units — and now no green hex either).
- Make the entire `HandCard` root surface accept a click as a card-select, in both `compact` and `full` display modes.
- Keep all changes frontend-only; no wire-contract or mod changes.

**Non-Goals:**

- No changes to ally-side die selection (the owner-side gates on lock, staggered, broken, ownership, etc. stay; vanilla blocks those for the unit's owner too).
- No changes to the `TargetPicker.vue` bottom sheet (it already permits locked/staggered enemy dice).
- No changes to per-die `isControlable` handling — vanilla's `OnClickSpeedDice` still early-returns on it for both sides, and the frontend's red rejection flash already mirrors that.
- No changes to the wire payload — `targetable`, `locked`, `controllable`, `staggered` already carry the right facts.
- No new arrow / overlay UI; this is purely a gating fix.

## Decisions

### Decision 1: Mirror vanilla's `IsTargetableUnit` for cross-faction by removing `!d.locked` and `!d.staggered` from the enemy `valid` check

The current enemy-side gate in `DieRow.vue` is:

```typescript
const valid =
  canBeTargeted.value &&
  !d.staggered &&
  !d.locked &&
  d.controllable !== false;
```

Vanilla's `OnClickSpeedDice` only early-returns on `!isControlable` (per-die). The Stun lock overlay (`d.locked` in our wire) is purely a visual flag — vanilla draws it via `SpeedDiceSetter.BreakDice(true, locked: hasStun)` but does not gate clicks on it. Per-die staggered (`_bBreakedDice`) is likewise not gated.

`IsTargetableUnit`'s `target.bufListDetail.IsControlable()` and per-die `isControlable` checks are guarded by `actor.faction == target.faction`. For player-attacks-enemy the guard is false and the checks are skipped.

The new gate becomes:

```typescript
const valid =
  canBeTargeted.value &&
  d.controllable !== false;
```

Where `canBeTargeted` (below) folds in unit-level targetability, dead-unit guard, and BigBird_Eye fixed-target restriction.

**Alternatives considered:**

- *Leave `!d.staggered` in place because the user didn't explicitly report it.* Rejected: it puts `DieRow` out of step with `TargetPicker.vue` (which permits staggered targets) and CLAUDE.md ("Staggered dice: still valid targets"). Fixing it now is one line; deferring guarantees we revisit it next time someone tries to target a freshly-broken enemy die.
- *Make `locked` behavior depend on faction (ally-locked → blocked, enemy-locked → allowed).* Effectively what the current gate ends up being for ally use; but the gate already runs in the `else` branch (enemy-side only), so the simpler fix is to just drop `!d.locked` here. Ally-side already has its own gate (`d.locked || d.controllable === false || isUnitBroken.value || !isOwnUnit`) that stays.

### Decision 2: Add `!isDead(props.unit)` to `canBeTargeted` as a defensive guard

The wire's `targetable` field is `unit.IsTargetable(null)` which is `false` for `_isKnockout`. In steady state this already excludes dead units. The bug is a transient window where `hp <= 0` is observable in a state push before `_isKnockout` flips. During that window `unit.targetable === true && isDead(unit) === true` — the green `hex-target` outline appears on a unit that already shows the DEAD badge.

Updated definition:

```typescript
const canBeTargeted = computed(
  () =>
    isTargeting.value &&
    props.unit.targetable &&
    !isDead(props.unit) &&
    !isRestrictedTarget(props.unit.id),
);
```

`isDead` is already imported from `useBattleDisplay.ts` (via auto-import in DieRow.vue — already used by `isUnitBroken`).

**Alternatives considered:**

- *Fix the lag on the mod side by snapshotting `_isKnockout` from `hp <= 0` directly.* Cleaner in principle but couples wire emission to a state guess that could mis-fire (a 0-HP unit might revive in some buff flows before the next push). The frontend already treats `hp <= 0` as the dead-unit convention everywhere else; making the targeting affordance follow that convention is the locally-correct fix.

### Decision 3: Remove `@click.stop` from `.hcard-detail` to make the whole `HandCard` clickable

`HandCard.vue` currently has:

```html
<div class="hcard-detail" @click.stop>
  ...
</div>
```

The `@click.stop` modifier prevents the click event from bubbling to the root `.hcard` element where `handleClick` is bound. The original motivation appears to be allowing text selection inside the detail pane, but in practice click selection and text selection don't conflict — text selection uses `mousedown` → `mousemove` → `mouseup` (no `click` fires when the user drags). The detail pane scrolls on touch via `touchmove`, which also doesn't fire `click`.

Removing the `@click.stop` lets the click bubble normally and `handleClick` fires for taps anywhere on the card. The long-press → `detail` gesture is unaffected because it lives on the root `@mousedown` / `@touchstart` and triggers via `setTimeout`; it doesn't depend on the click event.

**Alternatives considered:**

- *Make the click handler conditional on `displayMode === "full"` so compact-mode keeps `@click.stop`.* Rejected: in compact mode the detail pane is a hover-only overlay; on touch it's never shown. There is no scenario where a user clicks the compact-mode detail overlay without intending to select the card.
- *Move `@click="handleClick"` from the root onto each inner pane explicitly.* Adds duplication and a stop-propagation dance to keep events from firing twice. The single-handler-on-root pattern is simpler.

## Risks / Trade-offs

- **[Risk]** Removing `!d.locked` and `!d.staggered` from the enemy `valid` check makes more dice clickable, but the backend `IsTargetableUnit` is the authoritative gate — if for some reason the action fails server-side, the existing red rejection flash + `actionError` banner already cover it. → **Mitigation:** rely on the existing failure path; do not add a redundant frontend check.
- **[Risk]** A theme or unit that legitimately wants to render the green hex outline on a unit at HP 0 (e.g. some hypothetical "ghost" mechanic) would now be blocked by the `!isDead` guard. → **Mitigation:** none required — this matches every other dead-unit affordance the project has shipped; if such a mechanic ever lands we revisit then.
- **[Risk]** Removing `@click.stop` lets clicks on the detail pane fire `handleClick` *and* any future child interactive elements added later. → **Mitigation:** the only inner interactive element today is the (auto-imported) `KeywordText` and the per-die `<p>` elements, none of which have their own click handlers; if a future child does, it should set `@click.stop` on itself, not on the surrounding container.
- **[Trade-off]** No new wire field for "this unit is targetable *right now*". The frontend continues to do the join of `targetable && !isDead && !isRestrictedTarget`. This keeps the wire payload lean and the join logic auditable in one place.
