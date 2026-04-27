# Replace inline hex colors with CSS custom-property tokens

## Why

Across `frontend/app/components/**/*.vue` there are 25 inline `#hexvalue` literals (22 distinct values) inside scoped `<style>` blocks. The project already has a comprehensive CSS custom-property design system in `app.vue`'s `:root` — these literals predate the system or were added piecemeal. Every inline hex is a missed opportunity for semantic naming and makes the design system inconsistent: a future palette adjustment requires hunting through 12 component files instead of editing the central token set.

The audit (top-3 codebase finding #2) flagged this as the single largest surface-area cleanup with no behaviour risk.

## What Changes

- Add 19 new CSS custom properties to the `:root` block in `frontend/app/app.vue`, grouped by semantic family (die-text tints, mass-target badge, combat hex states with `-hover` siblings, gold-panel/slot states, buff borders, crimson surfaces, broken state).
- Replace 23 of the 25 inline hex literals across 12 component files with `var(--token)` references. The 2 remaining inline literals are `#fff` (used 6× in hover text states) and `#000` (used 1× in a button hover), kept inline per scoping decision — pure black/white are universal enough that tokenizing them adds noise without value.
- Reuse 3 existing tokens where semantics align: `--text-green` (exact match for `#81c784`), `--green-hi` (close enough for `#2e5c2e` buff border), and the new `--bg-crimson-deep` consolidates two near-identical literals (`#1a0505` and `#180808`).

Out of scope:
- Inline hex in JS/TS files (e.g. `useBattleDisplay.ts` ally-color array, `useLibrarianActions.ts` `FLOOR_COLORS` map). These are data, not theme.
- The internal hex values inside `librarian/customize/HslColorPicker.vue` — that's a colour-picker tool whose colors are domain values, not theme tokens.
- Restructuring the existing `:root` block organization or naming conventions for already-tokenized colours.

## Capabilities

- **New:** `color-tokens` — establishes the rule that components MUST use the centralized token set rather than inline hex literals, with explicit exceptions for pure white/black.

## Impact

- Affected code: 1 file (`app.vue`) plus 12 component files (~25 line changes total).
- Affected tests: none — no behaviour change. Visual diff should be zero.
- Risk: incorrect token mapping could shift a colour by a few RGB values in some component. Mitigation: each replacement is a literal-to-token swap of the exact same hex; the only collapses are explicitly noted (`#180808` → `--bg-crimson-deep` (#1a0505), `#2e5c2e` → `--green-hi` (#2e7d32)). Both deltas are within ~3 RGB units, imperceptible.
