## Context

Both `HairstyleTab.vue` and `FaceTab.vue` use `<button>` swatch rows to pick colors stored as `[r, g, b]` byte tuples (0–255). The backend serializes these as Color32 values. The game's appearance preview (AppearancePreview.vue) already consumes these tuples directly via CSS `rgb()`.

No external libraries can be added to the mod's frontend. The CSS `hsl()` function and native `<input type="range">` provide everything needed.

## Goals / Non-Goals

**Goals:**
- Allow arbitrary hair and eye color selection via HSL sliders.
- Keep preset swatches as quick-access shortcuts below the sliders.
- Maintain the existing `[r, g, b]` byte tuple interface — callers change only by swapping `<swatch-row>` for `<HslColorPicker>`.
- Slider gradient tracks update reactively so users can see what Hue/Saturation/Lightness they are adjusting.

**Non-Goals:**
- Skin color picker (fixed game presets, not free-form).
- Hex input field or RGB numeric fields (sliders are sufficient and more touch-friendly).
- Alpha channel support.

## Decisions

### Decision: Three separate sliders (H, S, L) rather than a 2D gradient canvas

A 2D canvas picker requires canvas API and mouse/touch coordinate math. Three `<input type="range">` sliders are natively accessible, touch-friendly with no extra code, and keyboard-navigable. The gradient background of each track is enough context for a user to understand the dimension being adjusted.

### Decision: Convert at component boundary; keep HSL as internal state

`HslColorPicker` holds `localH` (0–360), `localS` (0–100), `localL` (0–100) as reactive state initialized from the incoming RGB prop. On any slider change, it converts back to RGB and emits. This avoids accumulating rounding drift on every keystroke because HSL→RGB→HSL round-trips are not perfectly lossless for all values. The parent's value is the source of truth; the internal HSL state only re-initialises when the prop value differs from what the component last emitted.

**Conversion:** standard formulas — no third-party library needed. The `utils/color.ts` module exports `rgbToHsl(r, g, b)` → `[h, s, l]` (floats: h 0–360, s/l 0–100) and `hslToRgb(h, s, l)` → `[r, g, b]` (integers 0–255, rounded).

### Decision: Preset swatches remain, placed below sliders

Users who want a canonical game color (e.g. "Black" hair) can still one-tap it. Clicking a preset sets the RGB value and the sliders snap to the corresponding HSL position. This is a strictly additive change for both tabs.

### Decision: Gradient tracks via CSS background on a wrapper div, not on the input itself

Styling `<input type="range">` track backgrounds is not cross-browser consistent (requires vendor-prefixed `::-webkit-slider-runnable-track`, `::-moz-range-track`, etc.). Instead each slider is wrapped in a `.track-wrap` div whose background is a `linear-gradient` that updates via Vue binding. The range input sits on top with `appearance: none; background: transparent` and a custom thumb. This approach is reliably cross-browser without JS measurement.

## Risks / Trade-offs

**HSL → RGB rounding** — e.g. pure black `rgb(0,0,0)` round-trips cleanly, but some mid-range HSL values lose 1–2 LSB. Impact: negligible for aesthetic color picking. Preset swatches bypass the round-trip.

**Track gradient performance** — three `linear-gradient` bindings recompute on every slider drag. Each is a simple two-stop or three-stop gradient computed inline. Vue's reactivity tracks them fine; no debounce needed.
