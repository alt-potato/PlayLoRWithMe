<!--
  Floating dev-only overlay for swapping the active mock fixture at runtime.
  Rendered only when a fixture is active (window.__plwmMock is installed) and
  the user has not dismissed the picker this session.

  Clicking a button swaps the fixture via __plwmMock.setFixture, updates the
  URL's `mock` query param via history.replaceState so the selection is
  bookmarkable, and writes-through to localStorage so a refresh preserves it.

  Close state is held in useState so it survives Vite HMR reloads — without
  this, tweaking a nearby component would re-mount the picker and resurrect it.
-->
<script setup lang="ts">
import { FIXTURE_LOADERS } from "./fixtures";

const STORAGE_KEY = "plwm_mock_fixture";
const QUERY_PARAM = "mock";

const fixtures = Object.keys(FIXTURE_LOADERS);
const current = ref<string | null>(null);
const isClosed = useState<boolean>("plwm-dev-picker-closed", () => false);

onMounted(() => {
  // window.__plwmMock is only installed by useMockBackend when a fixture is
  // active. absence means we are on a normal (non-mock) page load and there
  // is nothing for the picker to do.
  if (typeof window !== "undefined" && window.__plwmMock) {
    current.value = localStorage.getItem(STORAGE_KEY);
  }
});

const visible = computed(() => current.value !== null && !isClosed.value);

function select(name: string) {
  if (typeof window === "undefined" || !window.__plwmMock) return;
  window.__plwmMock.setFixture(name);
  current.value = name;
  localStorage.setItem(STORAGE_KEY, name);
  const url = new URL(location.href);
  url.searchParams.set(QUERY_PARAM, name);
  history.replaceState(null, "", url);
}

function close() {
  isClosed.value = true;
}
</script>

<template>
  <div v-if="visible" class="dev-picker" role="complementary" aria-label="Mock fixture picker">
    <div class="dev-picker-head">
      <span class="dev-picker-title">Mock fixture</span>
      <button
        type="button"
        class="dev-picker-close"
        aria-label="Hide picker"
        @click="close"
      >×</button>
    </div>
    <div class="dev-picker-body">
      <button
        v-for="name in fixtures"
        :key="name"
        type="button"
        class="dev-picker-btn"
        :class="{ active: name === current }"
        @click="select(name)"
      >
        {{ name }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.dev-picker {
  position: fixed;
  bottom: 1rem;
  right: 1rem;
  z-index: 9999;
  min-width: 180px;
  background: var(--bg-card, #0e1217);
  border: 1px solid var(--border-gold, rgba(201, 162, 39, 0.4));
  padding: 0.5rem;
  font-family: var(--font-mono, monospace);
  font-size: 0.8rem;
  box-shadow: var(--shadow-md, 0 2px 10px rgba(0, 0, 0, 0.55));
}
.dev-picker-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.35rem;
  padding-bottom: 0.25rem;
  border-bottom: 1px solid var(--border, #232934);
}
.dev-picker-title {
  color: var(--gold, #c9a227);
  text-transform: uppercase;
  letter-spacing: 0.1em;
  font-size: 0.72rem;
}
.dev-picker-close {
  background: transparent;
  border: 0;
  color: var(--text-2, #8b7f68);
  cursor: pointer;
  font-size: 1.1em;
  line-height: 1;
  padding: 0 0.3em;
}
.dev-picker-close:hover {
  color: var(--text-1, #e8dfc6);
}
.dev-picker-body {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}
.dev-picker-btn {
  background: var(--bg-card-2, #131820);
  color: var(--text-1, #e8dfc6);
  border: 1px solid var(--border, #232934);
  padding: 0.25rem 0.5rem;
  font-family: inherit;
  font-size: inherit;
  cursor: pointer;
  text-align: left;
}
.dev-picker-btn:hover {
  background: var(--bg-card-3, #1b2028);
  border-color: var(--border-hi, #475060);
}
.dev-picker-btn.active {
  border-color: var(--gold, #c9a227);
  color: var(--gold, #c9a227);
}
</style>
