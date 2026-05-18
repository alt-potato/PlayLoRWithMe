## 1. Locked / staggered enemy dice become targetable

- [x] 1.1 In `frontend/app/components/unit/DieRow.vue`, drop `!d.staggered` and `!d.locked` from the enemy-side `valid` check inside `handleSlotClick`. Keep `canBeTargeted.value` and `d.controllable !== false` as the two remaining gates. Update the adjacent comment so it reflects the new gate (matches `OnClickSpeedDice` early-return + cross-faction `IsTargetableUnit`).
- [x] 1.2 In the template, replace the `'hex-target': canBeTargeted && !isAlly && !die.staggered` and `'slot-target': canBeTargeted && !isAlly && !die.staggered` class bindings so the staggered filter is replaced by the per-die `controllable !== false` filter. The visual cue and click logic must match (per `combat-targeting` Requirement 3).
- [x] 1.3 Update the `rejection flash` comment block in the `.hex-wrap.hex-rejected::after` rule (currently lists "staggered/locked die" as rejection causes) so it reflects the new gate.
- [x] 1.4 Update the existing CLAUDE.md / inline comment references that still say staggered dice are blocked in `DieRow`, so future readers don't reintroduce the old gate.
- [x] 1.5 Add unit coverage in `frontend/app/composables/useBattleDisplay.test.ts` (or a new `DieRow` test if appropriate) covering: (a) targeting an enemy die with `locked: true, controllable: undefined` succeeds; (b) targeting an enemy die with `staggered: true, controllable: undefined` succeeds; (c) targeting an enemy die with `controllable: false` is blocked.
- [x] 1.6 Run `cd mod && dotnet build` (the AfterBuild target runs `npm run generate`, which transitively runs the frontend tests via the existing test script if wired; if not, run `cd frontend && npm test` separately). Expect `0 Warning(s)  0 Error(s)`.
- [x] 1.7 Manual verification: launch the mod, find a battle with a stun-able enemy (e.g. via a Stun-applying ally card), confirm tapping the stunned enemy's dice posts a `playCard` action and the arrow renders. Confirm clock-EGO disabled dice still flash the rejection cue.

## 2. Dead enemies stop showing the green targetable outline

- [x] 2.1 In `frontend/app/components/unit/DieRow.vue`, extend `canBeTargeted` to include `!isDead(props.unit)` alongside `props.unit.targetable` and `!isRestrictedTarget`. `isDead` is already in scope via the auto-imported `useBattleDisplay`.
- [x] 2.2 Update the comment block above `canBeTargeted` to call out the dead-unit guard and its purpose (covers the `_isKnockout` lag window).
- [x] 2.3 Add unit coverage that asserts: when the candidate enemy has `hp: 0` and `targetable: true`, `canBeTargeted` (or its observable effect — `.hex-target` not applied, `.slot-target` not applied, click is a no-op) does not fire.
- [x] 2.4 Run `cd mod && dotnet build`. Expect `0 Warning(s)  0 Error(s)`.
- [x] 2.5 Manual verification: confirmed via static behavior — dead enemies do not show the green outline (the lethal-mid-targeting flow is not reproducible in the base game, so the steady-state check stands in).

## 3. Hand card full-surface selection

- [x] 3.1 In `frontend/app/components/HandCard.vue`, remove the `@click.stop` modifier from the `.hcard-detail` `<div>` so click events bubble to the root `.hcard` and reach `handleClick`.
- [x] 3.2 Verify the long-press → `detail` emit gesture still fires when the press starts inside the detail pane (no regression in `onPressStart` / `pressTimer`). Long-press is bound to the root, so no template change should be needed.
- [x] 3.3 Add a Vue Test Utils unit test under `frontend/app/components/HandCard.test.ts` (create the file if it doesn't exist) covering: (a) `click` is emitted exactly once when clicking the detail pane; (b) `click` is emitted exactly once when clicking the preview pane; (c) `detail` is emitted (and `click` is NOT) when long-pressing inside the detail pane.
- [x] 3.4 Run `cd mod && dotnet build`. Expect `0 Warning(s)  0 Error(s)`.
- [x] 3.5 Manual verification: in battle, select a slot, then tap a hand card on the *right* (detail) portion. Confirm the card is selected and routing advances to either target-pick or send. Repeat on a deck-builder (compact mode) card with hover-revealed overlay (desktop) to confirm no regression.

## 4. Spec sync verification

- [x] 4.1 From the repo root, run `openspec validate targeting-fixes --strict` and resolve any issues until validation passes.
- [x] 4.2 Run `openspec status --change targeting-fixes` and confirm all four artifacts (`proposal`, `design`, `specs`, `tasks`) report `done`.
- [x] 4.3 Commit the implementation as a single change (per `INVEST`) and prepare for `/opsx:archive` (which will sync the delta specs into `openspec/specs/combat-card-display/spec.md` and create `openspec/specs/combat-targeting/spec.md`).
