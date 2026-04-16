## 1. Color conversion utilities

- [x] 1.1 Create `frontend/app/utils/color.ts` with `rgbToHsl(r, g, b): [number, number, number]` and `hslToRgb(h, s, l): [number, number, number]` pure functions (h: 0–360, s/l: 0–100, rgb: 0–255 integers)
- [x] 1.2 Add unit tests for `rgbToHsl` and `hslToRgb` covering black, white, primary hues, and a mid-tone round-trip

## 2. HslColorPicker component

- [x] 2.1 Create `frontend/app/components/librarian/customize/HslColorPicker.vue` with `modelValue: [number, number, number]` prop and `update:modelValue` emit
- [x] 2.2 Add three `<input type="range">` sliders (H 0–360, S 0–100, L 0–100) with reactive CSS gradient tracks on wrapper divs
- [x] 2.3 Add a live preview swatch showing the current color
- [x] 2.4 Ensure internal HSL state re-syncs from prop only when the prop value differs from the last emitted value (prevents round-trip drift on re-render)

## 3. Wire into HairstyleTab and FaceTab

- [x] 3.1 Replace the hair color swatch row in `HairstyleTab.vue` with `<HslColorPicker v-model="...">` while keeping preset swatches below it
- [x] 3.2 Replace the skin color swatch row in `FaceTab.vue` with `<HslColorPicker v-model="...">` while keeping preset swatches below it
- [x] 3.3 Replace the eye color swatch row in `FaceTab.vue` with `<HslColorPicker v-model="...">` while keeping preset swatches below it

## 4. Validation

- [x] 4.1 Run `cd frontend && npm run test` — all color utility unit tests pass
- [x] 4.2 Run `cd mod && dotnet build` — expect `0 Warning(s)  0 Error(s)`
