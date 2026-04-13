## 1. Backend: Serialize `isSephirah` and enforce restrictions

- [x] 1.1 Add `isSephirah` boolean to librarian serialization in `GameStateSerializer.cs` (only emit when true)
- [x] 1.2 Add validation in `Server.cs` `HandleSetCustomization` to skip face/hair/color fields for patron units and dialogue fields when `battleDialogModel` is null
- [x] 1.3 Add validation in `Server.cs` `HandleRenameLibrarian` to reject rename for sephirah units
- [x] 1.4 Build validation: `cd mod && dotnet build` — 0 warnings, 0 errors

## 2. Frontend: Add restriction type and gate UI

- [x] 2.1 Add `isSephirah?: boolean` to `LibrarianEntry` in `types/game.ts`
- [x] 2.2 Add restriction computed flags in `CustomizePanel.vue` (`hasPatronHead`, `hasDialogue`, `canRename`)
- [x] 2.3 Disable Hairstyle and Face tab buttons when `hasPatronHead`; show explanatory message in tab content
- [x] 2.4 Disable Dialogue tab button when `!hasDialogue`; show explanatory message in tab content
- [x] 2.5 Disable name input in NameTitleTab when `!canRename`
- [x] 2.6 Skip inapplicable fields from the `handleComplete` payload when restrictions are active
- [x] 2.7 Build validation: `cd mod && dotnet build` (builds frontend too) — 0 warnings, 0 errors
