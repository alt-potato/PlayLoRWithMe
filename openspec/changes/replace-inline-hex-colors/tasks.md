# Tasks: Replace inline hex colors with CSS tokens

## 1. Add new tokens to the design system

- [x] 1.1 In `frontend/app/app.vue`'s `:root` block, add the 19 new tokens listed in `design.md` § "New tokens (19)". Group them with section comments matching the existing `/* ── ... ── */` style. Place each group near the most semantically related existing block (e.g. die-text tints near the existing `--text-*` family, combat hex backgrounds near the existing `--clash`/`--incoming` aliases).
- [x] 1.2 Verify the file still parses by running `cd frontend && npm run build` — confirm `Build complete`.

## 2. Replace literals in components

For each replacement, swap the inline `#hex` literal with `var(--token)`. Do NOT move declarations or restructure unrelated CSS. Use the exact line references from `design.md` as a checklist.

- [x] 2.1 `frontend/app/components/HandCard.vue` — lines 402, 454, 457, 460 (page text + 3 die-desc tints).
- [x] 2.2 `frontend/app/components/CardDetail.vue` — line 201 (`--bg-crimson-deep`).
- [x] 2.3 `frontend/app/components/battle/Stage.vue` — line 461 (banner-error bg → `--bg-crimson-deep` collapse).
- [x] 2.4 `frontend/app/components/TargetPicker.vue` — lines 160, 161, 162 (mass badge), 244, 247, 250, 284, 288, 292 (combat hex states + ego-tag bg). 9 replacements in this file.
- [x] 2.5 `frontend/app/components/unit/DieRow.vue` — lines 301, 349, 358, 416 (gold-text + slot-hover bg + animation keyframe + broken bg).
- [x] 2.6 `frontend/app/components/unit/DisplayCard.vue` — lines 473, 481, 485, 486 (buff borders × 3 + ego-tag bg). Note 481 maps to existing `--green-hi`.
- [x] 2.7 `frontend/app/components/EmotionUpgradePicker.vue` — line 205 (ab-header → `--bg-gold-deep`).
- [x] 2.8 `frontend/app/components/librarian/customize/BattleSymbolsTab.vue` — lines 433, 434 (positive stat chip → `--text-green`, exact match).

## 3. Validation

- [x] 3.1 `cd frontend && npm test` — all tests still pass (no behaviour change).
- [x] 3.2 `cd frontend && npm run build` — clean build.
- [x] 3.3 `cd frontend && npm run check` — typecheck does not regress (the 3 pre-existing errors stay 3, no new errors).
- [x] 3.4 `cd mod && dotnet build` — clean.
- [x] 3.5 `openspec validate replace-inline-hex-colors` — valid.
- [x] 3.6 `grep -rEn "#[0-9a-fA-F]{3,6}" frontend/app/components/ --include="*.vue"` (excluding JS expressions and `HslColorPicker.vue`) — only `#fff` and `#000` literals remain. **One unrelated finding logged for separate follow-up:** `CustomizePanel.vue:387` has `background: var(--bg, #1a1a1a)` (a `var()` fallback hex the audit missed); this is out of scope for this slice.
- [ ] 3.7 Manual visual smoke: launch frontend dev mode, load `battle-sampler` fixture, eyeball that HandCard, DieRow, TargetPicker, DisplayCard, and EmotionUpgradePicker render visually identically to before this change.
