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
    onSetGifts   – called immediately when gift equip/unequip/visibility changes
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
  SetGiftsPayload,
} from "~/types/game";

const props = defineProps<{
  lib: LibrarianEntry;
  state: GameState;
  session: SessionState | null;
  busy: boolean;
  onRename: (name: string) => Promise<ActionResult>;
  onSave: (payload: Omit<CustomizePayload, "floorIndex" | "unitIndex">) => Promise<ActionResult>;
  onSetGifts: (slots: SetGiftsPayload) => Promise<ActionResult>;
  onClose: () => void;
}>();

type SubTab = "name" | "hairstyle" | "face" | "projection" | "dialogue" | "symbols";
const activeTab = ref<SubTab>("name");

const saveBusy = ref(false);
const saveError = ref<string | null>(null);

// ── Patron/sephirah restriction flags ────────────────────────────────────
// Patron librarians use SpecialCustomizedAppearance prefabs for their
// heads — individual hair/face sprite IDs are ignored by the renderer.
const hasPatronHead = computed(() => !!props.lib.appearance?.patronHeadId);
const hasDialogue = computed(() => props.lib.dialogue != null);
const canRename = computed(() => !props.lib.isSephirah);
const disabledTabs = computed<Set<SubTab>>(() => {
  const s = new Set<SubTab>();
  if (hasPatronHead.value) { s.add("hairstyle"); s.add("face"); s.add("dialogue"); }
  if (!hasDialogue.value) s.add("dialogue");
  return s;
});

// ── Draft ─────────────────────────────────────────────────────────────────

/** Default appearance when the server hasn't yet serialized customization data. */
const DEFAULT_APPEARANCE: AppearanceData = {
  frontHairID: 0, backHairID: 0,
  eyeID: 0, browID: 0, mouthID: 0,
  height: 175,
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
  height:      props.lib.appearance?.height      ?? DEFAULT_APPEARANCE.height,
  appearanceType: props.lib.appearanceType ?? "N",

  hairColor: [...(props.lib.appearance?.hairColor ?? DEFAULT_APPEARANCE.hairColor)] as [number, number, number],
  skinColor: [...(props.lib.appearance?.skinColor ?? DEFAULT_APPEARANCE.skinColor)] as [number, number, number],
  eyeColor:  [...(props.lib.appearance?.eyeColor  ?? DEFAULT_APPEARANCE.eyeColor)]  as [number, number, number],

  dlgStartBattle:    props.lib.dialogue?.startBattle    ?? "",
  dlgVictory:        props.lib.dialogue?.victory        ?? "",
  dlgDeath:          props.lib.dialogue?.death          ?? "",
  dlgColleagueDeath: props.lib.dialogue?.colleagueDeath ?? "",
  dlgKillsOpponent:  props.lib.dialogue?.killsOpponent  ?? "",

  customBookId:        props.lib.customBookId        ?? -1,
  customBookPackageId: props.lib.customBookPackageId ?? "",
  workshopSkin:        props.lib.workshopSkin        ?? "",
});

/**
 * Snapshot of dialogue values at panel-open time. Used as the comparison base
 * when saving, so mid-edit server broadcasts don't shift what counts as
 * "unchanged" and silently swallow the user's edits.
 */
const origDialogue = {
  startBattle:    props.lib.dialogue?.startBattle    ?? null,
  victory:        props.lib.dialogue?.victory        ?? null,
  death:          props.lib.dialogue?.death          ?? null,
  colleagueDeath: props.lib.dialogue?.colleagueDeath ?? null,
  killsOpponent:  props.lib.dialogue?.killsOpponent  ?? null,
} as const;

/** Live AppearanceData fed to AppearancePreview — recomputed whenever draft changes. */
const previewAppearance = computed<AppearanceData>(() => ({
  frontHairID: draft.frontHairID,
  backHairID:  draft.backHairID,
  eyeID:       draft.eyeID,
  browID:      draft.browID,
  mouthID:     draft.mouthID,
  height:      draft.height,
  hairColor:   draft.hairColor,
  skinColor:   draft.skinColor,
  eyeColor:    draft.eyeColor,
  headID:      props.lib.appearance?.headID,
  patronHeadId: props.lib.appearance?.patronHeadId,
}));

