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
    • Individual librarian cards (expandable for resistances / passives / deck)

  Props:
    state       – full game state (scene = 'main', floors array present)
    session     – current session identity (unused in Batch 1, reserved)
    players     – connected player list (unused in Batch 1, reserved)
    sendAction  – WebSocket action dispatcher (unused in Batch 1, reserved)
    claimUnit   – claim a librarian unit (unused in Batch 1, reserved)
    releaseUnit – release a librarian unit (unused in Batch 1, reserved)
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
    expandedKey.value = null;
    emotionCardsOpen.value = false;
    egoCardsOpen.value = false;
    editTab.value = "keypage";
    rarityFilter.value = "All";
  }
}

/** Key of the currently expanded librarian card, or null. */
const expandedKey = ref<string | null>(null);

/**
 * Key of the librarian card currently in edit mode (this session holds the lock).
 * Format: "floorIndex:unitIndex"
 */
const editingKey = ref<string | null>(null);

/** Pending rename value while the inline input is open. */
const editName = ref("");

/** True while a lock/unlock/rename request is in-flight. */
const editBusy = ref(false);

/**
 * Whether this session holds the lock for a given librarian.
 * We track this client-side so we can show the edit button only
 * when the librarian is not locked by anyone else.
 */
function isLockedByOther(lib: LibrarianEntry): boolean {
  return !!lib.lockedBy;
}

function isEditingThis(lib: LibrarianEntry): boolean {
  return editingKey.value === cardKey(lib);
}

async function startEdit(lib: LibrarianEntry): Promise<void> {
  if (editBusy.value) return;
  editBusy.value = true;
  const result = await props.lockLibrarian(lib.floorIndex, lib.unitIndex);
  editBusy.value = false;
  if (!result.ok) return;
  editingKey.value = cardKey(lib);
  editName.value = lib.name;
  // Ensure the card is expanded so the edit panel is visible.
  expandedKey.value = editingKey.value;
}

async function commitEdit(lib: LibrarianEntry): Promise<void> {
  if (editBusy.value) return;
  const key = cardKey(lib);
  const trimmed = editName.value.trim();
  editBusy.value = true;
  try {
    if (trimmed && trimmed !== lib.name) {
      const result = await props.renameLibrarian(
        lib.floorIndex,
        lib.unitIndex,
        trimmed,
      );
      if (!result.ok) return;
    }
    await props.unlockLibrarian(lib.floorIndex, lib.unitIndex);
  } finally {
    editBusy.value = false;
    if (editingKey.value === key) editingKey.value = null;
  }
}

async function cancelEdit(lib: LibrarianEntry): Promise<void> {
  if (editBusy.value) return;
  const key = cardKey(lib);
  editBusy.value = true;
  await props.unlockLibrarian(lib.floorIndex, lib.unitIndex);
  editBusy.value = false;
  if (editingKey.value === key) editingKey.value = null;
}

/** Which edit sub-panel is active when a card is in edit mode. */
const editTab = ref<"keypage" | "deck">("keypage");

/** Rarity filter for the "Add cards" list in the deck editor. */
const rarityFilter = ref("All");

/**
 * Available key pages grouped by story line (bookIcon), sorted by chapter then
 * bookIcon — matching the in-game equip screen's UIStoryLine organisation.
 */
/**
 * Available key pages grouped by story line (bookIcon).
 * Insertion order matches the game's sort (chapter desc → workshopId asc → storyLine desc),
 * which is applied server-side before serialization.
 */
const groupedKeyPages = computed(() => {
  const pages = props.state.availableKeyPages ?? [];
  const groups = new Map<
    string,
    { chapter: number; bookIcon: string; pages: AvailableKeyPage[] }
  >();
  for (const kp of pages) {
    if (!groups.has(kp.bookIcon)) {
      groups.set(kp.bookIcon, {
        chapter: kp.chapter,
        bookIcon: kp.bookIcon,
        pages: [],
      });
    }
    groups.get(kp.bookIcon)!.pages.push(kp);
  }
  return [...groups.values()];
});

