## Context

Three independent drifts have accumulated between the C# serializer (`GameStateSerializer.WriteUnit`, `WriteSpeedDice`, `WriteDiceBehaviours`), the Zod wire schema (`UnitSchema`, `AllyUnitSchema`, `SpeedDieSchema` in `types/game.ts`), and the dev fixtures:

1. **Enemy light is silently dropped.** `WriteUnit` writes `light`, `maxLight`, `reservedLight` on every unit, but those fields only live on `AllyUnitSchema`. Zod parses with strip-unknown semantics, so enemy light fields disappear during validation. `DisplayCard.vue` then gates the entire `<UnitLightDisplay>` row on `v-if="isAlly"` — a redundant guard now that `LightDisplay` itself short-circuits via `v-if="max > 0"`.
2. **`SpeedDieSchema` requires fields nobody emits or reads.** `type` and `detail` are required strings on the schema, but `WriteSpeedDice` never writes them, no component reads them off a speed die, and the dev-mode `[wire-contract]` validator therefore logs a schema violation on every live `state` push. (The `type`/`detail` pair is meaningful for `DiceBehaviour` inside a card, not for the speed die itself — the schemas got conflated.)
3. **Battle-sampler fixture uses display-side names.** Card-die `detail` values are authored as `"Pierce"` and `"Blunt"`, but the wire enum (`LOR_DiceSystem.BehaviourDetail`) is `{Slash, Penetrate, Hit, Guard, Evasion, None}`. `DETAIL_SEGMENT` in `useBattleDisplay.ts` keys on the wire names, so the fixture's mistranslated dice resolve to a null icon path and the rendering falls back to a `·` placeholder. The deployed mod doesn't have this problem because the C# serializer emits the enum names via `d.Detail.ToString()`.

The fix is a single coordinated correction: align schema, fixture, and display so the wire shape the live mod emits is also the wire shape the schema validates and the fixtures replay. No serializer change is needed — the mod is already correct.

## Goals / Non-Goals

**Goals:**

- Every battle unit's light pool flows through `GameStateSchema` parsing intact, regardless of faction.
- The unit-card light row is rendered uniformly for every unit with a non-zero light pool, allies and enemies alike.
- `SpeedDieSchema` matches the live wire payload exactly — no required fields the serializer skips.
- The battle-sampler fixture's card dice resolve to real `diceIcon()` URLs so the dev page mirrors deployed behaviour.
- The dev-mode `[wire-contract]` validator stops logging false positives on every live `state` push.
- Canonical artifacts stay aligned: `schema/gamestate.schema.json` regenerates cleanly; `schema/reference-state.json` no longer ships `type`/`detail` on speed dice and gains an enemy-light scenario.

**Non-Goals:**

- Modifying the C# serializer (`WriteUnit`, `WriteSpeedDice`, `WriteDiceBehaviours`). It already emits the shape we're documenting.
- Reworking `LightDisplay`'s visual treatment for enemies. Same component, same colours, same pip math — only the gate on the parent moves.
- Adding new mock fixture scenarios beyond what's needed to exercise the enemy-light path.
- Touching `dieTypeColor`, `DETAIL_SEGMENT`, or any helper map — they're already keyed on the wire enum names and require no change.

## Decisions

### D1: Promote `light`/`maxLight`/`reservedLight` to `UnitSchema` rather than mirroring them onto enemies via a separate `EnemyUnitSchema`

The fields are universal on `BattleUnitModel` — every battle unit has a `PlayPoint`/`MaxPlayPoint`/`ReservedPlayPoint` triple. The serializer already treats them as unit-level, not faction-level. Hoisting them to `UnitSchema` keeps schema and wire in lockstep and avoids inventing a third schema variant. `AllyUnitSchema` continues to extend `UnitSchema` and now inherits the fields instead of redeclaring them, so every existing `ally.light` access path keeps working untouched.

**Alternative considered:** Add `light`/`maxLight`/`reservedLight` as `z.optional` on `UnitSchema` to model "enemies sometimes carry light, sometimes don't." Rejected — the wire always carries the fields (the serializer emits them unconditionally with `unit.PlayPoint` falling to 0 when no pool exists), so optionality is misleading and would force consumers into `?? 0` defensive checks.

### D2: Drop `type` and `detail` from `SpeedDieSchema` rather than start emitting them from the serializer

