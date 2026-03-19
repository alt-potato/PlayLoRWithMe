<!--
  EditPanel.vue

  Full-screen (mobile) or two-panel (desktop) overlay for editing a librarian.
  Manages three tabs: Key Page, Deck, Info.

  Lock lifecycle: acquired on mount, released on unmount. If a lock is held by
  another session the panel shows a read-only locked state.

  Props:
    lib              – the librarian being edited
    state            – full game state
    session          – current session
    floorColor       – accent hex for the floor header
    editBusy         – in-flight flag (passed from parent to avoid double submits)
    onClose          – called when the user closes the panel
    onLock           – acquire the edit lock
    onUnlock         – release the edit lock
    onRename         – rename the librarian
    onEquipPage      – equip a key page
    onAddCard        – add a card to the deck
    onRemoveCard     – remove one copy from the deck
-->
<script setup lang="ts">
import type {
  LibrarianEntry,
  GameState,
  SessionState,
  AvailableKeyPage,
  AvailableCard,
  DeckCardPreview,
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
  await props.onEquipPage(kp);
}

async function onAddCard(card: AvailableCard) {
  await props.onAddCard(card);
}

async function onRemoveCard(card: DeckCardPreview) {
  await props.onRemoveCard(card);
}

// Close on Escape key
function handleKeyDown(e: KeyboardEvent) {
  if (e.key === "Escape") props.onClose();
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

          <!-- Info tab: rename + current key page summary -->
          <div v-else class="info-tab">
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

            <div class="section-label" style="margin-top: 0.75rem;">Key Page</div>
            <LibrarianKeyPageDetail :key-page="lib.keyPage" />
          </div>
        </div>
      </div>
    </div>
  </Teleport>
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
  background: var(--bg, #1a1a1a);
  border: 1px solid var(--border-mid);
  border-radius: 10px 10px 0 0;
  width: 100%;
  max-width: 900px;
  height: 92dvh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

/* On wide screens, center the panel as a modal */
@media (min-width: 900px) {
  .edit-overlay {
    align-items: center;
  }

  .edit-panel {
    height: 80dvh;
    border-radius: 10px;
  }
}

.panel-header {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.75rem 1rem;
  border-left: 4px solid var(--gold);
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

.panel-title {
  font-size: 1rem;
  font-weight: 600;
  font-family: var(--font-display);
  flex: 1;
}

.lock-badge {
  font-size: 0.65rem;
  color: var(--crimson-hi);
  background: rgba(198, 40, 40, 0.12);
  border-radius: 4px;
  padding: 0.1rem 0.4rem;
}

.lock-badge--pending {
  color: var(--text-3);
  background: transparent;
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

.tab-bar {
  display: flex;
  gap: 0;
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

.tab-btn {
  padding: 0.5rem 1rem;
  background: transparent;
  border: none;
  color: var(--text-3);
  cursor: pointer;
  font-size: 0.75rem;
  font-family: var(--font-display);
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  transition: color 0.12s, border-color 0.12s;
}

.tab-btn.active {
  color: var(--gold);
  border-bottom-color: var(--gold);
}

.locked-banner {
  padding: 0.4rem 1rem;
  font-size: 0.7rem;
  color: var(--crimson-hi);
  background: rgba(198, 40, 40, 0.08);
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

.tab-content {
  flex: 1;
  overflow: hidden;
  padding: 0.75rem 1rem;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

/* Info tab */
.info-tab {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  overflow-y: auto;
}

.section-label {
  font-size: 0.6rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-3);
}

.rename-row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}

.rename-input {
  flex: 1;
  padding: 0.35rem 0.5rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: var(--bg, #1a1a1a);
  color: var(--text-1);
  font-size: 0.8rem;
}

.rename-btn {
  padding: 0.3rem 0.8rem;
  border-radius: 4px;
  border: 1px solid var(--gold);
  background: transparent;
  color: var(--gold);
  cursor: pointer;
  font-size: 0.72rem;
  transition: background 0.1s, color 0.1s;
}

.rename-btn:not(:disabled):hover {
  background: var(--gold);
  color: #000;
}

.rename-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.rename-error {
  font-size: 0.65rem;
  color: var(--crimson-hi);
}
</style>