/** Distinct rarity values present in the available card inventory, plus "All". */
const rarityFilters = computed(() => {
  const rarities = new Set<string>();
  for (const c of props.state.availableCards ?? []) {
    if (c.rarity) rarities.add(c.rarity);
  }
  return ["All", ...Array.from(rarities).sort()];
});

/** Available cards filtered by the selected rarity. */
const filteredAvailableCards = computed(() => {
  const cards = props.state.availableCards ?? [];
  if (rarityFilter.value === "All") return cards;
  return cards.filter((c) => c.rarity === rarityFilter.value);
});

async function doEquipKeyPage(
  lib: LibrarianEntry,
  kp: AvailableKeyPage,
): Promise<void> {
  if (editBusy.value) return;
  editBusy.value = true;
  await props.equipKeyPage(lib.floorIndex, lib.unitIndex, kp.instanceId);
  editBusy.value = false;
}

async function doAddCard(
  lib: LibrarianEntry,
  card: AvailableCard,
): Promise<void> {
  if (editBusy.value) return;
  editBusy.value = true;
  await props.addCardToDeck(
    lib.floorIndex,
    lib.unitIndex,
    card.cardId.id,
    card.cardId.packageId,
  );
  editBusy.value = false;
}

async function doRemoveCard(
  lib: LibrarianEntry,
  card: DeckCardPreview,
): Promise<void> {
  if (editBusy.value || !card.cardId) return;
  editBusy.value = true;
  await props.removeCardFromDeck(
    lib.floorIndex,
    lib.unitIndex,
    card.cardId.id,
    card.cardId.packageId,
  );
  editBusy.value = false;
}

/** Whether the abnormality pages section is expanded for the active floor. */
const emotionCardsOpen = ref(false);

/** Whether the EGO pages section is expanded for the active floor. */
const egoCardsOpen = ref(false);

/** Returns a color for each card rarity for use in inline styles. */
function rarityColor(rarity?: string): string {
  switch (rarity) {
    case "Uncommon":
      return "var(--rarity-uncommon, #81c784)";
    case "Rare":
      return "var(--rarity-rare, #4fc3f7)";
    case "Unique":
      return "var(--rarity-unique, #ce93d8)";
    default:
      return "var(--text-3)";
  }
}

function cardKey(lib: LibrarianEntry): string {
  return `${activeFloor.value}:${lib.unitIndex}`;
}