No frontend code reads `speedDie.type` or `speedDie.detail`. The `dieTypeColor`/`diceIcon` paths consume `d.type`/`d.detail` on `DiceBehaviour` objects inside cards (`SlottedCardEntry.dice[]`, `Card.dice[]`), not on speed dice. Adding emit-side code to satisfy an unused schema field would be pure overhead. The simpler correction is to remove the fields from the schema, ending the dev-mode log noise and clarifying what speed dice actually carry on the wire.

**Alternative considered:** Have `WriteSpeedDice` emit `type`/`detail` from the underlying speed-die rule, then surface them in `DieRow` (e.g. a small glyph next to the rolled value). Rejected — that's a new feature wearing a fix's clothing; if we want per-speed-die glyphs in the future, propose that explicitly with its own UI design.

### D3: Correct fixture `detail` values to match the `BehaviourDetail` wire enum, not invert the helper

`DETAIL_SEGMENT` is keyed on `Penetrate` and `Hit` because that's what `d.Detail.ToString()` emits from the C# enum (`LOR_DiceSystem.BehaviourDetail = { Slash, Penetrate, Hit, Guard, Evasion, None }`). The helper is correct; the fixture is wrong. Adding `Pierce` and `Blunt` aliases would create a second source of truth for the wire field and let future fixtures continue authoring against the display name, exactly the failure mode this fix is correcting.

### D4: Remove `detail: "Counter"` rather than expand `BehaviourDetail` modelling

`Counter` is not a value of `BehaviourDetail`; it's a display-side word for `BehaviourType.Standby` (see `useBattleDisplay.ts`'s type→colour map and the project CLAUDE.md note: "`Standby` displays as 'Counter' in-game"). The speed die in the battle-sampler fixture should carry no `detail` at all (consistent with the live wire shape after D2) — what's left is `type: "Standby"`, which is correctly the wire value.

### D5: Update `schema/reference-state.json` minimally — add one enemy with a light pool to the existing battle case

The reference fixture already exercises ally light. Adding a single enemy field-set (`light: 2, maxLight: 4, reservedLight: 0` on the existing `battle_normal.enemies[0]`) is the smallest change that turns the new requirement into a regression test. Removing `type`/`detail` from speed-die entries in this fixture is necessary to keep `referenceState.test.ts` green after the schema cleanup.

## Risks / Trade-offs

- **Risk: A future fixture author writes `detail: "Pierce"` again.** → Mitigation: `fixtures.test.ts` already round-trips every fixture through `GameStateSchema.parse`. The icon-resolution failure is silent at parse time (`detail` is just `z.string()`), so the parse test won't catch a typo. The realistic mitigation is the deployed sampler page itself — once it renders icons correctly, an authoring drift becomes visually obvious. A future hardening proposal could narrow `DiceBehaviourSchema.detail` to `z.enum(["Slash", "Penetrate", "Hit", "Guard", "Evasion", "None"])`, but that's a wider blast radius and belongs in its own change.
- **Risk: Removing `type`/`detail` from `SpeedDieSchema` breaks a downstream consumer I missed.** → Mitigation: `npm test` plus a `Grep` for `speedDie.type`, `speedDice.*\.type`, `speedDice.*\.detail`. The current sweep returns no consumers, but the build step is the final guard.
- **Risk: Schema artifact drift.** → Mitigation: `prebuild`/`pregenerate`/`pretest` already regenerate `schema/gamestate.schema.json`, so the committed artifact stays current. We commit the regenerated file in the same atomic batch as the schema change.
- **Trade-off: Enemy light renders identically to ally light** (gold pips, same component). For a player glancing at the screen this could read as ambiguous "whose light is this?" — but the unit card border colour and faction-coloured die fills already disambiguate ownership, and the alternative (a separate enemy-light visual treatment) is gratuitous polish outside this fix's scope. Defer to a follow-up if the deployed render shows it as a real problem.

## Migration Plan

No data migration needed — schemas describe live wire shape. The implementation lands as a single atomic commit per task group (per CLAUDE.md INVEST guidance):

1. Schema relocation + serializer-compatibility check (read-only inspection of `WriteUnit`/`WriteSpeedDice`).
2. `DisplayCard.vue` gate removal.
3. Fixture `detail`-value correction + speed-die field stripping (battle-sampler + reference-state).
4. Regenerate `schema/gamestate.schema.json`.
5. `dotnet build` from `mod/` to validate the full pipeline (frontend generate runs in AfterBuild).

Rollback is `git revert` on any commit — none of the changes mutate persisted state.
