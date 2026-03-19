<!--
  LibrarianManager.vue

  Librarian roster for the main scene (any UIPhase except BattleSetting).
  A wrap-grid of floor tiles across the top switches between floors; all
  occupied floors are visible at once — no horizontal scrolling.

  Each floor tile shows the floor's stage icon, official name, and current
  realization level (Roman numeral). The active tile is highlighted with the
  floor's accent color.

  Below the tiles the active floor shows:
    • Floor banner: official name + level badge
    • Abnormality/EGO pages section (collapsed; grouped by unlock level, displayed as HandCard tiles)
    • Compact librarian tiles — click Edit to open the full EditPanel overlay

  Props:
    state       – full game state (scene = 'main', floors array present)
    session     – current session identity
    players     – connected player list
    sendAction  – WebSocket action dispatcher
    claimUnit   – claim a librarian unit
    releaseUnit – release a librarian unit
    renamePlayer – rename the current player
-->
<script setup lang="ts">
import type {
  GameState,
  LibrarianEntry,
  PlayerInfo,
  SessionState,
  ActionResult,
  AvailableKeyPage,
  AvailableCard,
  Card,
  DeckCardPreview,
} from "~/types/game";

const props = defineProps<{
  state: GameState;
  session: SessionState | null;
  players: PlayerInfo[];
  sendAction: (action: Record<string, unknown>) => Promise<ActionResult>;
  claimUnit: (unitId: number) => Promise<ActionResult>;
  releaseUnit: (unitId: number) => Promise<ActionResult>;
  renamePlayer: (name: string) => Promise<ActionResult>;
  lockLibrarian: (
    floorIndex: number,
    unitIndex: number,
  ) => Promise<ActionResult>;
  unlockLibrarian: (
    floorIndex: number,
    unitIndex: number,
  ) => Promise<ActionResult>;
  renameLibrarian: (
    floorIndex: number,
    unitIndex: number,
    name: string,
  ) => Promise<ActionResult>;
  equipKeyPage: (
    floorIndex: number,
    unitIndex: number,
    bookInstanceId: number,
  ) => Promise<ActionResult>;
  addCardToDeck: (
    floorIndex: number,
    unitIndex: number,
    cardId: number,
    packageId: string,
  ) => Promise<ActionResult>;
  removeCardFromDeck: (
    floorIndex: number,
    unitIndex: number,
    cardId: number,
    packageId: string,
  ) => Promise<ActionResult>;
}>();

/** Accent color keyed by floorIndex (0 = Malkuth … 9 = Keter). */
const FLOOR_COLORS: Record<number, string> = {
  0: "#be9966", // Malkuth
  1: "#6968c4", // Yesod
  2: "#e5881b", // Hod
  3: "#4ed564", // Netzach
  4: "#ffe527", // Tiphereth
  5: "#ff3326", // Gebura
  6: "#5ccaf6", // Chesed
  7: "#957704", // Binah
  8: "#7c7b7c", // Hokma
  9: "#dddddd", // Keter
};

/** Roman numeral labels for realization levels 1–6. */
const ROMAN = ["I", "II", "III", "IV", "V", "VI"] as const;

function toRoman(level: number): string {
  return ROMAN[level - 1] ?? String(level);
}

function floorColor(floorIdx: number): string {
  return FLOOR_COLORS[floorIdx] ?? "#888";
}

function floorIconUrl(floorIdx: number): string {
  // SephirahType is 1-indexed; floorIndex is 0-indexed.
  return `/assets/stageicons/${floorIdx + 1}.png`;
}

const floors = computed(() => props.state.floors ?? []);

const totalLibrarians = computed(() =>
  floors.value.reduce((s, f) => s + f.librarians.length, 0),
);

/** Currently selected floor index. Defaults to the first available floor. */
const activeFloor = ref<number | null>(null);

watchEffect(() => {
  if (activeFloor.value === null && floors.value.length > 0)
    activeFloor.value = floors.value[0]!.floorIndex;
});

