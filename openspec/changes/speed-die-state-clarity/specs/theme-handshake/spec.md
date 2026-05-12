## ADDED Requirements

### Requirement: The `hello` payload SHALL carry a one-shot `theme` block

The server's `hello` message MUST include an optional top-level `theme` object containing runtime-sampled visual constants. The block is one-shot: clients cache its contents into CSS custom properties and do not expect the block to be re-sent on later messages.

The `theme.factionDieColors` sub-object MUST carry:

- `ally: string` — `#rrggbb` lowercase hex sampled from `SpeedDiceUI.Refs.color_allyDice`
- `enemy: string` — `#rrggbb` lowercase hex sampled from `SpeedDiceUI.Refs.color_enemyDice`

When the C# probe cannot sample the prefab (e.g. before any battle scene has loaded), the entire `theme` block MAY be omitted from the `hello`. Clients MUST tolerate the missing block by falling back to hardcoded defaults declared in `app.vue`'s `:root`.

When the probe succeeds, both `ally` and `enemy` MUST be present (no partial blocks). The mod MAY retry the probe on the first state push after a battle scene loads and include `theme.factionDieColors` in that snapshot's incremental payload via a `theme` delta entry.

#### Scenario: Hello carries faction die colours when probe succeeds

- **WHEN** the C# probe sampled `color_allyDice` and `color_enemyDice` before the hello is sent
- **THEN** the hello message contains `theme: {factionDieColors: {ally: "#xxxxxx", enemy: "#xxxxxx"}}`
- **AND** the frontend caches the two hex strings into `--die-ally-fill` and `--die-enemy-fill` on `document.documentElement.style`

#### Scenario: Hello omits theme when probe has not yet succeeded

- **WHEN** the C# probe was unable to find a `SpeedDiceUI` instance at hello-emit time
- **THEN** the hello message has no `theme` block (or `theme` is absent / null)
- **AND** the frontend's CSS reads `var(--die-ally-fill, <hardcoded-default>)` and the UI renders with the hardcoded defaults
- **AND** no console error is produced

#### Scenario: Late probe success arrives on a state push

- **WHEN** the C# probe succeeds after the hello has been sent (e.g. on first battle scene load)
- **THEN** the next state push MAY include `theme: {factionDieColors: {...}}` as a delta block
- **AND** the frontend overrides its cached CSS vars on receipt without disrupting any other rendering

### Requirement: The C# probe SHALL sample `SpeedDiceUI` prefab fields via reflection

The mod MUST find a `SpeedDiceUI` instance via `Resources.FindObjectsOfTypeAll<SpeedDiceUI>()`, then reflect into the private `Refs` field (`BindingFlags.NonPublic | BindingFlags.Instance`), then read the `color_allyDice` and `color_enemyDice` `UnityEngine.Color` fields off the struct. Each `Color` MUST be converted to a `#rrggbb` lowercase hex string with no alpha.

When `Resources.FindObjectsOfTypeAll<SpeedDiceUI>()` returns an empty array, the probe MUST defer to a later retry rather than failing. When the reflection lookup fails for any other reason, the probe MUST log a single `[ThemeProbe]` warning to the player's log and use hardcoded fallback values for the rest of the session.

The probe MUST NOT add any compile-time reference beyond what the mod already references (`Assembly-CSharp.dll`, Unity standard libraries).

#### Scenario: Probe successfully samples prefab colours

- **WHEN** the probe runs after at least one `SpeedDiceUI` prefab has been instantiated (typically post-first-scene-load)
- **THEN** the probe reads `Refs.color_allyDice` and `Refs.color_enemyDice` via reflection
- **AND** the two `Color` values are converted to `#rrggbb` hex strings

#### Scenario: Probe defers when no prefab is loaded yet

- **WHEN** the probe runs before any `SpeedDiceUI` has been instantiated
- **THEN** the probe records the deferred state and returns null
- **AND** the next state-push code path is permitted to retry the probe
- **AND** no warning is logged for this case

#### Scenario: Probe fails permanently after reflection error

- **WHEN** the probe finds a `SpeedDiceUI` instance but the `Refs` field is missing or not reflectable
- **THEN** a single `[ThemeProbe]` warning is logged with the underlying exception text
- **AND** subsequent state pushes MUST NOT retry the probe
- **AND** the frontend continues to render using hardcoded fallback colours
