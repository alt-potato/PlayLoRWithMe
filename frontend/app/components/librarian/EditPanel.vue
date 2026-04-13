<!--
  EditPanel.vue

  Full-screen (mobile) or two-panel (desktop) overlay for editing a librarian.
  Manages three tabs: Key Page, Deck, Info.

  Lock lifecycle: acquired on mount, released on unmount. If a lock is held by
  another session the panel shows a read-only locked state.

  Props:
    lib                – the librarian being edited
    state              – full game state
    session            – current session
    floorColor         – accent hex for the floor header
    onClose            – called when the user closes the panel
    onLock             – acquire the edit lock
    onUnlock           – release the edit lock
    onRename           – rename the librarian
    onEquipPage        – equip a key page
    onAddCard          – add a card to the deck
    onRemoveCard       – remove one copy from the deck
    onSetCustomization – save appearance, dialogue, and title changes
-->
<script setup lang="ts">
import type {
  LibrarianEntry,
  GameState,
  SessionState,
  AvailableKeyPage,
  AvailableCard,
  DeckCardPreview,
  CustomizePayload,
  ActionResult,
  FashionBook,
} from "~/types/game";

const props = defineProps<{
  lib: LibrarianEntry;
  state: GameState;
  session: SessionState | null;
  floorColor: string;
  onClose: () => void;
  onLock: () => Promise<{ ok: boolean; error?: string }>;
  onUnlock: () => Promise<{ ok: boolean; error?: string }>;
  onRename: (name: string) => Promise<{ ok: boolean; error?: string }>;
  onEquipPage: (kp: AvailableKeyPage) => Promise<void>;
  onAddCard: (card: AvailableCard) => Promise<void>;
  onRemoveCard: (card: DeckCardPreview) => Promise<void>;
  onSetCustomization: (
    payload: Omit<CustomizePayload, "floorIndex" | "unitIndex">,
  ) => Promise<ActionResult>;
  onSetGifts: (slots: Record<string, number>) => Promise<ActionResult>;
}>();

type Tab = "keypage" | "deck" | "info";
const activeTab = ref<Tab>("keypage");

const lockBusy = ref(false);
/** True if this session successfully holds the lock for this librarian. */
const hasLock = ref(false);

/** True when someone else holds the lock (reading from server state). */
const lockedByOther = computed(() => !!props.lib.lockedBy && !hasLock.value);

onMounted(async () => {
  lockBusy.value = true;
  const result = await props.onLock();
  lockBusy.value = false;
  if (result.ok) hasLock.value = true;
});

onBeforeUnmount(async () => {
  if (hasLock.value) {
    await props.onUnlock();
    hasLock.value = false;
  }
});

// Rename state (Info tab)
const editName = ref(props.lib.name);
const renameBusy = ref(false);
const renameError = ref<string | null>(null);

async function commitRename() {
  const trimmed = editName.value.trim();
  if (!trimmed || trimmed === props.lib.name) return;
  renameBusy.value = true;
  renameError.value = null;
  const result = await props.onRename(trimmed);
  renameBusy.value = false;
  if (!result.ok) renameError.value = result.error ?? "Rename failed";
}

// Shared busy flag that merges all async ops for child tabs.
const editBusy = computed(() => lockBusy.value || renameBusy.value);

async function onEquipPage(kp: AvailableKeyPage) {
  try { await props.onEquipPage(kp); }
  catch { /* network errors handled by useWebSocket reconnect */ }
}

async function onAddCard(card: AvailableCard) {
  try { await props.onAddCard(card); }
  catch { /* network errors handled by useWebSocket reconnect */ }
}

async function onRemoveCard(card: DeckCardPreview) {
  try { await props.onRemoveCard(card); }
  catch { /* network errors handled by useWebSocket reconnect */ }
}

/**
 * Active fashion book for the appearance preview.  Custom book projection takes priority;
 * when none is active, falls back to the key page's own body composite so the librarian's
 * default in-game appearance is reflected without needing to open the customize panel.
 */
