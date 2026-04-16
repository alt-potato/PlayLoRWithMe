## Patron Customization Restrictions

### Requirements

1. Patron (sephirah) librarians MUST NOT allow name editing — their names come from `CharactersNameXmlList` and cannot be changed in the base game.
2. Librarians with a patron head (`patronHeadId` present) MUST NOT allow face or hairstyle editing — the renderer ignores individual sprite IDs and uses a fixed `SpecialCustomizedAppearance` composite.
3. Librarians without a `dialogue` object MUST NOT allow dialogue editing — they have no `BattleDialogueModel` to write to.
4. Projection, title, and battle symbol customization MUST remain available for all librarian types (matching the base game behavior).

### Restriction Signals

| Signal | Source | Type | Gates |
|--------|--------|------|-------|
| `isSephirah` | `UnitDataModel.isSephirah` | boolean, only emitted when true | Name editing |
| `patronHeadId` | `AppearanceData.patronHeadId` | number, already serialized | Face, Hairstyle tabs |
| `dialogue` absence | `LibrarianEntry.dialogue` | null/undefined when unavailable | Dialogue tab |

### Frontend Behavior

- Disabled tabs are greyed out (`opacity: 0.4`, `pointer-events: none`, `aria-disabled="true"`), not hidden.
- Disabled tab content shows a short explanatory message (e.g. "Patron librarians have a fixed appearance").
- The name input field is disabled (not hidden) for sephirah units; title pickers remain functional.
- The save payload omits fields gated by active restrictions.

### Backend Behavior

- `HandleSetCustomization` silently skips face/hair/color/dialogue fields when the target unit is restricted.
- `HandleRenameLibrarian` returns an error when the target unit is a sephirah.
- No changes to the data model — the restrictions are enforcement-only, not structural.