const activeFloorData = computed(
  () => floors.value.find((f) => f.floorIndex === activeFloor.value) ?? null,
);

function selectFloor(idx: number): void {
  if (activeFloor.value !== idx) {
    activeFloor.value = idx;
    emotionCardsOpen.value = false;
    egoCardsOpen.value = false;
  }
}

/** Librarian currently open in the EditPanel, or null. */
const editingLibrarian = ref<LibrarianEntry | null>(null);

function openEdit(lib: LibrarianEntry): void {
  editingLibrarian.value = lib;
}

function closeEdit(): void {
  editingLibrarian.value = null;
}

// Keep the editingLibrarian in sync with state updates (e.g. after a rename).
watch(
  () => props.state.floors,
  () => {
    const cur = editingLibrarian.value;
    if (!cur) return;
    const floor = props.state.floors?.find((f) => f.floorIndex === cur.floorIndex);
    const lib = floor?.librarians.find((l) => l.unitIndex === cur.unitIndex);
    if (lib) editingLibrarian.value = lib;
  },
  { deep: true },
);

// ── EditPanel action callbacks ─────────────────────────────────────────────

async function onLock(): Promise<ActionResult> {
  const lib = editingLibrarian.value;
  if (!lib) return { ok: false, error: "No librarian selected" };
  return props.lockLibrarian(lib.floorIndex, lib.unitIndex);
}

async function onUnlock(): Promise<ActionResult> {
  const lib = editingLibrarian.value;
  if (!lib) return { ok: false, error: "No librarian selected" };
  return props.unlockLibrarian(lib.floorIndex, lib.unitIndex);
}

async function onRename(name: string): Promise<ActionResult> {
  const lib = editingLibrarian.value;
  if (!lib) return { ok: false, error: "No librarian selected" };
  return props.renameLibrarian(lib.floorIndex, lib.unitIndex, name);
}

async function onEquipPage(kp: AvailableKeyPage): Promise<void> {
  const lib = editingLibrarian.value;
  if (!lib) return;
  await props.equipKeyPage(lib.floorIndex, lib.unitIndex, kp.instanceId);
}

async function onAddCard(card: AvailableCard): Promise<void> {
  const lib = editingLibrarian.value;
  if (!lib) return;
  await props.addCardToDeck(lib.floorIndex, lib.unitIndex, card.cardId.id, card.cardId.packageId);
}

async function onRemoveCard(card: DeckCardPreview): Promise<void> {
  const lib = editingLibrarian.value;
  if (!lib || !card.cardId) return;
  await props.removeCardFromDeck(
    lib.floorIndex,
    lib.unitIndex,
    card.cardId.id,
    card.cardId.packageId,
  );
}

// ── Abnormality / EGO sections ─────────────────────────────────────────────

/** Whether the abnormality pages section is expanded for the active floor. */
const emotionCardsOpen = ref(false);

/** Whether the EGO pages section is expanded for the active floor. */
const egoCardsOpen = ref(false);

/**
 * Groups emotion cards by their unlock level (each level = one abnormality
 * encounter on this floor).
 */
const groupedEmotionCards = computed(() => {
  const floor = activeFloorData.value;
  if (!floor) return [];
  const map = new Map<number, typeof floor.emotionCards>();
  for (const ec of floor.emotionCards) {
    if (!map.has(ec.level)) map.set(ec.level, []);
    map.get(ec.level)!.push(ec);
  }
  return [...map.entries()].map(([level, cards]) => ({ level, cards }));
});

/** Card currently shown in the full CardDetail overlay. */
const detailCard = ref<Card | null>(null);

/**
 * Converts a DeckCardPreview EGO card to a Card with the EGO option set,
 * which triggers crimson border and the EGO tag in CardDetail.
 */
function egoCardToCard(p: DeckCardPreview, i: number): Card {
  return { ...previewToCard(p, i), options: ["EGO"] };
}

/**
 * Converts a DeckCardPreview to a minimal Card shape for HandCard rendering.
 * id/index are set to the list position — HandCard does not use them for actions.
 */