const activeFashionBook = computed<FashionBook | null>(() => {
  // Workshop skin (cloth overlay) takes priority when active.
  const wsId = props.lib.workshopSkin;
  if (wsId) {
    const skin = (props.state.customizeOptions?.workshopSkins ?? []).find(
      (s) => s.contentFolderIdx === wsId
    );
    if (skin) {
      return {
        id: 0,
        fileStem: `ws_${skin.contentFolderIdx}`,
        name: skin.name,
        rangeType: "Hybrid",
        replacesHead: skin.replacesHead ?? false,
        hasFrontLayer: skin.hasFrontLayer,
        headTiltDeg: skin.headTiltDeg,
        pivotFracX: skin.pivotFracX,
        pivotFracY: skin.pivotFracY,
      };
    }
  }

  const projId = props.lib.customBookId;
  if (projId != null && projId >= 0) {
    const projPkg = props.lib.customBookPackageId ?? "";
    const found = (props.state.customizeOptions?.fashionBooks ?? []).find(
      (fb) => fb.id === projId && (fb.packageId ?? "") === projPkg
    );
    if (found) return found;
  }
  // Fall back to the key page's own body composite.
  const bookId = props.lib.keyPage.bookId;
  if (bookId == null) return null;
  return {
    id: bookId,
    packageId: props.lib.keyPage.bookPackageId,
    name: props.lib.keyPage.name,
    rangeType: props.lib.keyPage.equipRangeType ?? "",
    replacesHead: props.lib.keyPageReplacesHead ?? false,
    hasFrontLayer: props.lib.keyPageHasFrontLayer,
    headTiltDeg: props.lib.keyPageHeadTiltDeg,
    pivotFracX: props.lib.keyPagePivotFracX,
    pivotFracY: props.lib.keyPagePivotFracY,
    hidesBackHair: props.lib.keyPageHidesBackHair,
    skinGender: props.lib.keyPageSkinGender,
  };
});

// Customize panel state
const showCustomize = ref(false);

// Close on Escape key (but not if CustomizePanel is open — it handles Escape itself)
function handleKeyDown(e: KeyboardEvent) {
  if (e.key === "Escape" && !showCustomize.value) props.onClose();
}

onMounted(() => window.addEventListener("keydown", handleKeyDown));
onBeforeUnmount(() => window.removeEventListener("keydown", handleKeyDown));
</script>

<template>
  <Teleport to="body">
    <div class="edit-overlay" @click.self="onClose">
      <div class="edit-panel" role="dialog" aria-modal="true">
        <!-- Header -->
        <div class="panel-header" :style="{ borderLeftColor: floorColor }">
          <span class="panel-title" :style="{ color: floorColor }">{{ lib.name }}</span>
          <span v-if="lockedByOther" class="lock-badge">
            Locked by {{ lib.lockedBy }}
          </span>
          <span v-else-if="!hasLock && lockBusy" class="lock-badge lock-badge--pending">
            Acquiring lock…
          </span>
          <button class="close-btn" title="Close" @click="onClose">✕</button>
        </div>

        <!-- Tab bar -->
        <div class="tab-bar">
          <button
            v-for="tab in (['keypage', 'deck', 'info'] as Tab[])"
            :key="tab"
            class="tab-btn"
            :class="{ active: activeTab === tab }"
            @click="activeTab = tab"
          >
            {{ tab === 'keypage' ? 'Key Page' : tab === 'deck' ? 'Deck' : 'Info' }}
          </button>
        </div>

        <!-- Locked banner -->
        <div v-if="lockedByOther" class="locked-banner">
          This librarian is being edited by <strong>{{ lib.lockedBy }}</strong>.
          You can view but not edit.
        </div>

        <!-- Panel body: sidebar (desktop) + tab content -->
        <div class="panel-body">
          <!--
            Desktop sidebar: appearance preview pinned to the left on the Info tab only.
            The Key Page tab renders the preview inline in its detail column, and the
            Deck tab shows no preview.  Hidden on mobile via CSS — the Info tab inset
            handles the narrow-viewport case.
          -->
          <div v-if="lib.appearance && activeTab === 'info'" class="preview-sidebar">
            <LibrarianAppearancePreview
              :appearance="lib.appearance"
              :fashion-book="activeFashionBook"
              :appearance-type="lib.appearanceType"
              :gifts="lib.gifts?.equipped"
              :size="260"
            />
          </div>

          <!-- Tab content -->
          <div class="tab-content">
            <LibrarianKeyPageTab
              v-if="activeTab === 'keypage'"
              :lib="lib"
              :state="state"
              :edit-busy="editBusy || lockedByOther"
              :on-equip-page="onEquipPage"
            />

            <LibrarianDeckTab
              v-else-if="activeTab === 'deck'"
              :lib="lib"
              :state="state"
              :edit-busy="editBusy || lockedByOther"
              :on-add-card="onAddCard"
              :on-remove-card="onRemoveCard"
            />

            <!-- Info tab: rename + customize button + current key page summary -->
            <div v-else class="info-tab">
              <!--
                Mobile preview inset: shown at the top of the Info tab on narrow
                viewports. Hidden above 700 px where the sidebar takes over.
              -->
              <div v-if="lib.appearance" class="preview-mobile">
                <LibrarianAppearancePreview
                  :appearance="lib.appearance"
                  :fashion-book="activeFashionBook"
                  :appearance-type="lib.appearanceType"
                  :gifts="lib.gifts?.equipped"
                  :size="220"
                />
              </div>

              <div class="section-label">Name</div>
              <div class="rename-row">
                <input
                  v-model="editName"
                  class="rename-input"
                  maxlength="40"
                  :disabled="lockedByOther || !hasLock"
                  @keydown.enter.prevent="commitRename"
                />
                <button
                  class="rename-btn"
                  :disabled="lockedByOther || !hasLock || renameBusy"
                  @click="commitRename"
                >
                  Save
                </button>
              </div>
              <div v-if="renameError" class="rename-error">{{ renameError }}</div>

              <button
                class="customize-btn"
                :disabled="lockedByOther || !hasLock"
                @click="showCustomize = true"
              >
                Customize Appearance &amp; Dialogue…
              </button>

              <div class="section-label" style="margin-top: 0.75rem;">Key Page</div>
              <LibrarianKeyPageDetail :key-page="lib.keyPage" />
            </div>
          </div>
        </div>
      </div>
    </div>
  </Teleport>

  <!-- Customize overlay — stacks above EditPanel (z-index: 300) -->
  <LibrarianCustomizePanel
    v-if="showCustomize"
    :lib="lib"
    :state="state"
    :session="session"
    :busy="editBusy"
    :on-rename="props.onRename"
    :on-save="props.onSetCustomization"
    :on-set-gifts="props.onSetGifts"
    :on-close="() => (showCustomize = false)"
  />
