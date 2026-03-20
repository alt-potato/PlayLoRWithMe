<!--
  DialogueTab.vue

  Dialogue sub-tab inside CustomizePanel.
  Allows setting custom text for each of the five battle dialogue types.
  Clearing the textarea restores the game's random preset dialogue.
  A collapsible preset list lets players pick from the game's official lines.
-->
<script setup lang="ts">
import type { CustomizeOptions } from "~/types/game";

/** Human-readable labels for each dialogue type key. */
const DIALOGUE_LABELS: Record<string, string> = {
  startBattle: "Combat Entrance",
  victory: "Victory Cry",
  death: "Death",
  colleagueDeath: "On ALly Death",
  killsOpponent: "On Kill",
};

const DIALOGUE_KEYS = Object.keys(DIALOGUE_LABELS) as Array<
  keyof typeof DIALOGUE_LABELS
>;

const props = defineProps<{
  startBattle: string;
  victory: string;
  death: string;
  colleagueDeath: string;
  killsOpponent: string;
  options: CustomizeOptions;
  busy: boolean;
}>();

const emit = defineEmits<{
  "update:startBattle": [value: string];
  "update:victory": [value: string];
  "update:death": [value: string];
  "update:colleagueDeath": [value: string];
  "update:killsOpponent": [value: string];
}>();

/** Tracks which dialogue type's preset list is expanded. */
const expandedPresets = ref<string | null>(null);

function togglePresets(key: string): void {
  expandedPresets.value = expandedPresets.value === key ? null : key;
}

// Writable computeds so Vue's v-model manages textarea values correctly.
const fields = {
  startBattle: computed({
    get: () => props.startBattle,
    set: (v: string) => emit("update:startBattle", v),
  }),
  victory: computed({
    get: () => props.victory,
    set: (v: string) => emit("update:victory", v),
  }),
  death: computed({
    get: () => props.death,
    set: (v: string) => emit("update:death", v),
  }),
  colleagueDeath: computed({
    get: () => props.colleagueDeath,
    set: (v: string) => emit("update:colleagueDeath", v),
  }),
  killsOpponent: computed({
    get: () => props.killsOpponent,
    set: (v: string) => emit("update:killsOpponent", v),
  }),
};

function getField(key: string) {
  return fields[key as keyof typeof fields];
}

function applyPreset(key: string, preset: string): void {
  getField(key).value = preset;
  expandedPresets.value = null;
}
</script>

<template>
  <div class="tab-inner">
    <div v-for="key in DIALOGUE_KEYS" :key="key" class="dialogue-section">
      <div class="dlg-header">
        <span class="dlg-label">{{ DIALOGUE_LABELS[key] }}</span>
        <button
          class="preset-toggle"
          :class="{ open: expandedPresets === key }"
          :disabled="busy"
          @click="togglePresets(key)"
        >
          Presets ▸
        </button>
      </div>

      <textarea
        v-model="getField(key).value"
        class="dlg-input"
        rows="2"
        placeholder="Leave empty to use random preset…"
        :disabled="busy"
      />

      <!-- Preset list (collapsible) -->
      <div v-if="expandedPresets === key" class="preset-list">
        <button
          v-for="(preset, i) in (
            options.dialoguePresets as Record<string, string[]>
          )[key] ?? []"
          :key="i"
          class="preset-item"
          :disabled="busy"
          @click="applyPreset(key, preset)"
        >
          {{ preset }}
        </button>
        <div
          v-if="
            !(
              (options.dialoguePresets as Record<string, string[]>)[key]
                ?.length ?? 0
            )
          "
          class="preset-empty"
        >
          No presets available.
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.tab-inner {
  display: flex;
  flex-direction: column;
  gap: 0.8rem;
}

.dialogue-section {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.dlg-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.dlg-label {
  font-size: 0.6rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-3);
}

.preset-toggle {
  font-size: 0.58rem;
  padding: 0.1rem 0.3rem;
  background: transparent;
  border: 1px solid var(--border);
  border-radius: 3px;
  color: var(--text-3);
  cursor: pointer;
  transition:
    color 0.1s,
    border-color 0.1s;
}

.preset-toggle:hover:not(:disabled) {
  color: var(--gold);
  border-color: var(--gold-dim);
}

.preset-toggle:disabled {
  opacity: 0.4;
  cursor: default;
}

.dlg-input {
  width: 100%;
  padding: 0.35rem 0.5rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: var(--bg);
  color: var(--text-1);
  font-size: 0.78rem;
  resize: vertical;
  box-sizing: border-box;
  font-family: var(--font-body);
  line-height: 1.4;
}

.dlg-input:disabled {
  opacity: 0.5;
}

.preset-list {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  padding: 0.3rem;
  background: var(--bg-card-2);
  border: 1px solid var(--border);
  border-radius: 4px;
  max-height: 8rem;
  overflow-y: auto;
}

.preset-item {
  padding: 0.25rem 0.4rem;
  text-align: left;
  background: transparent;
  border: none;
  border-radius: 3px;
  color: var(--text-2);
  font-size: 0.7rem;
  cursor: pointer;
  transition:
    background 0.1s,
    color 0.1s;
  line-height: 1.4;
}

.preset-item:hover:not(:disabled) {
  background: var(--bg-card);
  color: var(--text-1);
}

.preset-item:disabled {
  opacity: 0.4;
  cursor: default;
}

.preset-empty {
  font-size: 0.65rem;
  color: var(--text-3);
  font-style: italic;
  padding: 0.2rem 0.4rem;
}
</style>
