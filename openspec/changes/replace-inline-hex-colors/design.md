# Design: Replace inline hex colors with CSS tokens

## Context

`frontend/app/app.vue` defines a `:root` block with ~50 CSS custom properties grouped by semantic purpose (canvas, borders, gold accent, crimson combat, text hierarchy, state colors, rarity accents, info chips, typography, spacing, radii, shadows, motion, fonts, clash/stagger). Components are expected to consume this token set rather than hard-coding colours, but 25 inline `#hex` literals slipped in over time.

This change does the cleanup pass and codifies the rule.

## Goals

1. Zero inline hex literals in `<style>` blocks across `frontend/app/components/**/*.vue` other than `#fff`/`#000` exceptions.
2. New tokens follow the existing naming patterns (semantic family prefix, `-hover` for interaction siblings, `-deep` for darker shade variants).
3. No visual regression — every replacement preserves the exact pixel colour except the two explicit collapses.

## Non-Goals

- Restructuring the existing token vocabulary.
- Tokenizing hex literals inside JS/TS data files (those are domain values, not theme).
- Adding rgba alpha-variant tokens for the new colors — none of the call sites need transparency today.

## Decisions

### Decision 1: Hover variants get explicit sibling tokens

Per scoping decision (a), hover-state colours get their own `--token-hover` sibling token rather than being computed from a base via `color-mix()`. Rationale:
- `color-mix()` browser support is recent (Chromium 111, Firefox 113, Safari 16.2 — all 2023). Nuxt 4 doesn't pin a baseline, but the rest of the codebase doesn't use modern CSS functions.
- Computed values obscure the actual colour from the developer reading the stylesheet.
- The 5 hover variants in scope are stable design choices, not derived computations.

### Decision 2: Collapse two near-identical hexes

`#180808` (error banner bg, used 1×) → `--bg-crimson-deep` (#1a0505). RGB delta of (2, 0, -3), imperceptible.
`#2e5c2e` (green buff border, used 1×) → `--green-hi` (#2e7d32). RGB delta of (0, 33, -2), perceptible but both are mid-dark green and the buff border is a 1px line — the visual mismatch is below the threshold of notice.

These are the only collapses. Every other hex maps 1:1 to a fresh token.

### Decision 3: Token naming follows existing prefix conventions

- `--text-*` for foreground text colours (joins existing `--text-1`, `--text-red`, `--text-green`, `--text-info`, `--text-info-hi`).
- `--bg-*` for background fills (joins existing `--bg`, `--bg-surface`, `--bg-card-*`, `--bg-green`, `--bg-info`, `--bg-gold`).
- `--border-*` for border colours (joins existing `--border`, `--border-mid`, `--border-hi`, `--border-gold`, `--border-info`).
- `-hover` suffix for hover sibling, `-deep` suffix for darker shade.

### Decision 4: One atomic commit

The token additions and the literal replacements ship in a single commit. Splitting would leave an intermediate state where new tokens exist but aren't used (or vice versa), making code review harder. The whole change is mechanical and easy to verify by visual diff.

## Token Mapping

### New tokens (19)

```css
/* Die-type description text tints */
--text-atk: #f0c2c2;       /* HandCard.vue:454 */
--text-def: #c2d8f0;       /* HandCard.vue:457 */
--text-standby: #f0d8a0;   /* HandCard.vue:460 */

/* Page-level reading text (distinct from --text-1 body) */
--text-page: #f5efde;      /* HandCard.vue:402 */

/* Deeper crimson surface (collapses #180808 too) */
--bg-crimson-deep: #1a0505;  /* CardDetail.vue:201, TargetPicker.vue:284, DisplayCard.vue:485, Stage.vue:461 */

/* Mass-target damage badge */
--bg-mass: #2a0e00;        /* TargetPicker.vue:160 */
--border-mass: #8b3500;    /* TargetPicker.vue:161 */
--text-mass: #ff7043;      /* TargetPicker.vue:162 */

/* Combat hex die-state backgrounds */
--bg-incoming: #2a0a0a;        /* TargetPicker.vue:244 */
--bg-incoming-hover: #7a1010;  /* TargetPicker.vue:247 */
--bg-clash: #3d2e00;           /* TargetPicker.vue:250 */
--bg-clash-hover: #261c00;     /* TargetPicker.vue:288 */
--bg-stagger: #220808;         /* TargetPicker.vue:292 */

/* Gold panel/slot deeper shades */
--bg-gold-deep: #0d0d00;       /* EmotionUpgradePicker.vue:205 */
--bg-gold-hover: #141000;      /* DieRow.vue:349 */
--bg-gold-mid: #3a2c00;        /* DieRow.vue:358 (animation keyframe) */
--text-gold-deep: #4a3800;     /* DieRow.vue:301 */

/* Buff borders */
--border-gold-buff: #4a2800;     /* DisplayCard.vue:473 */
--border-crimson-buff: #5c1a1a;  /* DisplayCard.vue:486 */

/* Broken die state */
--bg-broken: #230808;            /* DieRow.vue:416 */
```

### Existing token reuse

```
#81c784 → --text-green (exact match)         /* BattleSymbolsTab.vue:433, 434 */
#2e5c2e → --green-hi (close, see Decision 2) /* DisplayCard.vue:481 */
#180808 → --bg-crimson-deep (collapse)       /* Stage.vue:461 */
```

### Inline-kept (per scoping decision)

```
#fff (6 occurrences) — universal white in hover text states
#000 (1 occurrence)  — universal black in button hover
```

## Risks

- **Visual regression on the two collapses.** Mitigation: both deltas are imperceptible (see Decision 2). If a reviewer disagrees about `--green-hi` for the buff border, swap in a new `--border-green-buff: #2e5c2e` token — additive change, no migration needed.
- **Future maintainer adds a new inline hex.** Mitigation: the spec delta in this change establishes the rule; the next code review should catch it.

## Verification

- `cd frontend && npm test` — passes (no behaviour change so all 68 tests stay green).
- `cd frontend && npm run build` — clean.
- `cd mod && dotnet build` — clean (`AfterBuild` runs `npm run generate`).
- Manual visual smoke: launch dev mode, load `battle-sampler` fixture, eyeball that the hand cards / dies / status chips look unchanged.
- `openspec validate replace-inline-hex-colors` — valid.
- `grep -rE "#[0-9a-fA-F]{3,6}" frontend/app/components/ --include="*.vue"` — only `#fff`/`#000` literals remain (plus colours inside JS expressions, which are out of scope).