</template>

<style scoped>
.edit-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  z-index: 200;
  display: flex;
  align-items: flex-end;
  justify-content: center;
}

.edit-panel {
  background: var(--bg-surface);
  border: 1px solid var(--border-mid);
  border-top: 2px solid var(--gold-dim);
  border-radius: var(--radius-lg) var(--radius-lg) 0 0;
  box-shadow: var(--shadow-lg);
  width: 100%;
  max-width: 900px;
  height: 92dvh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/*
 * Desktop modal: at >=900px the panel becomes a centered dialog with a
 * sticky left rail (1/3) for tab nav and the tab content (2/3) on the right.
 */
@media (min-width: 900px) {
  .edit-overlay {
    align-items: center;
  }

  .edit-panel {
    height: 90dvh;
    max-height: 90dvh;
    max-width: 1400px;
    border-radius: var(--radius-lg);
  }
}

.panel-header {
  display: flex;
  align-items: center;
  gap: var(--sp-2);
  padding: var(--sp-3) var(--sp-4);
  border-left: 4px solid var(--gold);
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

.panel-title {
  font-size: var(--fs-lg);
  font-weight: 600;
  font-family: var(--font-display);
  flex: 1;
}

.lock-badge {
  font-size: var(--fs-3xs);
  color: var(--crimson-hi);
  background: rgba(198, 40, 40, 0.12);
  border-radius: var(--radius-md);
  padding: var(--sp-1) var(--sp-2);
}

.lock-badge--pending {
  color: var(--text-3);
  background: transparent;
}

.close-btn {
  background: transparent;
  border: none;
  color: var(--text-3);
  font-size: var(--fs-md);
  cursor: pointer;
  padding: var(--sp-1) var(--sp-2);
  border-radius: var(--radius-md);
  transition: color var(--duration-fast) var(--ease-out);
}

.close-btn:hover {
  color: var(--text-1);
}

/*
 * Tab bar — horizontal on mobile (default). At desktop sizes the rail
 * inside .panel-body switches it to a vertical settings-style nav.
 */
.tab-bar {
  display: flex;
  gap: 0;
  border-bottom: 1px solid var(--border);
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
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  transition: color var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out);
}

.tab-btn.active {
  color: var(--gold-bright);
  border-bottom-color: var(--gold-bright);
}

.locked-banner {
  padding: var(--sp-2) var(--sp-4);
  font-size: var(--fs-xs);
  color: var(--crimson-hi);
  background: rgba(198, 40, 40, 0.08);
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

/* Flex row containing the optional sidebar and the tab content area. */
.panel-body {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

/*
 * Desktop sidebar: holds the AppearancePreview on Info tab.
 * Hidden on mobile so the Info-tab inset handles preview instead.
 */
.preview-sidebar {
  display: none;
  flex-shrink: 0;
  align-items: flex-start;
  padding: var(--sp-3) var(--sp-2) var(--sp-3) var(--sp-4);
}

@media (min-width: 700px) {
  .preview-sidebar {
    display: flex;
  }
}

.tab-content {
  flex: 1;
  overflow: hidden;
  padding: var(--sp-3) var(--sp-4);
  min-height: 0;
  display: flex;
  flex-direction: column;
}

/*
 * Desktop layout: at >=900px the tab bar moves into a compact left rail
 * and the tab content takes the remaining width. The rail is sized to
 * fit the tab labels only — a full 1/3 column wastes horizontal space
 * that the content panels (deck editor, key-page grid) desperately need.
 */
@media (min-width: 900px) {
  .edit-panel {
    display: grid;
    grid-template-rows: auto auto 1fr;
    grid-template-columns: 170px 1fr;
  }

  .panel-header {
    grid-column: 1 / -1;
    grid-row: 1;
  }

  .locked-banner {
    grid-column: 1 / -1;
    grid-row: 2;
  }

  .tab-bar {
    grid-column: 1;
    grid-row: 3;
    flex-direction: column;
    gap: var(--sp-1);
    border-bottom: none;
    border-right: 1px solid var(--border);
    padding: var(--sp-3) var(--sp-2);
    align-items: stretch;
    overflow-y: auto;
  }

  .tab-btn {
    border-bottom: none;
    border-left: 3px solid transparent;
    margin-bottom: 0;
    text-align: left;
    padding: var(--sp-2) var(--sp-3);
    border-radius: 0 var(--radius-md) var(--radius-md) 0;
    font-size: var(--fs-sm);
  }

  .tab-btn.active {
    border-left-color: var(--gold-bright);
    background: var(--gold-glow);
    box-shadow: var(--shadow-gold);
  }

  .panel-body {
    grid-column: 2;
    grid-row: 3;
    border-left: none;
    min-width: 0;
  }

  .preview-sidebar {
    /* On the desktop grid the sidebar is no longer needed inside the body. */
    display: none;
  }

  .tab-content {
    padding: var(--sp-4) var(--sp-5);
  }
}

/*
 * Mobile Info-tab preview: centered above the name/customize controls.
 * Hidden on desktop where the sidebar takes over.
 */
.preview-mobile {
  display: flex;
  justify-content: center;
  padding-bottom: var(--sp-3);
}

@media (min-width: 700px) {
  .preview-mobile {
    display: none;
  }
}

/* The sidebar version of the preview is desktop-only and also unused
   when the panel becomes a grid; keep the inline mobile preview visible. */
@media (min-width: 900px) {
  .preview-mobile {
    display: flex;
  }
}

/* Info tab */
.info-tab {
  display: flex;
  flex-direction: column;
  gap: var(--sp-2);
  overflow-y: auto;
}

.section-label {
  font-size: var(--fs-xs);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-2);
  font-family: var(--font-display);
}

.rename-row {
  display: flex;
  gap: var(--sp-2);
  align-items: center;
}

.rename-input {
  flex: 1;
  padding: var(--sp-2) var(--sp-3);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-mid);
  background: var(--bg-card-2);
  color: var(--text-1);
  font-size: var(--fs-sm);
  transition: border-color var(--duration-fast) var(--ease-out),
    box-shadow var(--duration-fast) var(--ease-out);
}

.rename-input:focus {
  outline: none;
  border-color: var(--gold-dim);
  box-shadow: var(--shadow-gold);
}

.rename-btn {
  padding: var(--sp-2) var(--sp-3);
  border-radius: var(--radius-md);
  border: 1px solid var(--gold);
  background: transparent;
  color: var(--gold);
  cursor: pointer;
  font-size: var(--fs-xs);
  font-family: var(--font-display);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  transition: background var(--duration-fast) var(--ease-out),
    color var(--duration-fast) var(--ease-out);
}

.rename-btn:not(:disabled):hover {
  background: var(--gold);
  color: var(--gold-ink);
}

.rename-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.rename-error {
  font-size: var(--fs-xs);
  color: var(--crimson-hi);
}

.customize-btn {
  margin-top: var(--sp-2);
  padding: var(--sp-2) var(--sp-3);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-mid);
  background: transparent;
  color: var(--text-2);
  cursor: pointer;
  font-size: var(--fs-sm);
  font-family: var(--font-display);
  text-align: left;
  transition: color var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out);
}

.customize-btn:hover:not(:disabled) {
  color: var(--gold-bright);
  border-color: var(--gold-dim);
}

.customize-btn:disabled {
  opacity: 0.4;
  cursor: default;
}
</style>