function toggleExpand(lib: LibrarianEntry): void {
  const key = cardKey(lib);
  expandedKey.value = expandedKey.value === key ? null : key;
  detailCard.value = null;
}

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

        <!-- Librarian cards -->
        <div
          v-for="lib in activeFloorData.librarians"
          :key="cardKey(lib)"
          class="lib-card"
          :class="{ 'lib-card--expanded': expandedKey === cardKey(lib) }"
          @click="toggleExpand(lib)"
        >
          <!-- Card header row -->
          <div class="card-header">
            <div class="card-main">
              <!-- Inline rename input (shown while this session holds the lock) -->
              <input
                v-if="isEditingThis(lib)"
                v-model="editName"
                class="name-input"
                maxlength="40"
                @click.stop
                @keydown.enter.prevent="commitEdit(lib)"
                @keydown.escape.prevent="cancelEdit(lib)"
              />
              <span v-else class="card-name">{{ lib.name }}</span>
              <span class="card-page">{{ lib.keyPage.name }}</span>
            </div>
            <div class="card-meta">
              <span class="meta-chip">
                {{ lib.keyPage.speedMin }}–{{ lib.keyPage.speedMax }}
              </span>
              <span
                v-if="lib.passives.length"
                class="meta-chip meta-chip--passive"
              >
                {{ lib.passives.length }} passive{{
                  lib.passives.length === 1 ? "" : "s"
                }}
              </span>
              <span
                v-if="lib.deckPreview.length"
                class="meta-chip meta-chip--deck"
              >
                {{ lib.deckPreview.reduce((s, c) => s + c.count, 0) }} cards
              </span>
              <!-- Edit / Done / Cancel controls -->
              <template v-if="isEditingThis(lib)">
                <button
                  class="edit-btn edit-btn--done"
                  :disabled="editBusy"
                  title="Save name"
                  @click.stop="commitEdit(lib)"
                >
                  ✓
                </button>
                <button
                  class="edit-btn edit-btn--cancel"
                  :disabled="editBusy"
                  title="Cancel"
                  @click.stop="cancelEdit(lib)"
                >
                  ✕
                </button>
              </template>
              <button
                v-else-if="!isLockedByOther(lib)"
                class="edit-btn"
                :disabled="editBusy"
                title="Edit librarian"
                @click.stop="startEdit(lib)"
              >
                ✎
              </button>
              <span
                v-else
                class="meta-chip meta-chip--locked"
                :title="'Being edited by ' + lib.lockedBy"
                >✎ {{ lib.lockedBy }}</span
              >
              <span
                class="chevron"
                :class="{ open: expandedKey === cardKey(lib) }"
                >▸</span
              >
            </div>
          </div>

          <!-- Expandable detail -->
          <div
            v-if="expandedKey === cardKey(lib)"
            class="card-detail"
            @click.stop
          >
            <div class="section-label">Resistances</div>
            <UnitResistanceTable
              v-if="lib.keyPage.resistances"
              :resistances="lib.keyPage.resistances"
            />

            <template v-if="lib.passives.length">
              <div class="section-label">Passives</div>
              <UnitPassiveList :passives="lib.passives" />
            </template>

            <template v-if="lib.deckPreview.length">
              <div class="section-label">
                Deck
                <span class="section-count">
                  ({{ lib.deckPreview.reduce((s, c) => s + c.count, 0) }})
                </span>
              </div>
              <div class="deck-cards">
                <HandCard
                  v-for="(card, i) in lib.deckPreview"
                  :key="i"
                  :card="previewToCard(card, i)"
                  :count="card.count"
                  readonly
                  @detail="detailCard = previewToCard(card, i)"
                />
              </div>
            </template>

            <!-- Edit panels: key page equip and deck management (edit mode only) -->
            <template v-if="isEditingThis(lib)">
              <div class="edit-section-tabs">
                <button
                  class="edit-section-tab"
                  :class="{ active: editTab === 'keypage' }"
                  @click.stop="editTab = 'keypage'"
                >
                  Key Page
                </button>
                <button
                  class="edit-section-tab"
                  :class="{ active: editTab === 'deck' }"
                  @click.stop="editTab = 'deck'"
                >
                  Deck
                </button>
              </div>

              <!-- Key page picker — grouped by story line, matching in-game order -->
              <div v-if="editTab === 'keypage'" class="edit-panel">
                <div v-if="!groupedKeyPages.length" class="edit-empty">
                  No key pages in inventory.
                </div>
                <template
                  v-for="group in groupedKeyPages"
                  :key="group.bookIcon"
                >
                  <div class="kp-group-label">{{ group.bookIcon }}</div>
                  <div
                    v-for="kp in group.pages"
                    :key="kp.instanceId"
                    class="kp-row"
                    :class="{
                      'kp-row--current':
                        kp.instanceId === lib.keyPage.instanceId,
                    }"
                  >
                    <span class="kp-name">{{ kp.name }}</span>
                    <span class="kp-speed"
                      >{{ kp.speedMin }}–{{ kp.speedMax }}</span
                    >
                    <button
                      class="equip-btn"
                      :disabled="
                        editBusy || kp.instanceId === lib.keyPage.instanceId
                      "
                      title="Equip this key page"
                      @click.stop="doEquipKeyPage(lib, kp)"
                    >
                      →
                    </button>
                  </div>
                </template>
              </div>

              <!-- Deck editor -->
              <div v-if="editTab === 'deck'" class="edit-panel">
                <div class="section-label">Current deck</div>
                <div v-if="!lib.deckPreview.length" class="edit-empty">
                  No cards in deck.
                </div>
                <div v-else class="deck-edit-list">
                  <div
                    v-for="(card, i) in lib.deckPreview"
                    :key="i"
                    class="deck-edit-row"
                  >
                    <span class="deck-edit-name">{{ card.name }}</span>
                    <span class="deck-edit-count">×{{ card.count }}</span>
                    <button
                      class="remove-btn"
                      :disabled="editBusy || !card.cardId"
                      title="Remove one copy"
                      @click.stop="doRemoveCard(lib, card)"
                    >
                      −
                    </button>
                  </div>
                </div>

                <div class="section-label" style="margin-top: 0.4rem">
                  Add cards
                </div>
                <div class="rarity-filter">
                  <button
                    v-for="r in rarityFilters"
                    :key="r"
                    class="rarity-tab"
                    :class="{ active: rarityFilter === r }"
                    @click.stop="rarityFilter = r"
                  >
                    {{ r }}
                  </button>
                </div>
                <div v-if="!filteredAvailableCards.length" class="edit-empty">
                  No cards available.
                </div>
                <div v-else class="deck-add-list">
                  <div
                    v-for="card in filteredAvailableCards"
                    :key="card.cardId.id + '_' + card.cardId.packageId"
                    class="deck-add-row"
                  >
                    <span
                      class="deck-edit-rarity"
                      :style="{ color: rarityColor(card.rarity) }"
                      >{{ card.rarity[0] }}</span
                    >
                    <span class="deck-edit-name">{{ card.name }}</span>
                    <span class="deck-edit-count">×{{ card.count }}</span>
                    <button
                      class="add-btn"
                      :disabled="editBusy"
                      title="Add one copy to deck"
                      @click.stop="doAddCard(lib, card)"
                    >
                      +
                    </button>
                  </div>
                </div>
              </div>
            </template>
          </div>
        </div>
      </div>
    </template>

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

