### Requirement: HSL color picker accepts and emits RGB byte tuple
The `HslColorPicker` component SHALL accept a `modelValue` prop of type `[number, number, number]` (RGB bytes 0–255) and emit `update:modelValue` with the same type whenever the user changes any slider or clicks a preset.

#### Scenario: Prop change updates sliders
- **WHEN** the parent updates `modelValue` to a new RGB value
- **THEN** the three sliders SHALL reflect the corresponding HSL values without the user interacting

#### Scenario: Slider change emits RGB
- **WHEN** the user moves any of the three sliders
- **THEN** the component SHALL emit `update:modelValue` with the converted RGB byte tuple

### Requirement: Slider tracks display reactive color gradients
Each slider track SHALL display a CSS gradient that reflects the current color context, so users can see what value they are adjusting.

#### Scenario: Hue track shows full spectrum
- **WHEN** the picker is rendered
- **THEN** the Hue slider track SHALL show a gradient from red through the full hue spectrum back to red

#### Scenario: Saturation track reflects current hue and lightness
- **WHEN** the hue or lightness value changes
- **THEN** the Saturation slider track gradient SHALL update to show desaturated → fully saturated at the current hue/lightness

#### Scenario: Lightness track reflects current hue and saturation
- **WHEN** the hue or saturation value changes
- **THEN** the Lightness slider track gradient SHALL update to show black → mid-color → white at the current hue/saturation

### Requirement: Preset swatches remain as quick-access shortcuts
`HairstyleTab`, and `FaceTab` SHALL retain their existing preset swatch buttons below the `HslColorPicker` for each color field. Clicking a preset SHALL set the picker to that color and snap all sliders to the corresponding HSL position.

#### Scenario: Preset click updates sliders
- **WHEN** the user clicks a preset swatch
- **THEN** the parent value SHALL update to the preset's RGB value AND the sliders SHALL reflect the preset's HSL representation

### Requirement: Live color preview swatch
The `HslColorPicker` SHALL display a swatch showing the currently selected color so users can evaluate the result without relying solely on the slider positions.

#### Scenario: Preview reflects current selection
- **WHEN** any slider value changes
- **THEN** the preview swatch color SHALL immediately update to match the new selection

### Requirement: All three color fields use HSL picker
`HairstyleTab` SHALL use `HslColorPicker` for hair color. `FaceTab` SHALL use `HslColorPicker` for both skin color and eye color.

#### Scenario: Hair color uses HSL picker
- **WHEN** the user opens the Hairstyle tab
- **THEN** the hair color section SHALL display `HslColorPicker` sliders with preset swatches below

#### Scenario: Skin color uses HSL picker
- **WHEN** the user opens the Face tab
- **THEN** the skin color section SHALL display `HslColorPicker` sliders with preset swatches below

#### Scenario: Eye color uses HSL picker
- **WHEN** the user opens the Face tab
- **THEN** the eye color section SHALL display `HslColorPicker` sliders with preset swatches below
