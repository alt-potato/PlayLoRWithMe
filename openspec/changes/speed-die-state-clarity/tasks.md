## 1. C# theme probe and serializer additions

- [x] 1.1 Create `mod/ThemeProbe.cs` (or fold equivalent logic into an existing init flow) that finds a `SpeedDiceUI` instance via `Resources.FindObjectsOfTypeAll<SpeedDiceUI>()` and reflects into the private `Refs` field to read `color_allyDice` and `color_enemyDice`. Convert each `Color` to `#rrggbb` lowercase hex and cache as two static `string`s.
- [x] 1.2 Implement the deferred-retry path: if init runs before any prefab is loaded, schedule a single retry on the first state push after a battle scene loads. If a permanent error occurs, log a single `[ThemeProbe]` warning and proceed with `null` values.
- [x] 1.3 Add `WriteTheme(JsonWriter o)` helper in `GameStateSerializer.cs` that emits the `theme.factionDieColors` block from the cached values; called from the `hello` writer when values are present.
- [x] 1.4 Extend `WriteSpeedDice` to emit `locked` per die, computed as `(!unit.bufListDetail.IsControlable()) || (!d.isControlable)`. Both the post-roll and pre-roll placeholder paths must include the field.
- [x] 1.5 `cd mod && dotnet build` runs `0 Warning(s) 0 Error(s)`.

## 2. Schema extension

- [ ] 2.1 Extend `SpeedDieSchema` in `frontend/app/types/game.ts` with `locked: z.optional(z.boolean())`.
- [ ] 2.2 Extend the `hello` message variant in `ServerMessageSchema` (or its sibling schema) with `theme: z.optional(z.object({ factionDieColors: z.optional(...) }))`. Permit `theme` as a delta entry on state messages too.
- [ ] 2.3 Regenerate `schema/gamestate.schema.json`. Add a drift-test case covering a die with `locked: true`, a die without `locked`, and a hello with and without the theme block.
- [ ] 2.4 `cd frontend && npm test` passes including the new drift cases.

## 3. Frontend theme handling

- [ ] 3.1 Add `--die-ally-fill` and `--die-enemy-fill` defaults to `app.vue`'s `:root` block (hardcoded approximations: `#3aaad8` ally / `#d83a6d` enemy, adjustable based on the sampled values).
- [ ] 3.2 In `composables/useWebSocket.ts`, on receipt of a hello (and on any later state message with a `theme` block), write `theme.factionDieColors.ally`/`enemy` to `document.documentElement.style` as the corresponding CSS custom properties.
- [ ] 3.3 Add a unit test for the theme-cache helper: given a hello payload with theme, it writes the two CSS vars; given a hello without theme, it leaves the vars untouched.

## 4. DieRow visual updates

- [ ] 4.1 Refactor `components/unit/DieRow.vue` so the `.hex-inner` background uses `var(--die-ally-fill)` for ally rows and `var(--die-enemy-fill)` for enemy rows. Existing committed-state classes (broken / clash / unopposed-outgoing / unopposed-incoming / open / pending) continue to drive the outer `.hex-wrap` and their existing inner overrides.
- [ ] 4.2 Add a lock overlay element: `<span v-if="die.locked && !die.staggered" class="hex-overlay hex-lock"><svg .../></span>`. Style it as an absolutely-positioned, additive overlay on top of `.hex-inner` with `pointer-events: none`.
- [ ] 4.3 Add the locked-and-broken case: when `die.locked && die.staggered`, render the broken state as today AND also include the lock overlay (so the cause is visible).
- [ ] 4.4 Add a crosshatch overlay element: `<span v-if="isUntargetable" class="hex-overlay hex-untargetable"><svg .../></span>`. Style it as additive, `pointer-events: none`. Inject `isUntargetable` from a parent or compute from `unit.targetable === false`.
- [ ] 4.5 Verify with the existing fixture that no committed-state appearance regresses; the new overlays must not visually displace any existing affordance.

## 5. Stage-level untargetable chip and row dim

- [ ] 5.1 In `components/battle/Stage.vue` (or wherever the unit name and die rows are co-rendered), add the "⚠ untargetable" chip near the name when `unit.targetable === false`. Match the existing chip styling vocabulary.
- [ ] 5.2 Apply a row-level opacity reduction (~`0.6`) on untargetable rows. Confirm the chip itself remains at full opacity so the cue stays legible.
- [ ] 5.3 Verify on narrow viewports (mobile) that the chip fits without pushing the dice off-screen.

## 6. Out-of-battle preview surfaces

- [ ] 6.1 Update `components/battle/SettingView.vue` preview-die rendering to use the faction-fill CSS var.
- [ ] 6.2 Update `components/librarian/KeyPageDetail.vue` preview-die rendering to use the faction-fill CSS var.
- [ ] 6.3 Confirm neither surface ever attempts to render lock or crosshatch overlays.

## 7. Fixture and visual verification

- [ ] 7.1 Extend `frontend/app/dev/fixtures/battle-sampler.json` with: a die that has `locked: true, staggered: false`; a die that has `locked: true, staggered: true`; an enemy unit with `targetable: false` and at least one rolled die.
- [ ] 7.2 Add the `theme.factionDieColors` block to the hello-like setup the fixture loader uses (or wire the loader to inject the CSS vars manually for fixture mode).
- [ ] 7.3 `npm run dev` from `frontend/`, load the fixture, visually confirm: faction-fill colours, lock glyph preserves colour underneath, crosshatch preserves colour and value underneath, chip is visible near the untargetable unit's name.

## 8. Final validation

- [ ] 8.1 `cd mod && dotnet build` — `0 Warning(s) 0 Error(s)`.
- [ ] 8.2 `cd frontend && npm test` — all suites pass.
- [ ] 8.3 Live test: launch LoR with the local mod build, take a stage with at least one paralysis-bearing unit or untargetable enemy, confirm the cues appear in the web UI.
- [ ] 8.4 Update `MEMORY.md` with any non-obvious reflection lookup pattern that future sessions would benefit from knowing (e.g. the `SpeedDiceUI.Refs.color_allyDice` field path).
