<!--
  CustomizePanel.vue

  Full-screen overlay for customizing a librarian's appearance, dialogue, and titles.
  Opened from EditPanel while it still holds the edit lock; this panel acquires no
  additional lock.

  Changes are staged locally in a reactive draft object. "Complete" submits a single
  setCustomization action (and a rename if the name changed), then closes.
  "Cancel" discards all draft changes without sending anything.

  Props:
    lib          – the librarian being customized (source of truth for initial values)
    state        – full game state (for customizeOptions)
    session      – current session
    busy         – whether parent has an in-flight operation
    onRename     – called if the name was changed
    onSave       – called with the flat setCustomization payload
    onClose      – called on Complete or Cancel
-->
<script setup lang="ts">
import type {
  LibrarianEntry,
  GameState,
  SessionState,
  AppearanceData,
  CustomizePayload,
  ActionResult,
} from "~/types/game";

const props = defineProps<{
  lib: LibrarianEntry;
  state: GameState;
  session: SessionState | null;
  busy: boolean;
  onRename: (name: string) => Promise<ActionResult>;
  onSave: (payload: Omit<CustomizePayload, "floorIndex" | "unitIndex">) => Promise<ActionResult>;
  onClose: () => void;
}>();

type SubTab = "name" | "hairstyle" | "face" | "projection" | "dialogue";
const activeTab = ref<SubTab>("name");

const saveBusy = ref(false);
const saveError = ref<string | null>(null);

// ── Draft ─────────────────────────────────────────────────────────────────

/** Default appearance when the server hasn't yet serialized customization data. */
const DEFAULT_APPEARANCE: AppearanceData = {
  frontHairID: 0, backHairID: 0,
  eyeID: 0, browID: 0, mouthID: 0,
  headID: 0, height: 175,
  hairColor: [13, 13, 13],
  skinColor: [224, 188, 157],
  eyeColor: [13, 13, 13],
};

const draft = reactive({
  name:      props.lib.name,
  prefixID:  props.lib.titles?.prefixID  ?? 0,
  postfixID: props.lib.titles?.postfixID ?? 0,

  frontHairID: props.lib.appearance?.frontHairID ?? DEFAULT_APPEARANCE.frontHairID,
  backHairID:  props.lib.appearance?.backHairID  ?? DEFAULT_APPEARANCE.backHairID,
  eyeID:       props.lib.appearance?.eyeID       ?? DEFAULT_APPEARANCE.eyeID,
  browID:      props.lib.appearance?.browID      ?? DEFAULT_APPEARANCE.browID,
  mouthID:     props.lib.appearance?.mouthID     ?? DEFAULT_APPEARANCE.mouthID,
  headID:      props.lib.appearance?.headID      ?? DEFAULT_APPEARANCE.headID,
  height:      props.lib.appearance?.height      ?? DEFAULT_APPEARANCE.height,

  hairColor: [...(props.lib.appearance?.hairColor ?? DEFAULT_APPEARANCE.hairColor)] as [number, number, number],
  skinColor: [...(props.lib.appearance?.skinColor ?? DEFAULT_APPEARANCE.skinColor)] as [number, number, number],
  eyeColor:  [...(props.lib.appearance?.eyeColor  ?? DEFAULT_APPEARANCE.eyeColor)]  as [number, number, number],

  dlgStartBattle:    props.lib.dialogue?.startBattle    ?? "",
  dlgVictory:        props.lib.dialogue?.victory        ?? "",
  dlgDeath:          props.lib.dialogue?.death          ?? "",
  dlgColleagueDeath: props.lib.dialogue?.colleagueDeath ?? "",
  dlgKillsOpponent:  props.lib.dialogue?.killsOpponent  ?? "",
});

/** Live AppearanceData fed to AppearancePreview — recomputed whenever draft changes. */
const previewAppearance = computed<AppearanceData>(() => ({
  frontHairID: draft.frontHairID,
  backHairID:  draft.backHairID,
  eyeID:       draft.eyeID,
  browID:      draft.browID,
  mouthID:     draft.mouthID,
  headID:      draft.headID,
  height:      draft.height,
  hairColor:   draft.hairColor,
  skinColor:   draft.skinColor,
  eyeColor:    draft.eyeColor,
}));

const customizeOptions = computed(() => props.state.customizeOptions);

// ── Actions ────────────────────────────────────────────────────────────────

