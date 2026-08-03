# color-tokens Specification

## Purpose
TBD - created by archiving change replace-inline-hex-colors. Update Purpose after archive.
## Requirements
### Requirement: Components SHALL use centralized CSS custom-property tokens for color

Component `<style>` blocks in `frontend/app/components/**/*.vue` MUST reference colors via `var(--token-name)` where `--token-name` is defined in the `:root` block of `frontend/app/app.vue`, OR is set inline via a Vue `:style` binding when the value derives from runtime data (e.g. a rarity hex received from the server). Inline hex literals (`#xxxxxx` or `#xxx`) in `<style>` blocks are forbidden, with two explicit exceptions:

- `#fff` (and the equivalent `#ffffff`, `white`) MAY appear inline since pure white is universal and rarely benefits from indirection.
- `#000` (and the equivalent `#000000`, `black`) MAY appear inline for the same reason.

This rule applies to color values only — non-color hex tokens (e.g. `0x` prefixes in JS) are unaffected. It applies only to `<style>` blocks within `.vue` files; hex literals inside `<script>` blocks (used for data such as the ally-color palette, or for inline `:style` bindings that carry runtime colour values) and inside `.ts`/`.js` files are out of scope.

Per-rarity helper classes (e.g. `.rarity-rare`) that set a `border-left-color` directly to a `--rarity-*` token are deprecated in favour of the `--rarity-color` inline-var convention. New rarity-styled components MUST follow the inline-var pattern.

#### Scenario: A component uses a tokenized color

- **WHEN** a Vue component declares `color: var(--text-page);` in its `<style>` block
- **AND** `--text-page` is defined in `app.vue`'s `:root` block
- **THEN** the component renders with the value defined centrally
- **AND** changing the central token value updates the component without a per-file edit

#### Scenario: A component sets a token inline from runtime data

- **WHEN** a Vue component binds `:style="{'--rarity-color': resolvedColour}"` on an element
- **AND** its `<style>` block reads `border-left-color: var(--rarity-color, var(--border));`
- **THEN** the rule is compliant with this requirement
- **AND** no inline hex literal appears in the `<style>` block

#### Scenario: A component uses an inline hex literal

- **WHEN** a Vue component declares `color: #f5efde;` in its `<style>` block
- **AND** `#f5efde` is not the universal `#fff` or `#000` exception
- **THEN** the change is non-compliant with this requirement
- **AND** code review SHOULD ask the author to replace the literal with a `var(--token)` reference or an inline-var binding, adding a new token if needed

### Requirement: Hover-state color variants SHALL be expressed as `-hover` sibling tokens

When a component needs a hover-state colour that differs from a base tokenized colour, the hover variant MUST be defined as a sibling token suffixed `-hover` (e.g. `--bg-clash-hover` paired with `--bg-clash`) rather than computed inline via `color-mix()`, `filter`, or hard-coded near-duplicate hex values.

This keeps the design vocabulary explicit, lets `color-mix()` adoption happen as a future pass once browser baselines are confirmed, and avoids rgba-percentage drift between components.

#### Scenario: A component styles a hover state on a tokenized base

- **WHEN** a `:hover` rule needs a darker variant of `var(--bg-incoming)`
- **THEN** the rule MUST reference `var(--bg-incoming-hover)` defined in `app.vue`
- **AND** MUST NOT use `color-mix(...)`, `filter: brightness(...)`, or a literal hex value

### Requirement: New colors SHALL be declared in `app.vue`'s `:root` block

When a new colour value is needed by any component, the value MUST be added as a new custom property in the `:root` block of `frontend/app/app.vue`, placed in (or near) the most semantically related existing token group.

The token name MUST follow the established prefix conventions:
- `--text-*` for foreground text colours
- `--bg-*` for background fills
- `--border-*` for border colours
- Suffix `-hover` for interaction siblings, `-deep` for darker shade variants, `-hi` for brighter

#### Scenario: A developer needs a new accent colour for a component

- **WHEN** a component design needs a colour not already in the token set
- **THEN** the developer adds a new property to `app.vue`'s `:root` block
- **AND** uses a name following the established prefix conventions
- **AND** references it via `var(--new-token)` from the component

### Requirement: Rarity-styled surfaces SHALL read from a single `--rarity-color` inline-var

Every component that renders a rarity-derived border, glyph, or text colour MUST read from one of four CSS custom properties: `--rarity-color`, `--rarity-range-icon-color`, `--rarity-ability-color`, `--rarity-keyword-color`. These properties MUST be set inline via `:style` on the rarity-styled element by Vue components that resolve a rarity to a colour value (whether via a per-rarity class-based lookup or via a payload-supplied hex override).

Components consuming the vars MUST reference them with a default fallback: `var(--rarity-color, var(--border))`, `var(--rarity-range-icon-color, var(--gold))`, etc. Default tokens `--rarity-common`, `--rarity-uncommon`, `--rarity-rare`, `--rarity-unique`, `--rarity-special` remain defined in `app.vue`'s `:root` block for vanilla-rarity lookups but are no longer referenced via per-rarity classes inside individual components.

#### Scenario: A Vue component computes the rarity colour inline

- **WHEN** a Vue component renders a card with `rarity === "Rare"` and no payload override
- **THEN** the component element carries an inline style of `--rarity-color: var(--rarity-rare)`
- **AND** the component's CSS reads `border-left-color: var(--rarity-color, var(--border))`

#### Scenario: Payload supplies an override hex

