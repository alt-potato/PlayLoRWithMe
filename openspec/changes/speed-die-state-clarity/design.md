## Context

`LOR_DiceSystem.SpeedDice` exposes two booleans we currently ignore in the wire contract:

- `breaked` — we emit as `staggered` and render as ✕
- `isControlable` — never sampled

Plus `BattleUnitBufListDetail.IsControlable()` returns false when the unit has at least one buff whose `IsControllable == false` (paralysis, stun, mind control, certain self-attack effects). The game's `SpeedDiceUI.BreakDice(breaked, locked)` activates `_lockDiceRoot` instead of `_breakDiceRoot` when `breaked && locked`; `locked` itself is computed from `!unit.bufListDetail.IsControlable() || !die.isControlable`.

`unit.targetable` is already on the wire (used by `TargetPicker`) but the main-stage `DieRow.vue` does not visually reflect it — `canBeTargeted = isTargeting && unit.targetable` only gates the green hover affordance during active targeting.

Faction colours live on the `SpeedDiceUI` prefab in a private `Refs` struct field (`color_allyDice`, `color_enemyDice`). They are designer-set Unity `Color` values — not hardcoded constants in code, but stable across vanilla LoR versions. We can reach them at runtime via `Resources.FindObjectsOfTypeAll<SpeedDiceUI>()` plus a single reflection read.

## Goals / Non-Goals

**Goals:**

