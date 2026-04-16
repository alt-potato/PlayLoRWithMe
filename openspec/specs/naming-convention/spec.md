# Spec: Naming Convention

Defines the naming convention between the C# backend, JSON API contract, and Vue/TypeScript frontend.

---

## Requirement: JSON field names use player-facing terminology
The JSON state contract emitted by `GameStateSerializer` SHALL use player-facing names
for fields that correspond to in-game UI concepts visible to players.
C# internal variable and property names are exempt — they follow engine conventions
and never appear in the JSON output.

### Scenario: Light resource fields use player-facing names
- **WHEN** the serializer emits ally unit state
- **THEN** the fields for the Light resource SHALL be named `light`, `maxLight`, and `reservedLight`
- **THEN** no field named `playPoint`, `maxPlayPoint`, or `reservedPlayPoint` SHALL appear in the output

---

## Requirement: TypeScript types mirror JSON field names exactly
Frontend TypeScript interfaces that model the JSON state contract SHALL use
the same field names as the JSON, so the player-facing convention propagates
into the frontend type system without a translation layer.

### Scenario: AllyUnit interface reflects renamed fields
- **WHEN** a developer reads the `AllyUnit` interface in `types/game.ts`
- **THEN** the Light resource fields SHALL be `light`, `maxLight`, `reservedLight`

---

## Requirement: Vue component names use player-facing terminology
Vue component file names SHALL use player-facing game terms. A component whose
behavior spans multiple player-visible concepts SHALL use the broadest accurate term.

### Scenario: Emotion level-up picker name reflects full scope
- **WHEN** a developer reads the emotion level-up overlay component
- **THEN** the component file SHALL be named `EmotionUpgradePicker.vue`
- **THEN** the component SHALL be used for both key-page selection and abnormality card selection without any naming contradiction

---

## Requirement: Frontend comments do not reference C# internal class names
TSDoc and inline comments in frontend files (`.ts`, `.vue`) SHALL NOT reference
C# class or method names from `Assembly-CSharp.dll`. Constraints or behaviors
derived from the game engine SHALL be described in plain terms.

### Scenario: game.ts comments are free of engine class names
- **WHEN** a developer reads TSDoc comments in `frontend/app/types/game.ts`
- **THEN** no comment SHALL reference `BookModel`, `DiceCardXmlInfo`, `StageController`, or any other internal C# class

### Scenario: Component comments are free of engine class names
- **WHEN** a developer reads inline comments in Vue components
- **THEN** no comment SHALL reference `BookModel`, `DiceCardXmlInfo`, or any other internal C# class
