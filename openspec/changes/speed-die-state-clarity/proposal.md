## Why

Speed dice on the battle stage today carry only two states the player can read at a glance: rolled-value-with-faction-neutral-hex, or `staggered` → ✕. The vanilla game distinguishes three more states the remote-play UI currently collapses into "looks fine":

- **Faction-coloured base** (vanilla ally dice are teal-ish, enemy dice are magenta-ish; our hex inner is the same neutral panel colour for both). Loses an at-a-glance "whose side is this" cue.
- **Locked / immobilized** (`!unit.IsControlable()` from a paralysis-class buff, or per-die `!isControlable`). In-game these render with a distinct lock frame; in our UI they look exactly like a normal rolled die.
- **Untargetable** (`unit.targetable === false`, e.g. Justitia-class invincibility). Today only the narrow-viewport `TargetPicker` reflects this; on the main stage the enemy looks freely clickable until the player actually tries to assign a target.

For remote players who can't see the in-game animation cues (paralysis shimmer, untargetable shield), the missing affordances cost real turns.

## What Changes

- Speed-die hex inner fill matches the vanilla per-faction colour. Colours are sampled at mod init from `SpeedDiceUI`'s prefab via reflection (the prefab's `Refs.color_allyDice` / `Refs.color_enemyDice` `Color` fields) and shipped once on the `hello` payload as a `theme.factionDieColors` block. Frontend caches them into CSS vars at receive time and falls back to hardcoded defaults if reflection ever returns null.
- The serializer emits a new optional `locked: boolean` on each `SpeedDie`, derived as `(!unit.bufListDetail.IsControlable()) || (!die.isControlable)`. When `true` (and the die is not also broken), the die renders a centred lock glyph in place of the rolled value; the underlying faction-coloured inner-hex fill remains visible around the glyph. Locked dies are not selectable for slotting (the click guard mirrors the staggered/broken-unit guard).
- Untargetable enemy units (`unit.targetable === false`) gain a stage-level visual treatment in `DieRow.vue`: a row-level dim with a small "⚠ untargetable" chip near the unit name, plus a crosshatch SVG mask overlay on each die. The crosshatch is additive — the faction tint and rolled value remain visible underneath.
- The broken state takes priority over the locked state: a die that is both `staggered` and `locked` renders the broken ✕ on the crimson hex with no lock glyph overlay. The locked and untargetable affordances compose with each other (a locked-and-untargetable die shows both lock glyph and crosshatch).
- Out-of-battle preview surfaces (`SettingView`, `KeyPageDetail`) currently render speed as `min–max` text only — no die graphics to update.
- Soft-dependency compatibility with the `Patty_SpeedDiceColor_MOD` workshop mod (id 2746914901): when present, per-unit dice colour entries flow through as an optional `dieColor: "#rrggbb"` on each unit's wire payload, overriding the faction default. No graphics swaps are honoured (out of scope) — only the colour.

## Capabilities

### New Capabilities

- `battle-die-state`: contract for how speed dice render their state — faction-coloured base fill, locked overlay, untargetable overlay, and the interaction between these and existing combat overlays (clash, broken, etc.).
- `theme-handshake`: a small one-shot block on the `hello` message that lets the C# side ship runtime-sampled visual constants (faction die colours today; potentially more later) without per-state churn.

### Modified Capabilities

- `wire-contract-schema`: admits the new optional `SpeedDie.locked: boolean` and the new `theme.factionDieColors: {ally: string, enemy: string}` block on the `hello` payload.

## Impact

- **C#**: new helper sampling `SpeedDiceUI.Refs.color_allyDice` / `color_enemyDice` via reflection at init (or first scene-load); emit on `hello`. `WriteSpeedDice` in `GameStateSerializer.cs` gains a `locked` field per die. ~30 lines plus a small reflection helper. No new project references.
- **Frontend**: `useWebSocket.ts` caches `theme.factionDieColors` into CSS vars (`--die-ally-fill`, `--die-enemy-fill`) on receive. `DieRow.vue` adds inline-style binding for the faction colour and two new overlay elements (lock glyph, crosshatch mask). Schema additions and a small fixture update.
- **Tests**: extend `useBattleDisplay.test.ts` to cover the new die states; new fixture cases for locked + untargetable dice in `battle-sampler.json`.
- **Dev workflow**: no new dependencies; fall back to hardcoded defaults if reflection fails (logged once at startup).