- Emit `locked: boolean` on each `SpeedDie` so the frontend can distinguish "this die can't be commanded right now" from "this die was destroyed" — even when both are true at the same time.
- Show the locked state proactively (when a unit has paralysis but its dice haven't broken yet), not just after the break — remote players need the cue earlier than the game's UI provides it.
- Match vanilla LoR's per-faction die colours so the inner-hex fill is informative at a glance. Source the values from the game prefab to track any future tuning by Project Moon.
- Make the untargetable state visible on the main stage, not only in the mobile `TargetPicker` fallback.
- Keep all overlays *additive*: the lock and untargetable affordances must not erase the underlying faction tint or rolled value, per the user's explicit constraint.

**Non-Goals:**

- Per-die untargetable distinction. The game's targetable state is unit-level; we don't need finer granularity than that.
- Animated lock / untargetable effects (shimmer, pulse). Static visuals match our existing design vocabulary.
- Exposing `unit.bufListDetail.IsControlable()` separately from per-die `isControlable`. The frontend doesn't need the distinction — it only needs to know whether *this die* is locked.
- Rebuilding the existing `staggered → ✕` behaviour. A broken die that is also locked still renders as broken (matches in-game `_breakDiceRoot` precedence over the lock root when `!locked`, but we ship a lock glyph for the `breaked && locked` case too).

## Decisions

### Locked semantics

**Decision:** `SpeedDie.locked` on the wire is `(!unit.bufListDetail.IsControlable()) || (!die.isControlable)`. The frontend renders the lock glyph whenever `locked === true`, regardless of `staggered`.

The visual layering on a locked die:

```
  hex inner = faction-coloured (always)
  + lock glyph SVG overlay centred on the hex
  + rolled value still rendered behind the glyph at reduced opacity
```

If the die is *also* `staggered`, the broken-state crimson tint takes precedence (matches in-game `BreakDice(true, true)` rendering — locked-and-broken shows as locked). The frontend computes a derived `dieState` of `"locked"` for `locked && !staggered` and `"broken-locked"` for `locked && staggered`.

**Alternative considered:** ship two separate fields (`unitLocked`, `dieLocked`). Rejected — the frontend treatment is identical for both, and merging at the serializer keeps the wire contract minimal.

**Alternative considered:** only show lock when the die is also broken (vanilla-faithful). Rejected — remote players miss the cue during the planning phase, which is exactly when they most need it.

### Untargetable visual

**Decision:** `unit.targetable === false` adds a row-level visual treatment in `DieRow.vue`:

- Row opacity reduced to `0.6` (preserves visibility but signals "not selectable")
- A small "⚠ untargetable" chip rendered near the unit name (sourced via context injection from the parent `Stage.vue` so we don't duplicate the chip on every die)
- Each die in the row gains a crosshatch SVG mask overlay (low-opacity diagonal stripes) on top of the faction-coloured hex inner

The crosshatch is the visual differentiator from "this row is just dimmed because it's far from focus" — diagonal stripes specifically signal "no entry" / "untargetable" without erasing the underlying state.

**Alternative considered:** strike-through the unit name. Rejected — collides with the existing "dead" treatment.

**Alternative considered:** full-opacity shield overlay on each die. Rejected per the user's "should not completely hide the colour" constraint.

### Faction colour sourcing

**Decision:** at mod init (after the main scene loads, same lifecycle hook that warms `IconCache`), find any `SpeedDiceUI` instance via `Resources.FindObjectsOfTypeAll<SpeedDiceUI>()`, reflect into the private `Refs` field (`BindingFlags.NonPublic | BindingFlags.Instance`), then read `color_allyDice` and `color_enemyDice` from the struct. Convert each `Color` (Unity 0–1 floats) to `#rrggbb` lowercase hex. Cache the two strings on a static `Theme` class.

The serializer attaches `theme.factionDieColors: {ally: "#xxxxxx", enemy: "#xxxxxx"}` to the next `hello` payload after init completes. The hello message already carries session state and other one-shot setup data; the `theme` block is a natural home for runtime-sampled visual constants.

The frontend receives the `hello` and caches the two strings into CSS custom properties on `document.documentElement.style`: `--die-ally-fill`, `--die-enemy-fill`. `DieRow.vue` reads `var(--die-ally-fill, <hardcoded-default>)` on ally rows and `var(--die-enemy-fill, <hardcoded-default>)` on enemy rows.

**Alternative considered:** per-unit `factionDieColor` on every state push. Rejected — the colours are stable for a session; per-unit duplication wastes bytes.

**Alternative considered:** read once and emit as a CSS variable on the served HTML. Rejected — couples to `wwwroot/index.html` rewrites, which the rest of the system doesn't do.

**Fallback:** if `Resources.FindObjectsOfTypeAll<SpeedDiceUI>()` returns empty (no prefab loaded yet) at init time, the helper retries on the first state push after a battle scene loads. If reflection ever fails outright, log a single `[ThemeProbe]` warning and ship hardcoded defaults (`#3aaad8` ally / `#d83a6d` enemy — eyeballed approximations).

### Overlay composition order in `DieRow.vue`

**Decision:** the existing CSS state classes (`.broken`, `.clash`, `.unopposed-outgoing`, `.unopposed-incoming`, `.open`, `.pending`, `.available`) continue to drive the *outer* `.hex-wrap` background. They represent committed game state and target attachment — louder than the lock/untargetable cues.

The *inner* `.hex-inner` background changes to `var(--die-faction-fill)` for the new empty / normal / locked / untargetable states. When the die enters one of the existing committed-state classes (broken / clash / etc.), the inner background follows the existing rules (so a clash still looks like a clash even if the unit is also untargetable).

New layered DOM:

```vue
<span class="hex-wrap" :class="dieState">
  <span class="hex-inner">{{ dieDisplayValue }}</span>
  <span v-if="die.locked && !die.staggered" class="hex-overlay hex-lock" aria-label="locked">
    <!-- lock SVG, additive over hex-inner -->
  </span>
  <span v-if="isUntargetable" class="hex-overlay hex-untargetable">
    <!-- crosshatch SVG mask, additive -->
  </span>
</span>
```

The two overlay spans are absolutely positioned over `.hex-inner`. They get `pointer-events: none` so clicks still hit the slot interaction layer.

### Untargetable chip placement

**Decision:** `Stage.vue` (or whichever component is the parent of `DisplayCard`) renders the chip next to the unit name when `unit.targetable === false`. `DieRow.vue` does not render its own copy — the chip is at the row-grouping level, and the per-die crosshatch is the inline cue. This keeps the chip from duplicating on multi-die rows.

### Out-of-battle preview dice

**Decision:** the `SettingView` formation/deck preview and the `KeyPageDetail` view both render "preview" dice — placeholder representations of speed-die slots that haven't rolled yet. These inherit the faction-coloured fill (so the visual cue is consistent in/out of battle) but never display lock or untargetable overlays — there's no battle context for either signal. The `locked` field on a preview die is always `false` or absent on the wire.

## Risks / Trade-offs

[Prefab name drift / Project Moon rename] → if `SpeedDiceUI.Refs` is renamed in a future LoR patch, the reflection probe returns null and we fall back to hardcoded defaults. The fallback colours diverge slightly from vanilla but the cue still works. We log once so the maintainer notices.

[Resources.FindObjectsOfTypeAll returns multiple SpeedDiceUI instances] → all are prefab-clones with the same `Refs` value. We pick the first non-null result; if the colours ever differ between instances, we'd see a visual mismatch but no crash.

[Crosshatch + faction-tint contrast] → diagonal stripes on a bright magenta enemy hex may not be high-contrast enough to read. Mitigation: design crosshatch as a white-stripe SVG mask with a slight outer glow; test against both ally and enemy fills with the fixture.

[Per-die locked field on every push] → `locked` is a boolean per die, so the wire cost is one byte per die per push. Negligible compared to the existing `staggered` field.

[Backwards-compatible delta] → DeltaEngine should drop `locked: false` from snapshots when nothing has changed. Verify this works for the new field.

[Mobile viewport] → the untargetable chip near the unit name needs to fit on narrow viewports without pushing the dice out of frame. Match the existing chip-style components (`.tag.rarity-tag` and friends) for sizing.

## Migration Plan

1. Add `mod/ThemeProbe.cs` (or fold into the existing init flow) that samples `SpeedDiceUI.Refs.color_allyDice` and `color_enemyDice` once at scene-ready. Cache as two `string` hex values.
2. Extend the `hello` payload writer in `GameStateSerializer.cs` with a `theme.factionDieColors` block. Mirror the structure on the frontend schema.
3. Add `locked: boolean` emission in `WriteSpeedDice`. Update `SpeedDieSchema` in `game.ts`.
4. Add the new CSS vars (`--die-ally-fill`, `--die-enemy-fill`) to `app.vue`'s `:root` block with hardcoded defaults; `useWebSocket.ts` overrides them on hello.
5. Refactor `DieRow.vue` inner hex to use the faction-coloured var; layer in the lock and crosshatch overlay elements.
6. Add the untargetable chip in `Stage.vue` (or `unit/DisplayCard.vue`, whichever currently renders the unit name).
7. Extend `battle-sampler.json` with a locked-and-not-broken die, a locked-and-broken die, and an untargetable enemy unit. Visually verify in dev.
8. Spec deltas: new `battle-die-state` capability, new `theme-handshake` capability, updates to `wire-contract-schema`.

Rollback: revert commits. `locked` defaults to `false`, `theme.factionDieColors` is optional, so older snapshots and the absence of either feature work cleanly.

## Open Questions

- Should the lock overlay use the in-game lock sprite (extracted via the same prefab probe) or a stylised CSS/SVG lock glyph? Stylised is simpler and lets us tune contrast independently; sprite would match in-game exactly. Leaning **stylised** for v1.
- For untargetable allies (does this ever happen in vanilla LoR?): the chip + crosshatch treatment should apply to allies too if so. Out of scope unless someone surfaces a concrete case.
- Should `theme.factionDieColors` also expose the line / range colour fields from `SpeedDiceUI.Refs` for use by `ArrowOverlay`? Future work — out of scope for this change.