function previewToCard(p: DeckCardPreview, i: number): Card {
  return {
    id: { id: i, packageId: 0 },
    index: i,
    name: p.name,
    cost: p.cost,
    range: p.range,
    rarity: p.rarity,
    dice: p.dice,
    abilityDesc: p.abilityDesc,
  };
}
</script>

<template>
  <div class="lib-manager">
    <div class="lib-header">
      <span class="lib-title">Library</span>
      <span class="lib-sub"
        >{{ totalLibrarians }} librarian{{
          totalLibrarians === 1 ? "" : "s"
        }}</span
      >
    </div>

    <div v-if="floors.length === 0" class="lib-empty">
      No librarian data available.
    </div>

    <template v-else>
      <!-- Floor tile grid — all floors visible at once, wraps on narrow viewports -->
      <div class="floor-grid">
        <button
          v-for="floor in floors"
          :key="floor.floorIndex"
          class="floor-tile"
          :class="{ active: activeFloor === floor.floorIndex }"
          :style="{ '--floor-color': floorColor(floor.floorIndex) }"
          @click="selectFloor(floor.floorIndex)"
        >
          <img
            :src="floorIconUrl(floor.floorIndex)"
            class="tile-icon"
            :alt="floor.officialName"
            @error="($event.target as HTMLImageElement).style.display = 'none'"
          />
          <span class="tile-name">{{ floor.officialName }}</span>
          <span class="tile-level">{{ toRoman(floor.realizationLevel) }}</span>
        </button>
      </div>

      <!-- Active floor content -->
      <div v-if="activeFloorData" class="floor-content">
        <!-- Floor banner -->
        <div
          class="floor-banner"
          :style="{ borderLeftColor: floorColor(activeFloorData.floorIndex) }"
        >
          <span
            class="banner-name"
            :style="{ color: floorColor(activeFloorData.floorIndex) }"
            >{{ activeFloorData.officialName }}</span
          >
          <span
            class="banner-level"
            :style="{ color: floorColor(activeFloorData.floorIndex) }"
            >{{ toRoman(activeFloorData.realizationLevel) }}</span
          >
        </div>

        <!-- Emotion cards (collapsed toggle) -->
        <div
          v-if="activeFloorData.emotionCards.length"
          class="section-toggle"
          @click="emotionCardsOpen = !emotionCardsOpen"
        >
          <span class="section-toggle-label">Abnormality Pages</span>
          <span class="section-toggle-count">{{
            activeFloorData.emotionCards.length
          }}</span>
          <span class="chevron" :class="{ open: emotionCardsOpen }">▸</span>
        </div>

        <div v-if="emotionCardsOpen" class="emotion-groups">
          <div
            v-for="group in groupedEmotionCards"
            :key="group.level"
            class="emotion-group"
          >
            <div class="emotion-group-label">
              {{ toRoman(group.level) }}
              <span
                v-if="group.cards[0]?.abnormalityName"
                class="emotion-group-name"
              >
                — {{ group.cards[0].abnormalityName }}
              </span>
            </div>
            <div class="em-cards">
              <AbnormalityPageCard
                v-for="ec in group.cards"
                :key="ec.name"
                :name="ec.name"
                :state="ec.state"
                :emotion-level="ec.emotionLevel"
                :target-type="ec.targetType"
                :desc="ec.desc"
                :flavor-text="ec.flavorText"
                readonly
              />
            </div>
          </div>
        </div>

        <!-- EGO pages section (full battle cards, separate from abnormality pages) -->
        <div
          v-if="activeFloorData.egoCards.length"
          class="section-toggle"
          @click="egoCardsOpen = !egoCardsOpen"
        >
          <span class="section-toggle-label">EGO Pages</span>
          <span class="section-toggle-count">{{
            activeFloorData.egoCards.length
          }}</span>
          <span class="chevron" :class="{ open: egoCardsOpen }">▸</span>
        </div>

        <div v-if="egoCardsOpen" class="emotion-groups">
          <div class="emotion-cards">
            <HandCard
              v-for="(card, i) in activeFloorData.egoCards"
              :key="card.name"
              :card="egoCardToCard(card, i)"
              readonly
              @detail="detailCard = egoCardToCard(card, i)"
            />
          </div>
        </div>

        <!-- Compact librarian tiles -->
        <div
          v-for="lib in activeFloorData.librarians"
          :key="`${lib.floorIndex}:${lib.unitIndex}`"
          class="lib-tile"
        >
          <div class="tile-main">
            <div class="tile-info">
              <span class="tile-name-text">{{ lib.name }}</span>
              <span class="tile-page-name">{{ lib.keyPage.name }}</span>
            </div>
            <div class="tile-meta">
              <span class="meta-chip">
                {{ lib.keyPage.speedMin }}–{{ lib.keyPage.speedMax }}
              </span>
              <span
                v-if="lib.passives.length"
                class="meta-chip meta-chip--passive"
              >
                {{ lib.passives.length }}P
              </span>
              <span
                v-if="lib.deckPreview.length"
                class="meta-chip meta-chip--deck"
              >
                {{ lib.deckPreview.reduce((s, c) => s + c.count, 0) }}
              </span>
              <!-- Lock badge -->
              <span
                v-if="lib.lockedBy"
                class="meta-chip meta-chip--locked"
                :title="`Being edited by ${lib.lockedBy}`"
              >
                ✎ {{ lib.lockedBy }}
              </span>
              <button class="edit-btn" title="Edit librarian" @click="openEdit(lib)">
                Edit
              </button>
            </div>
          </div>
        </div>
      </div>
    </template>

    <!-- Full-screen EditPanel overlay -->
    <LibrarianEditPanel
      v-if="editingLibrarian"
      :lib="editingLibrarian"
      :state="state"
      :session="session"
      :floor-color="floorColor(editingLibrarian.floorIndex)"
      :on-close="closeEdit"
      :on-lock="onLock"
      :on-unlock="onUnlock"
      :on-rename="onRename"
      :on-equip-page="onEquipPage"
      :on-add-card="onAddCard"
      :on-remove-card="onRemoveCard"
    />

    <CardDetail
      v-if="detailCard"
      :card="detailCard"
      @close="detailCard = null"
    />
  </div>