- **WHEN** a Vue component renders a card whose payload includes `rarityColor: "#ff0000"`
- **THEN** the component element carries an inline style of `--rarity-color: #ff0000`
- **AND** the rendered border colour is red regardless of the rarity name

#### Scenario: Range-icon glyph reads its own var

- **WHEN** `CardRangeIcon` is rendered with `--rarity-range-icon-color` set on an ancestor
- **THEN** the glyph's SVG fill / stroke MUST resolve to that value
- **AND** when the var is absent, the glyph MUST fall back to `var(--gold)`

### Requirement: New rarity-sibling tokens SHALL be declared in `app.vue`'s `:root` block

`app.vue` MUST declare three new tokens in its `:root` block alongside the existing `--rarity-*` family:

- `--rarity-range-icon-color`: default value `var(--gold)` — used when no rarity override is in effect.
- `--rarity-ability-color`: default value matching the existing ability-description colour for that surface (typically `var(--text-2)`).
- `--rarity-keyword-color`: default value matching the existing bracketed-keyword highlight colour from `card-keyword-highlighting`.

These tokens MUST be declared, not just referenced, so that omitting an inline override leaves the surface looking exactly as it did before this change.

#### Scenario: Defaults are declared even when no override is in play

- **WHEN** a developer inspects `app.vue`'s `:root` block after this change
- **THEN** all four `--rarity-color` / `--rarity-range-icon-color` / `--rarity-ability-color` / `--rarity-keyword-color` defaults are declared
- **AND** their values match the pre-change visual rendering

### Requirement: Components SHALL use a centralized `--z-*` scale for stacking order

Component `<style>` blocks in `frontend/app/components/**/*.vue` and `frontend/app/dev/**/*.vue` MUST reference `z-index` via `var(--z-token-name)` where `--z-token-name` is defined in the `:root` block of `frontend/app/app.vue`. Bare numeric `z-index` literals in `<style>` blocks are forbidden.

Tokens MUST be named by the role they play in the stacking order (e.g. `--z-arrows`, `--z-overlay`, `--z-modal`), not by their numeric value, so the name documents intent independently of the number chosen. Two layers that are visually or functionally distinct MUST NOT share a token merely because they happen to use the same number — each layer that can be reasoned about independently gets its own named token, even if two tokens end up with adjacent or equal-looking values.

When two overlays are known to never render at the same time (e.g. one belongs to a battle-scene surface and the other to a library-scene surface), they MAY still share a numeric value only if that is a deliberate choice recorded at the point of definition; the default is to give every named layer a distinct value so the scale stays self-documenting.

#### Scenario: A component raises an element above its siblings

- **WHEN** a Vue component declares `z-index: var(--z-tooltip);` in its `<style>` block
- **AND** `--z-tooltip` is defined in `app.vue`'s `:root` block
- **THEN** the component renders at the layer the token names
- **AND** changing the central token value updates the component without a per-file edit

#### Scenario: A component uses a bare numeric z-index

- **WHEN** a Vue component declares `z-index: 200;` in its `<style>` block
- **THEN** the change is non-compliant with this requirement
- **AND** code review SHOULD ask the author to add or reuse a named `--z-*` token in `app.vue` instead

#### Scenario: Two overlays coincidentally share a number

- **WHEN** two components each need a full-viewport blocking overlay and neither can ever be visible while the other is
- **THEN** each MUST still get its own named `--z-*` token
- **AND** the token declarations MUST record why the values were chosen (equal, adjacent, or otherwise) so a future change that makes the two surfaces coexist does not have to rediscover the reasoning

### Requirement: Colors SHALL NOT be re-derived by hand as `rgba()`/`rgb()` literals

Component `<style>` blocks in `frontend/app/components/**/*.vue` MUST NOT restate a tokenized colour's channels as a hand-typed `rgba()` or `rgb()` literal (e.g. `rgba(201, 162, 39, 0.12)` for the `--gold` colour). When a component needs a translucent variant of an existing colour token, the value MUST come from a named alpha-variant token declared in `app.vue`'s `:root` block (e.g. `--gold-a12`, following the same suffix convention as `-hover`/`-deep`/`-hi`), not from restating the base token's RGB channels at the point of use.

This rule applies the same two exceptions as the base color-token requirement: `rgba(0, 0, 0, α)` and `rgba(255, 255, 255, α)` (pure black/white at any alpha) MAY appear inline, since they are not re-deriving a brand token. A `<style>` block MAY also declare a full-page backdrop scrim via a shared `--bg-scrim*` token rather than a per-component literal, so that near-duplicate scrim values do not drift apart unintentionally.

#### Scenario: A component needs a translucent brand colour

- **WHEN** a Vue component needs a faint gold wash for a hover state
- **THEN** the `<style>` block MUST reference an alpha-variant token such as `var(--gold-a12)`
- **AND** MUST NOT declare `background: rgba(201, 162, 39, 0.12);` inline

#### Scenario: A component uses neutral black/white shading

- **WHEN** a Vue component declares `filter: drop-shadow(0 0 1px rgba(0, 0, 0, 0.7));` for a non-brand neutral shadow
- **THEN** the rule is compliant with this requirement, matching the existing pure-black/white exception

#### Scenario: A component re-derives a brand colour by hand

- **WHEN** a Vue component declares `background: rgba(198, 40, 40, 0.08);` where `#c62828` (`--crimson-hi`) is already a token
- **THEN** the change is non-compliant with this requirement
- **AND** code review SHOULD ask the author to add or reuse a `--crimson-hi-aXX` alpha-variant token instead