async function handleComplete(): Promise<void> {
  saveBusy.value = true;
  saveError.value = null;

  // Rename if the name was edited.
  if (draft.name.trim() && draft.name.trim() !== props.lib.name) {
    const result = await props.onRename(draft.name.trim());
    if (!result.ok) {
      saveError.value = result.error ?? "Rename failed";
      saveBusy.value = false;
      return;
    }
  }

  const origDlg = props.lib.dialogue;
  // Returns the draft value only if it differs from the server's effective
  // value, otherwise null (leave unchanged on server).
  const dlgField = (draft: string, orig: string | null | undefined) =>
    draft !== (orig ?? "") ? draft : null;

  // Send the full customization payload.
  const payload: Omit<CustomizePayload, "floorIndex" | "unitIndex"> = {
    frontHairID: draft.frontHairID,
    backHairID:  draft.backHairID,
    eyeID:       draft.eyeID,
    browID:      draft.browID,
    mouthID:     draft.mouthID,
    headID:      draft.headID,
    height:      draft.height,
    hairR: draft.hairColor[0], hairG: draft.hairColor[1], hairB: draft.hairColor[2],
    skinR: draft.skinColor[0], skinG: draft.skinColor[1], skinB: draft.skinColor[2],
    eyeR:  draft.eyeColor[0],  eyeG:  draft.eyeColor[1],  eyeB:  draft.eyeColor[2],
    // Only send dialogue fields that were actually changed from the server's
    // effective value. Null tells the server to leave the field unchanged,
    // which preserves random presets rather than locking them in as custom
    // text just because the panel was opened and closed without editing.
    dlgStartBattle:    dlgField(draft.dlgStartBattle,    origDlg?.startBattle),
    dlgVictory:        dlgField(draft.dlgVictory,        origDlg?.victory),
    dlgDeath:          dlgField(draft.dlgDeath,          origDlg?.death),
    dlgColleagueDeath: dlgField(draft.dlgColleagueDeath, origDlg?.colleagueDeath),
    dlgKillsOpponent:  dlgField(draft.dlgKillsOpponent,  origDlg?.killsOpponent),
    prefixID:  draft.prefixID,
    postfixID: draft.postfixID,
  };

  const result = await props.onSave(payload);
  saveBusy.value = false;
  if (!result.ok) {
    saveError.value = result.error ?? "Save failed";
    return;
  }

  props.onClose();
}

// Close on Escape key.
function handleKeyDown(e: KeyboardEvent): void {
  if (e.key === "Escape") props.onClose();
}

onMounted(() => window.addEventListener("keydown", handleKeyDown));
onBeforeUnmount(() => window.removeEventListener("keydown", handleKeyDown));

const isBusy = computed(() => props.busy || saveBusy.value);

// Re-sync dialogue and title fields from the server state whenever props.lib
// changes (e.g. after a setCustomization broadcast updates the parent state).
// immediate: true ensures pre-fill is correct even on first mount.
watch(
  () => props.lib,
  (lib) => {
    draft.prefixID  = lib.titles?.prefixID  ?? 0;
    draft.postfixID = lib.titles?.postfixID ?? 0;
    draft.dlgStartBattle    = lib.dialogue?.startBattle    ?? "";
    draft.dlgVictory        = lib.dialogue?.victory        ?? "";
    draft.dlgDeath          = lib.dialogue?.death          ?? "";
    draft.dlgColleagueDeath = lib.dialogue?.colleagueDeath ?? "";
    draft.dlgKillsOpponent  = lib.dialogue?.killsOpponent  ?? "";
  },
  { immediate: true },
);
</script>

<template>
  <Teleport to="body">
    <div class="customize-overlay" @click.self="onClose">
      <div class="customize-panel" role="dialog" aria-modal="true">
        <!-- Header -->
        <div class="panel-header">
          <span class="panel-title">Customize — {{ lib.name }}</span>
          <button class="close-btn" title="Cancel" @click="onClose">✕</button>
        </div>

        <!-- Body: preview + tab area -->
        <div class="panel-body">
          <!-- Appearance preview (left on desktop, top on mobile) -->
          <div class="preview-col">
            <LibrarianAppearancePreview :appearance="previewAppearance" />
          </div>

          <!-- Sub-tab area -->
          <div class="tab-area">
            <div class="tab-bar">
              <button
                v-for="[key, label] in ([
                  ['name', 'Name & Title'],
                  ['hairstyle', 'Hairstyle'],
                  ['face', 'Face'],
                  ['projection', 'Projection'],
                  ['dialogue', 'Dialogue'],
                ] as [SubTab, string][])"
                :key="key"
                class="tab-btn"
                :class="{ active: activeTab === key }"
                @click="activeTab = key"
              >
                {{ label }}
              </button>
            </div>

            <div class="tab-content">
              <!-- Name & Title -->
              <LibrarianCustomizeNameTitleTab
                v-if="activeTab === 'name'"
                v-model:name="draft.name"
                v-model:prefix-i-d="draft.prefixID"
                v-model:postfix-i-d="draft.postfixID"
                :options="customizeOptions ?? { prefixTitles: [], suffixTitles: [], suggestedNames: [], dialoguePresets: { startBattle: [], victory: [], death: [], colleagueDeath: [], killsOpponent: [] } }"
                :busy="isBusy"
              />

              <!-- Hairstyle -->
              <LibrarianCustomizeHairstyleTab
                v-else-if="activeTab === 'hairstyle'"
                v-model:front-hair-i-d="draft.frontHairID"
                v-model:back-hair-i-d="draft.backHairID"
                v-model:hair-color="draft.hairColor"
                :busy="isBusy"
              />

              <!-- Face -->
              <LibrarianCustomizeFaceTab
                v-else-if="activeTab === 'face'"
                v-model:eye-i-d="draft.eyeID"
                v-model:brow-i-d="draft.browID"
                v-model:mouth-i-d="draft.mouthID"
                v-model:skin-color="draft.skinColor"
                v-model:eye-color="draft.eyeColor"
                :busy="isBusy"
              />

              <!-- Projection -->
              <LibrarianCustomizeProjectionTab
                v-else-if="activeTab === 'projection'"
                v-model:head-i-d="draft.headID"
                v-model:height="draft.height"
                :busy="isBusy"
              />

              <!-- Dialogue -->
              <LibrarianCustomizeDialogueTab
                v-else-if="activeTab === 'dialogue'"
                v-model:start-battle="draft.dlgStartBattle"
                v-model:victory="draft.dlgVictory"
                v-model:death="draft.dlgDeath"
                v-model:colleague-death="draft.dlgColleagueDeath"
                v-model:kills-opponent="draft.dlgKillsOpponent"
                :options="customizeOptions ?? { prefixTitles: [], suffixTitles: [], suggestedNames: [], dialoguePresets: { startBattle: [], victory: [], death: [], colleagueDeath: [], killsOpponent: [] } }"
                :busy="isBusy"
              />
            </div>
          </div>
        </div>

        <!-- Footer -->
        <div class="panel-footer">
          <div v-if="saveError" class="save-error">{{ saveError }}</div>
          <button class="cancel-btn" :disabled="isBusy" @click="onClose">Cancel</button>
          <button class="complete-btn" :disabled="isBusy" @click="handleComplete">
            {{ saveBusy ? "Saving…" : "Complete" }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.customize-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.7);
  z-index: 300;
  display: flex;
  align-items: flex-end;
  justify-content: center;
}