</template>

<style scoped>
.lib-manager {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  padding: 0.5rem 0;
}

.lib-header {
  display: flex;
  align-items: baseline;
  gap: 0.75rem;
}

.lib-title {
  font-family: var(--font-display);
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--gold);
  letter-spacing: 0.1em;
  text-transform: uppercase;
}

.lib-sub {
  font-size: 0.68rem;
  color: var(--text-2);
}

.lib-empty {
  color: var(--text-2);
  font-size: 0.8rem;
  padding: 2rem 0;
  text-align: center;
}

/* ── Floor tile grid ───────────────────────────────────────────────────────── */
/*
 * Tiles wrap rather than scroll, so all occupied floors are always visible.
 * --floor-color is injected per tile via inline style.
 */
.floor-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 0.3rem;
}

.floor-tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.15rem;
  padding: 0.3rem 0.5rem 0.25rem;
  background: var(--bg-card);
  border: 1px solid var(--border);
  margin-top: 1px;
  border-radius: 3px;
  cursor: pointer;
  transition:
    border-top-color 0.15s,
    background 0.12s,
    opacity 0.15s;
  min-width: 3rem;
}

.floor-tile:hover {
  background: var(--bg-card-2);
  border-top-color: color-mix(in srgb, var(--floor-color) 50%, transparent);
}

.floor-tile.active {
  border-top: 2px solid var(--floor-color);
  margin-top: 0px;
  background: var(--bg-card-2);
}

.tile-icon {
  width: 18px;
  height: 18px;
  object-fit: contain;
  opacity: 0.7;
  transition: opacity 0.15s;
}

.floor-tile.active .tile-icon,
.floor-tile:hover .tile-icon {
  opacity: 1;
}