const customizeOptions = computed(() => props.state.customizeOptions);

/** The fashion book currently selected in the draft, or null if none. */
const activeFashionBook = computed(() =>
  resolveFashionBook(draft, props.lib, customizeOptions.value),
);

/**
 * SkinGender of the active body source: from the selected fashion book if one
 * is equipped, otherwise from the librarian's own key page (via the server).
 * When a fashion book is selected in the draft, its skinGender takes precedence
 * because the toggle applies to whatever skin is currently being previewed.
 */
const activeSkinGender = computed(() =>
  activeFashionBook.value?.skinGender ?? props.lib.skinGender
);

// ── Actions ────────────────────────────────────────────────────────────────

async function handleComplete(): Promise<void> {
  saveBusy.value = true;
  saveError.value = null;

  // Rename if the name was edited. Track whether rename was sent so we can
  // surface a clear error if the subsequent customize call fails.
  let didRename = false;
  if (canRename.value && draft.name.trim() && draft.name.trim() !== props.lib.name) {
    const result = await props.onRename(draft.name.trim());
    if (!result.ok) {
      saveError.value = result.error ?? "Rename failed";
      saveBusy.value = false;
      return;
    }
    didRename = true;
  }

  // Returns the draft value only if it differs from the snapshotted original,
  // otherwise null (leave unchanged on server). Compares against the raw
  // original (which may be null for random presets) — so "user typed nothing"
  // (draft "" vs orig null) correctly skips, but "user cleared existing text"
  // (draft "" vs orig "Hello") correctly sends the empty string.
  const dlgField = (draftVal: string, orig: string | null) =>
    draftVal !== (orig ?? "") ? draftVal : null;

  // Build the customization payload, omitting fields gated by restrictions.
  // Face/hair/color are skipped for patron heads (renderer ignores them).
  // Dialogue is skipped when the unit has no BattleDialogueModel.
  const payload: Omit<CustomizePayload, "floorIndex" | "unitIndex"> = {
    // face/hair/color: only for non-patron units
    ...(!hasPatronHead.value ? {
      frontHairID: draft.frontHairID,
      backHairID:  draft.backHairID,
      eyeID:       draft.eyeID,
      browID:      draft.browID,
      mouthID:     draft.mouthID,
      hairR: draft.hairColor[0], hairG: draft.hairColor[1], hairB: draft.hairColor[2],
      skinR: draft.skinColor[0], skinG: draft.skinColor[1], skinB: draft.skinColor[2],
      eyeR:  draft.eyeColor[0],  eyeG:  draft.eyeColor[1],  eyeB:  draft.eyeColor[2],
    } : {}),
    // height and body type are under projection (always editable)
    height:      draft.height,
    appearanceType: draft.appearanceType,
    // dialogue: only when the unit has a dialogue model and isn't a patron
    ...(hasDialogue.value && !hasPatronHead.value ? {
      dlgStartBattle:    dlgField(draft.dlgStartBattle,    origDialogue.startBattle),
      dlgVictory:        dlgField(draft.dlgVictory,        origDialogue.victory),
      dlgDeath:          dlgField(draft.dlgDeath,          origDialogue.death),
      dlgColleagueDeath: dlgField(draft.dlgColleagueDeath, origDialogue.colleagueDeath),
      dlgKillsOpponent:  dlgField(draft.dlgKillsOpponent,  origDialogue.killsOpponent),
    } : {}),
    prefixID:     draft.prefixID,
    postfixID:    draft.postfixID,
    customBookId: draft.customBookId,
    ...(draft.customBookPackageId ? { customBookPackageId: draft.customBookPackageId } : {}),
    workshopSkin: draft.workshopSkin,
  };

  const result = await props.onSave(payload);
  saveBusy.value = false;
  if (!result.ok) {
    saveError.value = didRename
      ? `Appearance save failed (name was updated). ${result.error ?? ""}`
      : (result.error ?? "Save failed");
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

// Draft is initialized from props.lib when the panel opens. Server broadcasts
// during editing are intentionally ignored — the user's in-progress changes
// take priority. When the user saves (or cancels and reopens), the draft will
// be re-initialized from fresh server data.
</script>

<template>
  <Teleport to="body">
    <div class="customize-overlay" @click.self="onClose">
      <div class="customize-panel" role="dialog" aria-modal="true">
        <!-- Header -->
        <div class="panel-header">
          <span class="panel-title">Customize — {{ lib.name }}</span>
          <button class="close-btn" title="Cancel" aria-label="Close customize panel" @click="onClose">✕</button>
        </div>

        <!-- Body: preview + tab area -->
        <div class="panel-body">
          <!-- Appearance preview (left on desktop, top on mobile) -->
          <div class="preview-col">
            <LibrarianAppearancePreview
              :appearance="previewAppearance"
              :fashion-book="activeFashionBook"
              :appearance-type="draft.appearanceType"
              :gifts="lib.gifts?.equipped"
              :size="280"
              :zoom="3"
            />
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
                  ['symbols', 'Battle Symbols'],
                ] as [SubTab, string][])"
                :key="key"
                class="tab-btn"
                :class="{ active: activeTab === key, disabled: disabledTabs.has(key) }"
                :aria-disabled="disabledTabs.has(key) || undefined"
                :title="disabledTabs.has(key) ? 'Not available for patron librarians' : undefined"
                @click="!disabledTabs.has(key) && (activeTab = key)"
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
                :can-rename="canRename"
              />

              <!-- Hairstyle -->
              <div v-else-if="activeTab === 'hairstyle' && hasPatronHead" class="tab-disabled-msg">
                Patron librarians have a fixed appearance.
              </div>
              <LibrarianCustomizeHairstyleTab
                v-else-if="activeTab === 'hairstyle'"
                v-model:front-hair-i-d="draft.frontHairID"
                v-model:back-hair-i-d="draft.backHairID"
                v-model:hair-color="draft.hairColor"
                :busy="isBusy"
              />

              <!-- Face -->
              <div v-else-if="activeTab === 'face' && hasPatronHead" class="tab-disabled-msg">
                Patron librarians have a fixed appearance.
              </div>
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
                v-model:appearance-type="draft.appearanceType"
                v-model:height="draft.height"
                v-model:custom-book-id="draft.customBookId"
                v-model:custom-book-package-id="draft.customBookPackageId"
                v-model:workshop-skin="draft.workshopSkin"
                :fashion-books="customizeOptions?.fashionBooks ?? []"
                :workshop-skins="customizeOptions?.workshopSkins ?? []"
                :lib-range-type="lib.keyPage.equipRangeType ?? 'Hybrid'"
                :skin-gender="activeSkinGender"
                :busy="isBusy"
              />

              <!-- Dialogue -->
              <div v-else-if="activeTab === 'dialogue' && disabledTabs.has('dialogue')" class="tab-disabled-msg">
                Dialogue is not available for patron librarians.
              </div>
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

              <!-- Battle Symbols -->
              <LibrarianCustomizeBattleSymbolsTab
                v-else-if="activeTab === 'symbols'"
                :lib="lib"
                :busy="isBusy"
                :on-set-gifts="onSetGifts"
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
  background: var(--bg);
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

/*
 * Tab bar wraps to as many rows as needed — no horizontal scroll. Each
 * button owns its own bottom border so wrapped rows render a seamless
 * divider line even when the underline drops to the next row.
 */
.tab-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 0;
  flex-shrink: 0;
}

.tab-btn {
  padding: var(--sp-2) var(--sp-4);
  background: transparent;
  border: none;
  color: var(--text-3);
  cursor: pointer;
  font-size: var(--fs-sm);
  font-family: var(--font-display);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  border-bottom: 1px solid var(--border);
  transition: color var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out),
    background var(--duration-fast) var(--ease-out);
}

.tab-btn:hover:not(.active):not(.disabled) {
  color: var(--text-2);
  background: var(--bg-card-2);
}

.tab-btn.active {
  color: var(--gold-bright);
  border-bottom: 2px solid var(--gold-bright);
  padding-bottom: calc(var(--sp-2) - 1px);
}

.tab-btn.disabled {
  opacity: 0.4;
  pointer-events: none;
  cursor: default;
}

.tab-disabled-msg {
  color: var(--text-3);
  font-size: 0.75rem;
  font-family: var(--font-body);
  padding: 2rem 0;
  text-align: center;
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
