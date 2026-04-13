## Why

All three color selectors in the Customize panel — hair, skin, and eye — are limited to fixed preset swatches. Users cannot pick arbitrary colors, which restricts expressiveness and makes any shade not represented by a preset completely inaccessible.

## What Changes

- A new shared `HslColorPicker.vue` component replaces the preset-only swatch rows for all three color fields: hair color (HairstyleTab), skin color (FaceTab), and eye color (FaceTab).
- The picker exposes three sliders — Hue, Saturation, Lightness — with gradient-styled tracks that update reactively to reflect the selected color.
- A live color preview swatch sits beside the sliders so the chosen color is always visible.
- The existing preset swatches are kept as quick-access shortcuts below the sliders; clicking a preset still snaps the sliders to that color.
- No external dependencies are added; the picker is a plain Vue component with CSS gradient tracks and native `<input type="range">` elements.

## Capabilities

### New Capabilities

- `hsl-color-picker`: Reusable HSL slider component that accepts and emits an RGB byte tuple `[r, g, b]` (0–255), internally converts to/from HSL for editing.

### Modified Capabilities

*(none — no existing spec requirements are changing)*

## Impact

- `frontend/app/utils/color.ts` (new) — `rgbToHsl` and `hslToRgb` pure conversion utilities.
- `frontend/app/components/librarian/customize/HslColorPicker.vue` (new) — the picker component.
- `frontend/app/components/librarian/customize/HairstyleTab.vue` — hair color swatch row replaced with `HslColorPicker`.
- `frontend/app/components/librarian/customize/FaceTab.vue` — skin color and eye color swatch rows both replaced with `HslColorPicker`.