.tile-name {
  font-family: var(--font-display);
  font-size: 0.52rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-2);
  white-space: nowrap;
}

.floor-tile.active .tile-name {
  color: var(--floor-color);
}

.tile-level {
  font-family: var(--font-display);
  font-size: 0.48rem;
  color: var(--text-3);
  letter-spacing: 0.05em;
}

.floor-tile.active .tile-level {
  color: var(--floor-color);
  opacity: 0.8;
}

/* ── Active floor content ──────────────────────────────────────────────────── */
.floor-content {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

/* ── Floor banner ──────────────────────────────────────────────────────────── */
.floor-banner {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.3rem 0.5rem;
  border-left: 3px solid var(--border);
  background: var(--bg-card);
  border-radius: 0 3px 3px 0;
}

.banner-name {
  font-family: var(--font-display);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.1em;
  text-transform: uppercase;
}

.banner-level {
  font-family: var(--font-display);
  font-size: 0.62rem;
  opacity: 0.7;
  margin-left: -0.25rem;
}

/* ── Abnormality page cards ─────────────────────────────────────────────────── */
.section-toggle {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.25rem 0.5rem;
  cursor: pointer;
  user-select: none;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 3px;
}

.section-toggle:hover {
  border-color: var(--border-mid);
}

.section-toggle-label {
  font-family: var(--font-display);
  font-size: 0.55rem;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--text-2);
}

.section-toggle-count {
  font-size: 0.55rem;
  color: var(--text-3);
  background: var(--bg-card-2);
  border: 1px solid var(--border);
  border-radius: 2px;
  padding: 0 0.25rem;
}

.chevron {
  font-size: 0.65rem;
  color: var(--text-3);
  display: inline-block;
  transition: transform 0.18s ease;
  margin-left: auto;
}

.chevron.open {
  transform: rotate(90deg);
}

.emotion-groups {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  padding: 0.3rem 0.5rem;
  background: var(--bg-card-2);
  border: 1px solid var(--border);
  border-radius: 3px;
}

.emotion-group-label {
  font-family: var(--font-display);
  font-size: 0.48rem;
  text-transform: uppercase;
  letter-spacing: 0.12em;
  color: var(--text-3);
  margin-bottom: 0.1rem;
}

.emotion-group-name {
  color: var(--text-2);
}

.em-cards {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

@media (min-width: 500px) {
  .em-cards {
    flex-direction: row;
    flex-wrap: wrap;
    align-items: flex-start;
  }
}

.emotion-cards {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}

/* ── Compact librarian tile ─────────────────────────────────────────────────── */
.lib-tile {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 3px;
  overflow: hidden;
  transition: border-color 0.15s;
}

.lib-tile:hover {
  border-color: var(--border-mid);
}

.tile-main {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  padding: 0.45rem 0.6rem;
}

.tile-info {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
  min-width: 0;
}

.tile-name-text {
  font-family: var(--font-display);
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--text-1);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.tile-page-name {
  font-size: 0.62rem;
  color: var(--text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.tile-meta {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  flex-shrink: 0;
}

.meta-chip {
  font-size: 0.6rem;
  font-family: var(--font-body);
  color: var(--text-2);
  background: var(--bg-card-2);
  border: 1px solid var(--border);
  border-radius: 2px;
  padding: 0.1rem 0.3rem;
  white-space: nowrap;
}

.meta-chip--passive {
  color: var(--rarity-rare);
  border-color: #2a3a6a;
}

.meta-chip--locked {
  color: var(--gold);
  border-color: var(--gold-dim);
}

.edit-btn {
  padding: 0.2rem 0.55rem;
  background: none;
  border: 1px solid var(--border);
  border-radius: 2px;
  color: var(--text-2);
  font-size: 0.62rem;
  cursor: pointer;
  transition: color 0.12s, border-color 0.12s;
  white-space: nowrap;
}

.edit-btn:hover {
  color: var(--gold);
  border-color: var(--gold-dim);
}
</style>
