## Why

The in-game customization UI disables most tabs for patron (sephirah) librarians because their heads use fixed `SpecialCustomizedAppearance` prefabs — setting hair/face fields has no visual effect. Dialogue is also unavailable since patron units have no `BattleDialogueModel`. The web UI currently ignores these restrictions: all tabs are fully interactive, and the backend accepts changes that the rendering engine silently discards.

Players editing hair, face, or dialogue for a patron librarian and seeing no result is confusing. The frontend should disable inapplicable options, and the backend should reject mutations that would be silently ignored.

## What Changes

- **Serialize restriction signals** in the librarian state: `isSephirah` for name gating. Face/hair gating is derived from `patronHeadId` (already serialized). Dialogue gating is derived from the absence of the `dialogue` field (already correct).
- **Frontend: disable inapplicable tabs and fields** in `CustomizePanel.vue`. Greyed-out tabs with a short explanation instead of hidden, so the UI is consistent between patron and regular librarians.
- **Backend: reject inapplicable mutations** in `HandleSetCustomization` and `HandleRenameLibrarian` when the target unit is a sephirah or has a patron head.

## Capabilities

### New Capabilities
- `patron-customization-restrictions`: Gate customization options based on unit type — patron librarians cannot edit name, face/hair, or dialogue.

## Impact

- **`mod/GameStateSerializer.cs`**: Add `isSephirah` boolean to librarian serialization.
- **`mod/Server.cs`**: Add validation in `HandleSetCustomization` to skip face/hair/dialogue fields for patron units; reject rename for sephirah units.
- **`frontend/app/types/game.ts`**: Add `isSephirah` field to `LibrarianEntry`.
- **`frontend/app/components/librarian/CustomizePanel.vue`**: Disable tabs and name field based on restriction signals; show explanatory text on disabled tabs.

## Restriction Matrix

| Tab / Field       | Disabled when              | Signal                     |
|-------------------|----------------------------|----------------------------|
| Name (rename)     | `isSephirah`               | New field                  |
| Title (prefix/sfx)| never                      | —                          |
| Hairstyle         | `patronHeadId` present     | Already serialized         |
| Face              | `patronHeadId` present     | Already serialized         |
| Projection        | never                      | —                          |
| Dialogue          | `dialogue` absent          | Already correct            |
| Battle Symbols    | never                      | —                          |
