<!--
  NameTitleTab.vue

  Name & Title sub-tab inside CustomizePanel.
  Allows renaming the librarian with name suggestions, and selecting a
  prefix and suffix title from the game's gift title lists.
-->
<script setup lang="ts">
import type { CustomizeOptions } from "~/types/game";

const props = defineProps<{
  name: string;
  prefixID: number;
  postfixID: number;
  options: CustomizeOptions;
  busy: boolean;
}>();

const emit = defineEmits<{
  "update:name": [value: string];
  "update:prefixID": [value: number];
  "update:postfixID": [value: number];
}>();

/** Randomly selected subset of name suggestions shown as chips. */
const visibleSuggestions = ref<string[]>([]);

function shuffleSuggestions(): void {
  const pool = [...props.options.suggestedNames];
  for (let i = pool.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [pool[i], pool[j]] = [pool[j]!, pool[i]!];
  }
  visibleSuggestions.value = pool.slice(0, 5);
}

onMounted(shuffleSuggestions);

// Writable computeds let us use v-model on the selects while still proxying through
// the prop/emit pattern. v-model handles the selected-option sync more robustly than
// :value alone, especially when option lists arrive after initial render.
const selectedPrefix = computed({
  get: () => props.prefixID,
  set: (v: number) => emit("update:prefixID", v),
});

const selectedPostfix = computed({
  get: () => props.postfixID,
  set: (v: number) => emit("update:postfixID", v),
});

const prefixText = computed(
  () =>
    props.options.prefixTitles.find((t) => t.id === props.prefixID)?.text ?? "",
);

const postfixText = computed(
  () =>
    props.options.suffixTitles.find((t) => t.id === props.postfixID)?.text ?? "",
);

/** Live display showing how the full librarian name will appear in-game. */
const previewLabel = computed(() => {
  const parts: string[] = [];
  if (prefixText.value) parts.push(prefixText.value);
  parts.push(props.name || "…");
  if (postfixText.value) parts.push(postfixText.value);
  return parts.join(" ");
});
</script>

<template>
  <div class="tab-inner">
    <div class="section-label">Name</div>
    <div class="name-row">
      <input
        :value="name"
        class="name-input"
        maxlength="40"
        :disabled="busy"
        @input="emit('update:name', ($event.target as HTMLInputElement).value)"
      />
      <button
        class="icon-btn"
        title="Shuffle suggestions"
        :disabled="busy"
        @click="shuffleSuggestions"
      >
        ⟳
      </button>
    </div>

    <div v-if="visibleSuggestions.length" class="chips">
      <button
        v-for="s in visibleSuggestions"
        :key="s"
        class="chip"
        :disabled="busy"
        @click="emit('update:name', s)"
      >
        {{ s }}
      </button>
    </div>

    <div class="section-label" style="margin-top: 0.75rem;">Prefix Title</div>
    <select
      v-model.number="selectedPrefix"
      class="title-select"
      :disabled="busy"
    >
      <option :value="0">— None —</option>
      <option v-for="t in options.prefixTitles" :key="t.id" :value="t.id">
        {{ t.text }}
      </option>
    </select>

    <div class="section-label" style="margin-top: 0.5rem;">Suffix Title</div>
    <select
      v-model.number="selectedPostfix"
      class="title-select"
      :disabled="busy"
    >
      <option :value="0">— None —</option>
      <option v-for="t in options.suffixTitles" :key="t.id" :value="t.id">
        {{ t.text }}
      </option>
    </select>

    <div class="section-label" style="margin-top: 0.75rem;">Preview</div>
    <div class="name-preview">{{ previewLabel }}</div>
  </div>
</template>

<style scoped>
.tab-inner {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.section-label {
  font-size: 0.6rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-3);
}

.name-row {
  display: flex;
  gap: 0.4rem;
}

.name-input {
  flex: 1;
  padding: 0.35rem 0.5rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: var(--bg);
  color: var(--text-1);
  font-size: 0.8rem;
}

.icon-btn {
  padding: 0.3rem 0.55rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: transparent;
  color: var(--text-2);
  font-size: 0.9rem;
  cursor: pointer;
  transition: color 0.1s;
}

.icon-btn:hover:not(:disabled) {
  color: var(--gold);
}

.icon-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.3rem;
}

.chip {
  padding: 0.15rem 0.45rem;
  border-radius: 3px;
  border: 1px solid var(--border);
  background: var(--bg-card-2);
  color: var(--text-2);
  font-size: 0.65rem;
  cursor: pointer;
  transition: color 0.1s, border-color 0.1s;
}

.chip:hover:not(:disabled) {
  color: var(--gold);
  border-color: var(--gold-dim);
}

.chip:disabled {
  opacity: 0.4;
  cursor: default;
}

.title-select {
  width: 100%;
  padding: 0.3rem 0.5rem;
  border-radius: 4px;
  border: 1px solid var(--border-mid);
  background: var(--bg);
  color: var(--text-1);
  font-size: 0.75rem;
}

.name-preview {
  padding: 0.5rem 0.8rem;
  background: var(--bg-card-2);
  border: 1px solid var(--border);
  border-radius: 4px;
  font-family: var(--font-display);
  font-size: 0.85rem;
  color: var(--gold);
  text-align: center;
  margin-top: 0.1rem;
}
</style>
