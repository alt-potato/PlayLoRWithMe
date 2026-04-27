# color-tokens Specification

## Purpose
TBD - created by archiving change replace-inline-hex-colors. Update Purpose after archive.
## Requirements
### Requirement: Components SHALL use centralized CSS custom-property tokens for color

Component `<style>` blocks in `frontend/app/components/**/*.vue` MUST reference colors via `var(--token-name)` where `--token-name` is defined in the `:root` block of `frontend/app/app.vue`. Inline hex literals (`#xxxxxx` or `#xxx`) in `<style>` blocks are forbidden, with two explicit exceptions:

- `#fff` (and the equivalent `#ffffff`, `white`) MAY appear inline since pure white is universal and rarely benefits from indirection.
- `#000` (and the equivalent `#000000`, `black`) MAY appear inline for the same reason.

This rule applies to color values only — non-color hex tokens (e.g. `0x` prefixes in JS) are unaffected. It applies only to `<style>` blocks within `.vue` files; hex literals inside `<script>` blocks (used for data such as the ally-color palette) and inside `.ts`/`.js` files are out of scope.

#### Scenario: A component uses a tokenized color

- **WHEN** a Vue component declares `color: var(--text-page);` in its `<style>` block
- **AND** `--text-page` is defined in `app.vue`'s `:root` block
- **THEN** the component renders with the value defined centrally
- **AND** changing the central token value updates the component without a per-file edit

#### Scenario: A component uses an inline hex literal

- **WHEN** a Vue component declares `color: #f5efde;` in its `<style>` block
- **AND** `#f5efde` is not the universal `#fff` or `#000` exception
- **THEN** the change is non-compliant with this requirement
- **AND** code review SHOULD ask the author to replace the literal with a `var(--token)` reference, adding a new token if needed

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

