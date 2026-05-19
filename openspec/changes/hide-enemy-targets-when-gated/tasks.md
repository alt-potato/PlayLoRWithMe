## 1. Mod-side serializer gating

- [x] 1.1 Add a cached `bool enemyTargetsHidden` lookup at the top of the `battle` branch of `GameStateSerializer.BuildJson` that reads `Singleton<StageController>.Instance?.IsVisibleEnemyTarget()` and inverts it. Guard with `try`/`catch` (or null-coalesce) so any failure falls back to `false` (gate open). Pass the value through to `WriteUnit` and on into `WriteSlottedCards` via a new parameter.
- [x] 1.2 In `WriteSlottedCards`, when `enemyTargetsHidden && unit.faction == Faction.Enemy`, skip the `targetUnitId` / `targetSlot` / `clash` / `subTargets` emission entirely (the `if (slot.target != null)` block becomes a no-op for enemies in this case). When `enemyTargetsHidden && unit.faction != Faction.Enemy`, emit `targetUnitId`/`targetSlot`/`subTargets` as today but force `clash: false`.
- [x] 1.3 Add an inline comment at the gate explaining the "why" (encounter passive 240428 / `EnemyTeamStageManager_TheCrying.HideEnemyTarget()` / vanilla `BattleUnitTargetArrowManagerUI.Show*Arrow` gating) so the next reader does not need to re-derive it.
- [x] 1.4 Validate: `cd mod && dotnet build` reports `0 Warning(s)  0 Error(s)`.

## 2. Frontend schema and fixture coverage

- [x] 2.1 Add a wire fixture file under `frontend/schema/` (or wherever `reference-state.json` cases live in the current tree — locate via existing test imports) representing a Crying-Children-style gated state: enemy `slottedCards[]` with `card` populated but no `targetUnitId`; ally `slottedCards[]` with `targetUnitId` set and `clash: false`. Include the fixture in the existing schema round-trip test list so `GameStateSchema.parse` is exercised. (Added `battle_hiddenEnemyTargets` to `schema/reference-state.json`. Also relaxed `SlottedCardEntrySchema.clash` from required to optional since the previous required-boolean shape contradicted `targetUnitId` already being optional and would have rejected any no-target slot.)
- [x] 2.2 If the dev mock-backend's `FIXTURE_LOADERS` is the canonical place to register UI-exercising fixtures, add a matching fixture entry (e.g. `battle-hidden-enemy-targets`) so the gated state is observable via the dev picker. Otherwise skip and note in PR description. (Added `battle-hidden-enemy-targets.json` and registered it in `frontend/app/dev/fixtures/index.ts`.)
- [x] 2.3 Validate: `cd frontend && npm test` passes (full vitest suite, including any new fixture round-trip cases). (173/173 pass, including new schema and fixture round-trip cases.)

## 3. Frontend rendering assertions (no implementation change expected)

- [x] 3.1 Add a unit test in the appropriate composable / component test file (e.g. `composables/useBattleContext.test.ts` or a new file under `components/__tests__/`) asserting that an ally slot with `targetUnitId` set and `clash: false` produces an `↗` (not `⚔`) chip prefix in `DieRow`-style formatting and an `outgoing` (not `clash`) `ArrowOverlay` arrow type. Use existing pure-helper invocation patterns; do not mount the full Vue tree if a pure helper is available. (Added a `slotted-card clash-driven rendering invariants` block to `useBattleDisplay.test.ts` covering the three rendering branches.)
- [x] 3.2 Add a unit test asserting `attackMap` (Stage.vue's computed) skips any slot with absent `targetUnitId`, so an enemy with hidden targets contributes zero entries. (Extracted `buildAttackMap` from `Stage.vue` into `useBattleDisplay.ts` and added a `buildAttackMap` describe block with four scenarios including the gated wire end-to-end case.)
- [x] 3.3 Validate: `cd frontend && npm test` still passes; no display-side template change was required. (180/180 pass — Stage.vue now consumes the extracted helper; templates and component scripts otherwise unchanged.)

## 4. Manual end-to-end verification

- [x] 4.1 Run `./build_and_run.sh` to deploy to the live LoR install, load The Crying Children's Page encounter, and confirm in the connected web UI: enemy speed dice and hand chips remain visible; no enemy incoming arrows appear on ally dice; no `⚔` clash marker appears on either side; ally outgoing blue arrows still draw to enemy dice when slotting cards. (Confirmed working by user. Initial pass dropped enemy speed-die values to `—`; fixed in `DieRow.vue` by adding a `hidden-target` `DieState` that renders the rolled value with no directional decoration when a slot has a card but no `targetUnitId`.)
- [x] 4.2 Stagger the Unhearing Child holder and confirm the next state push restores enemy arrows and clash markers, verifying the gate re-opens correctly. (Confirmed.)
- [x] 4.3 Smoke-test one ordinary encounter (no `HideEnemyTarget` override) and confirm no regression — full arrow and clash rendering as before. (Confirmed.)

## 5. Wrap-up

- [x] 5.1 Update CLAUDE.md if any inline documentation in `GameStateSerializer.cs` or the architecture section materially changed (likely just a one-line note that `WriteSlottedCards` is gated by `IsVisibleEnemyTarget()`).
- [x] 5.2 Validate: `cd mod && dotnet build` (end-to-end, includes frontend regen) reports `0 Warning(s)  0 Error(s)` and the dev server boots cleanly.