.customize-panel {
  background: var(--bg, #1a1a1a);
  border: 1px solid var(--border-mid);
  border-radius: 10px 10px 0 0;
  width: 100%;
  max-width: 940px;
  height: 94dvh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

@media (min-width: 700px) {
  .customize-overlay {
    align-items: center;
  }

  .customize-panel {
    height: 82dvh;
    border-radius: 10px;
  }
}

.panel-header {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

.panel-title {
  flex: 1;
  font-size: 0.9rem;
  font-weight: 600;
  font-family: var(--font-display);
  color: var(--gold);
}

.close-btn {
  background: transparent;
  border: none;
  color: var(--text-3);
  font-size: 0.9rem;
  cursor: pointer;
  padding: 0.1rem 0.3rem;
  border-radius: 4px;
  transition: color 0.1s;
}

.close-btn:hover {
  color: var(--text-1);
}

/* ── Body layout ───────────────────────────────────────────────────────────── */

.panel-body {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* On desktop, preview appears on the left; tabs fill the right. */
@media (min-width: 700px) {
  .panel-body {
    flex-direction: row;
  }
}

.preview-col {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0.75rem;
  flex-shrink: 0;
  border-bottom: 1px solid var(--border);
}

@media (min-width: 700px) {
  .preview-col {
    border-bottom: none;
    border-right: 1px solid var(--border);
    padding: 1rem;
    align-items: flex-start;
    padding-top: 1.25rem;
  }
}

.tab-area {
  flex: 1;
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* ── Sub-tab bar ───────────────────────────────────────────────────────────── */

.tab-bar {
  display: flex;
  gap: 0;
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
  overflow-x: auto;
}

.tab-btn {
  padding: 0.45rem 0.75rem;
  background: transparent;
  border: none;
  color: var(--text-3);
  cursor: pointer;
  font-size: 0.7rem;
  font-family: var(--font-display);
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  white-space: nowrap;
  transition: color 0.12s, border-color 0.12s;
}

.tab-btn.active {
  color: var(--gold);
  border-bottom-color: var(--gold);
}

.tab-content {
  flex: 1;
  overflow-y: auto;
  padding: 0.75rem 1rem;
  min-height: 0;
}

/* ── Footer ────────────────────────────────────────────────────────────────── */

.panel-footer {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.6rem 1rem;
  border-top: 1px solid var(--border);
  flex-shrink: 0;
}

.save-error {
  flex: 1;
  font-size: 0.65rem;
  color: var(--crimson-hi);
}

.cancel-btn,
.complete-btn {
  padding: 0.35rem 0.9rem;
  border-radius: 4px;
  font-size: 0.75rem;
  cursor: pointer;
  transition: background 0.1s, color 0.1s, border-color 0.1s;
}

.cancel-btn {
  background: transparent;
  border: 1px solid var(--border-mid);
  color: var(--text-2);
  margin-left: auto;
}

.cancel-btn:hover:not(:disabled) {
  border-color: var(--text-2);
  color: var(--text-1);
}

.complete-btn {
  background: transparent;
  border: 1px solid var(--gold);
  color: var(--gold);
}

.complete-btn:hover:not(:disabled) {
  background: var(--gold);
  color: #000;
}

.cancel-btn:disabled,
.complete-btn:disabled {
  opacity: 0.4;
  cursor: default;
}
</style>
