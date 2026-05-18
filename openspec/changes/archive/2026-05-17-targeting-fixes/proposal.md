## Why

Three targeting affordances in the battle UI diverge from how vanilla Library of Ruina (LoR) actually gates target selection, surfacing as small but consistent papercuts during play:

1. **Locked enemy dice cannot be targeted.** The frontend treats the "lock" overlay (rendered when an enemy unit has Stun) as an untargetable signal, but vanilla's `BattleUnitModel.IsTargetableUnit` only consults per-die `isControlable` for **same-faction** targeting — an attacking player can freely target a stunned enemy's dice (and in practice usually wants to, since a stunned enemy is a free target).
2. **Dead enemies still render the green "targetable" outline.** Dead units fail vanilla's `IsTargetable(null)` (via `_isKnockout`), but the frontend's `canBeTargeted` derivation has a window where `unit.targetable === true` slips through during the death transition, leaving the green hex highlight visible on a unit whose other affordances (slotted cards, incoming chips, arrows) are already hidden by the dead-unit convention.
3. **Hand card selection has a too-small hit area.** `HandCard.vue` puts `@click.stop` on its `.hcard-detail` pane, so taps on the right portion of a battle-mode hand card (the always-visible detail pane in `displayMode="full"`) do not bubble to the root selection handler — only clicks on the left preview pane count.

These are all small, frontend-only behaviors with no wire-contract or mod changes required. Grouping them keeps one batch of "targetability gating" review together.

## What Changes

- **Locked enemy dice** — In `DieRow.vue`, remove the `!d.locked` clause from the enemy-side `valid` check so stun-locked enemy dice accept target clicks. Per-die `controllable !== false` and unit-level `untargetable`/`isDead` continue to gate (matches vanilla's `SpeedDiceUI.OnClickSpeedDice` early-return and `IsTargetableUnit`).
- **Staggered enemy dice** — Drop `!d.staggered` from the same `valid` check; vanilla `OnClickSpeedDice` has no `_bBreakedDice` early-return, the `TargetPicker.vue` bottom-sheet already permits it, and the project convention (CLAUDE.md, "Staggered dice: still valid targets") expects it. Bringing `DieRow` in line removes a latent inconsistency the same audit surfaced.
- **Dead enemy targetability** — Add `!isDead(props.unit)` to `canBeTargeted` in `DieRow.vue` so the green `.hex-target` outline and pointer affordance never appear on a unit that already shows the DEAD badge. This guards both the steady-state and the death-transition window where the wire's `targetable` flag has not yet flipped.
- **Hand card hit area** — Remove `@click.stop` from `.hcard-detail` in `HandCard.vue` so a click anywhere on the card root (preview or detail pane) triggers `handleClick`. The long-press / `detail` emit gesture is unaffected because it uses `mousedown`/`touchstart` rather than `click`.

## Capabilities

### New Capabilities

- `combat-targeting`: Frontend rules for when a battle die may be picked as a card target, and the visual affordances that signal targetability. Codifies the cross-faction vs. same-faction gating so it does not drift back away from vanilla.

### Modified Capabilities

- `combat-card-display`: Adds a requirement that the entire `HandCard` root surface (both preview and detail panes, in both display modes) is the click target for the `click` emit.

## Impact

- Frontend only — `frontend/app/components/unit/DieRow.vue`, `frontend/app/components/HandCard.vue`.
- No wire schema changes; the existing `targetable`, `locked`, `controllable`, and `staggered` fields are sufficient.
- No mod / C# changes.
- No new dependencies.
- Existing tests in `frontend/app/composables/useBattleDisplay.test.ts` and any DieRow tests stay green; new unit coverage will exercise the targetability and hit-area rules.