/* ── Abnormality page cards (mirrors AbnormalityPicker .ab-card style) ─────── */
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

/* Card visuals are owned by AbnormalityPageCard; only the flex container is set here. */
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

/* ── Individual librarian card ─────────────────────────────────────────────── */
.lib-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 3px;
  overflow: hidden;
  cursor: pointer;
  transition: border-color 0.15s;
}

.lib-card:hover {
  border-color: var(--border-mid);
}

.lib-card--expanded {
  border-color: var(--gold-dim);
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  padding: 0.45rem 0.6rem;
}

.card-main {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
  min-width: 0;
}

.card-name {
  font-family: var(--font-display);
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--text-1);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.card-page {
  font-size: 0.62rem;
  color: var(--text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.card-meta {
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

.meta-chip--deck {
  color: var(--text-2);
}

.meta-chip--locked {
  color: var(--gold);
  border-color: var(--gold-dim);
}

/* ── Expanded detail panel ─────────────────────────────────────────────────── */
.card-detail {
  padding: 0.5rem 0.6rem 0.6rem;
  border-top: 1px solid var(--border);
  background: var(--bg-card-2);
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.section-label {
  font-family: var(--font-display);
  font-size: 0.5rem;
  text-transform: uppercase;
  letter-spacing: 0.12em;
  color: var(--text-3);
  margin-top: 0.1rem;
  display: flex;
  align-items: center;
  gap: 0.3em;
}

.section-count {
  color: var(--text-3);
  font-family: var(--font-body);
  font-size: 0.58rem;
  letter-spacing: 0;
}

.deck-cards {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}

/* ── Edit controls ─────────────────────────────────────────────────────────── */
.name-input {
  font-family: var(--font-display);
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--text-1);
  background: var(--bg-card-2);
  border: 1px solid var(--gold-dim);
  border-radius: 2px;
  padding: 0.1rem 0.35rem;
  outline: none;
  width: 100%;
  max-width: 14rem;
}

.name-input:focus {
  border-color: var(--gold);
}

.edit-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 1.4rem;
  height: 1.4rem;
  padding: 0;
  background: none;
  border: 1px solid var(--border);
  border-radius: 2px;
  color: var(--text-2);
  font-size: 0.7rem;
  cursor: pointer;
  transition:
    color 0.12s,
    border-color 0.12s;
  flex-shrink: 0;
}

.edit-btn:hover:not(:disabled) {
  color: var(--gold);
  border-color: var(--gold-dim);
}

.edit-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.edit-btn--done:hover:not(:disabled) {
  color: #81c784;
  border-color: #2a4a2a;
}

.edit-btn--cancel:hover:not(:disabled) {
  color: var(--crimson);
  border-color: #5a1a1a;
}

/* ── Edit sub-panel tabs (Key Page / Deck) ───────────────────────────────── */
.edit-section-tabs {
  display: flex;
  gap: 0.15rem;
  border-bottom: 1px solid var(--border);
  margin-top: 0.25rem;
}

.edit-section-tab {
  padding: 0.2rem 0.55rem;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  color: var(--text-3);
  font-family: var(--font-display);
  font-size: 0.5rem;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  cursor: pointer;
  margin-bottom: -1px;
  transition:
    color 0.12s,
    border-color 0.12s;
}

.edit-section-tab.active {
  color: var(--gold);
  border-bottom-color: var(--gold);
}

.edit-panel {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.edit-empty {
  font-size: 0.6rem;
  color: var(--text-3);
  padding: 0.25rem 0;
}

/* Key page group header */
.kp-group-label {
  font-family: var(--font-display);
  font-size: 0.48rem;
  text-transform: uppercase;
  letter-spacing: 0.12em;
  color: var(--text-3);
  padding: 0.15rem 0 0.05rem;
  margin-top: 0.15rem;
  border-bottom: 1px solid var(--border);
}

/* Key page picker rows */
.kp-row {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.2rem 0.35rem;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 2px;
}

.kp-row--current {
  border-color: var(--gold-dim);
  background: color-mix(in srgb, var(--gold) 6%, var(--bg-card));
}

.kp-name {
  flex: 1;
  font-size: 0.65rem;
  color: var(--text-1);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.kp-speed {
  font-size: 0.58rem;
  color: var(--text-2);
  white-space: nowrap;
}

.equip-btn {
  width: 1.4rem;
  height: 1.4rem;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  background: none;
  border: 1px solid var(--border);
  border-radius: 2px;
  color: var(--text-2);
  font-size: 0.75rem;
  cursor: pointer;
  flex-shrink: 0;
  transition:
    color 0.12s,
    border-color 0.12s;
}

.equip-btn:hover:not(:disabled) {
  color: var(--gold);
  border-color: var(--gold-dim);
}

.equip-btn:disabled {
  opacity: 0.35;
  cursor: default;
}

/* Deck editor rows */
.deck-edit-list,
.deck-add-list {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.deck-edit-row,
.deck-add-row {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.18rem 0.35rem;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 2px;
}

.deck-edit-name {
  flex: 1;
  font-size: 0.63rem;
  color: var(--text-1);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.deck-edit-rarity {
  font-family: var(--font-display);
  font-size: 0.55rem;
  font-weight: 700;
  width: 0.7rem;
  text-align: center;
  flex-shrink: 0;
}

.deck-edit-count {
  font-size: 0.58rem;
  color: var(--text-3);
  white-space: nowrap;
}

.remove-btn,
.add-btn {
  width: 1.4rem;
  height: 1.4rem;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  background: none;
  border: 1px solid var(--border);
  border-radius: 2px;
  font-size: 0.85rem;
  cursor: pointer;
  flex-shrink: 0;
  transition:
    color 0.12s,
    border-color 0.12s;
}

.remove-btn {
  color: var(--crimson);
}

.remove-btn:hover:not(:disabled) {
  border-color: #5a1a1a;
}

.add-btn {
  color: #81c784;
}

.add-btn:hover:not(:disabled) {
  border-color: #2a4a2a;
}

.remove-btn:disabled,
.add-btn:disabled {
  opacity: 0.35;
  cursor: default;
}

/* Rarity filter tabs */
.rarity-filter {
  display: flex;
  flex-wrap: wrap;
  gap: 0.2rem;
  margin-bottom: 0.1rem;
}

.rarity-tab {
  padding: 0.1rem 0.35rem;
  background: none;
  border: 1px solid var(--border);
  border-radius: 2px;
  color: var(--text-3);
  font-size: 0.52rem;
  cursor: pointer;
  transition:
    color 0.12s,
    border-color 0.12s;
}

.rarity-tab.active {
  color: var(--gold);
  border-color: var(--gold-dim);
}

.rarity-tab:hover:not(.active) {
  color: var(--text-1);
}
</style>
