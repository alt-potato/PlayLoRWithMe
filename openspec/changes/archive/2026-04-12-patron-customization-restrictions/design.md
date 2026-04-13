## Restriction Signals

Three independent signals gate different customization features:

```
┌──────────────────────────────────────────────────────────────┐
│                    Restriction Signals                        │
├────────────────────┬─────────────────────────────────────────┤
│ isSephirah         │ New. Gates name editing.                │
│                    │ Sephirah names come from                │
│                    │ CharactersNameXmlList, not user input.   │
├────────────────────┼─────────────────────────────────────────┤
│ patronHeadId       │ Already serialized. When present, the   │
│                    │ unit uses SpecialCustomizedAppearance    │
│                    │ — individual face/hair sprite IDs are    │
│                    │ ignored by the renderer. Gates hair and  │
│                    │ face tabs.                               │
├────────────────────┼─────────────────────────────────────────┤
│ dialogue (absent)  │ Already correct. battleDialogModel is   │
│                    │ null for sephirah units — serializer     │
│                    │ skips the dialogue object. Gates         │
│                    │ dialogue tab.                            │
└────────────────────┴─────────────────────────────────────────┘
```

### Why not just `isSephirah` for everything?

`patronHeadId` is more semantically precise for face/hair gating. It describes the actual rendering constraint ("head is a fixed composite") rather than the unit category. If a modded key page ever introduces patron-style composite heads for non-sephirah units, `patronHeadId` would still be correct.

## Backend Changes

### GameStateSerializer.cs — librarian serialization

Add `isSephirah` boolean after the existing `appearanceType` field. Only emit when true to keep the JSON lean.

### Server.cs — HandleSetCustomization

Skip face/hair/color fields when `customizeData.UseCustomData` is false or when `patronHeadId` would be set (i.e. the unit's specialCustomID maps to a known patron ID). Skip dialogue fields when `battleDialogModel` is null. These are no-ops in the engine but rejecting them keeps the contract honest.

### Server.cs — HandleRenameLibrarian

Reject rename with an error when `unit.isSephirah` is true.

## Frontend Changes

### types/game.ts

Add `isSephirah?: boolean` to `LibrarianEntry`.

### CustomizePanel.vue

Derive three computed restriction flags:

```typescript
const hasPatronHead = computed(() => !!props.lib.appearance?.patronHeadId);
const hasDialogue = computed(() => props.lib.dialogue != null);
const canRename = computed(() => !props.lib.isSephirah);
```

Tab-level behavior:
- **Name & Title**: Name input disabled when `!canRename`. Title pickers always enabled.
- **Hairstyle**: Tab button greyed out with `title="Patron librarians have a fixed appearance"`. Tab content replaced with a short message.
- **Face**: Same as Hairstyle.
- **Dialogue**: Tab button greyed out with `title="Not available for patron librarians"`. Tab content replaced with a short message.
- **Projection**: Always enabled.
- **Battle Symbols**: Always enabled.

Greyed-out tabs use `pointer-events: none; opacity: 0.4` and `aria-disabled="true"`. If a disabled tab is somehow the active tab (e.g. from a stale `activeTab` ref), the content area shows the explanatory message.

### handleComplete payload

Skip face/hair/color/dialogue fields from the payload when the corresponding restriction is active. This prevents sending no-op data even though the backend would now reject it.

## UX Decision

Disabled tabs are visually present but greyed out rather than hidden. This keeps the tab bar layout consistent between patron and regular librarians so users understand what exists but isn't available, rather than wondering where tabs went.
