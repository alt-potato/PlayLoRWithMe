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
    claimUnit   – claim a librarian unit
    releaseUnit – release a librarian unit
    renamePlayer – rename the current player

  Injected (LIBRARIAN_ACTIONS):
    sendAction, lockLibrarian, unlockLibrarian, renameLibrarian,
    equipKeyPage, addCardToDeck, removeCardFromDeck
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
  CustomizePayload,
  FashionBook,
  SetGiftsPayload,
} from "~/types/game";
import { LIBRARIAN_ACTIONS, floorColor } from "~/composables/useLibrarianActions";

const props = defineProps<{
  state: GameState;
  session: SessionState | null;
  players: PlayerInfo[];
  claimUnit: (unitId: number) => Promise<ActionResult>;
  releaseUnit: (unitId: number) => Promise<ActionResult>;
  renamePlayer: (name: string) => Promise<ActionResult>;
}>();

const actions = inject(LIBRARIAN_ACTIONS)!;

function floorIconUrl(floorIdx: number): string {
  // SephirahType is 1-indexed; floorIndex is 0-indexed.
  return `/assets/stageicons/${floorIdx + 1}.png`;
}

const floors = computed(() => props.state.floors ?? []);

/** Active fashion book for the librarian's current in-game appearance. */
function fashionBookFor(lib: LibrarianEntry): FashionBook | null {
  return resolveFashionBook(lib, lib, props.state.customizeOptions);
}

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
  // Capture the lib before nulling the ref. EditPanel's onBeforeUnmount
  // hook also tries to unlock via onUnlock(), but by the time it runs
  // editingLibrarian is already null and that path early-returns. So we
  // must release the lock explicitly here.
  const lib = editingLibrarian.value;
  editingLibrarian.value = null;
  if (lib) void actions.unlockLibrarian(lib.floorIndex, lib.unitIndex);
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
);

// ── EditPanel action callbacks ─────────────────────────────────────────────

/**
 * Invokes `fn` with the currently editing librarian's floorIndex and
 * unitIndex. No-op when no librarian is open in the panel.
 */
async function forEditing(
  fn: (floorIndex: number, unitIndex: number) => Promise<unknown>,
): Promise<void> {
  const lib = editingLibrarian.value;
  if (!lib) return;
  await fn(lib.floorIndex, lib.unitIndex);
}

/**
 * Like forEditing, but returns ActionResult so the EditPanel can surface
 * errors. Returns a synthetic failure when no librarian is open so the
 * caller's promise contract is preserved.
 */
function forEditingResult(
  fn: (floorIndex: number, unitIndex: number) => Promise<ActionResult>,
): Promise<ActionResult> {
  const lib = editingLibrarian.value;
  if (!lib) return Promise.resolve({ ok: false, error: "No librarian selected" });
  return fn(lib.floorIndex, lib.unitIndex);
}

function onLock(): Promise<ActionResult> {
  return forEditingResult((fi, ui) => actions.lockLibrarian(fi, ui));
}

function onUnlock(): Promise<ActionResult> {
  return forEditingResult((fi, ui) => actions.unlockLibrarian(fi, ui));
}

function onRename(name: string): Promise<ActionResult> {
  return forEditingResult((fi, ui) => actions.renameLibrarian(fi, ui, name));
}

function onEquipPage(kp: AvailableKeyPage): Promise<void> {
  return forEditing((fi, ui) => actions.equipKeyPage(fi, ui, kp.instanceId));
}

function onAddCard(card: AvailableCard): Promise<void> {
  return forEditing((fi, ui) =>
    actions.addCardToDeck(fi, ui, card.cardId.id, card.cardId.packageId),
  );
}

function onRemoveCard(card: DeckCardPreview): Promise<void> {
  // DeckCardPreview.cardId is nullable; skip the action if the preview
  // doesn't carry a concrete card reference.
  if (!card.cardId) return Promise.resolve();
  const cardId = card.cardId;
  return forEditing((fi, ui) =>
    actions.removeCardFromDeck(fi, ui, cardId.id, cardId.packageId),
  );
}

function onEquipSourceBook(bookInstanceId: number): Promise<void> {
  return forEditing((fi, ui) => actions.equipSourceBook(fi, ui, bookInstanceId));
}

function onUnequipSourceBook(bookInstanceId: number): Promise<void> {
  return forEditing((fi, ui) => actions.unequipSourceBook(fi, ui, bookInstanceId));
}

function onAttributePassive(
  sourceInstanceId: number,
  passiveId: number,
  passivePackageId: string,
): Promise<void> {
  return forEditing((fi, ui) =>
    actions.attributePassive(fi, ui, sourceInstanceId, passiveId, passivePackageId),
  );
}

function onRemoveAttributedPassive(
  sourceInstanceId: number,
  passiveId: number,
  passivePackageId: string,
): Promise<void> {
  return forEditing((fi, ui) =>
    actions.removeAttributedPassive(fi, ui, sourceInstanceId, passiveId, passivePackageId),
  );
}

function onSetCustomization(
  payload: Omit<CustomizePayload, "floorIndex" | "unitIndex">,
): Promise<ActionResult> {
  return forEditingResult((fi, ui) =>
    actions.sendAction({ type: "setCustomization", floorIndex: fi, unitIndex: ui, ...payload }),
  );
}

function onSetGifts(slots: SetGiftsPayload): Promise<ActionResult> {
  return forEditingResult((fi, ui) =>
    actions.sendAction({ type: "setGifts", floorIndex: fi, unitIndex: ui, ...slots }),
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

</script>

<template>
  <div class="lib-manager">
    <div class="lib-header">
      <span class="lib-title">Library</span>
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
          class="section-block"
          :class="{ open: emotionCardsOpen }"
        >
          <div
            class="section-toggle"
            role="button"
            tabindex="0"
            aria-label="Toggle Abnormality Pages"
            :aria-expanded="emotionCardsOpen"
            @click="emotionCardsOpen = !emotionCardsOpen"
            @keydown.enter="emotionCardsOpen = !emotionCardsOpen"
            @keydown.space.prevent="emotionCardsOpen = !emotionCardsOpen"
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
        </div>

        <!-- EGO pages section (full battle cards, separate from abnormality pages) -->
        <div
          v-if="activeFloorData.egoCards.length"
          class="section-block"
          :class="{ open: egoCardsOpen }"
        >
          <div
            class="section-toggle"
            role="button"
            tabindex="0"
            aria-label="Toggle EGO Pages"
            :aria-expanded="egoCardsOpen"
            @click="egoCardsOpen = !egoCardsOpen"
            @keydown.enter="egoCardsOpen = !egoCardsOpen"
            @keydown.space.prevent="egoCardsOpen = !egoCardsOpen"
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
                :key="`ego-${i}`"
                :card="egoCardToCard(card, i)"
                readonly
                @detail="detailCard = egoCardToCard(card, i)"
              />
            </div>
          </div>
        </div>

        <!--
          Librarian roster list.

          Rows instead of tiles: the previous grid (auto-fill minmax 280px
          with a 240px portrait) couldn't leave enough horizontal room for
          the text at narrow widths, so librarian names clipped. A
          full-width row puts the portrait on the left and gives the body
          the rest of the row, so the name only clips on genuinely tiny
          viewports.
        -->
        <div class="lib-list">
          <div
            v-for="lib in activeFloorData.librarians"
            :key="`${lib.floorIndex}:${lib.unitIndex}`"
            class="lib-row"
            @click="openEdit(lib)"
          >
            <div v-if="lib.appearance" class="row-portrait">
              <LibrarianAppearancePreview
                :appearance="lib.appearance"
                :fashion-book="fashionBookFor(lib)"
                :appearance-type="lib.appearanceType"
                :gifts="lib.gifts?.equipped"
                :size="120"
              />
            </div>
            <div class="row-body">
              <div class="row-title">
                <span class="row-name" :title="lib.name">{{ lib.name }}</span>
                <span
                  v-if="lib.lockedBy"
                  class="row-lock"
                  :title="`Being edited by ${lib.lockedBy}`"
                >
                  ✎ {{ lib.lockedBy }}
                </span>
              </div>
              <span class="row-page" :title="lib.keyPage.name">{{ lib.keyPage.name }}</span>
            </div>

            <!--
              Stat strip (HP / stagger gauge / speed). Hides below the stat
              breakpoint; resistances hide at a wider breakpoint, so as the
              row narrows the order is: resists drop first, then stats.
            -->
            <div class="row-stats">
              <span v-if="lib.keyPage.hp != null" class="stat">
                <img src="/assets/stats/health.png" class="stat-icon" alt="HP" />
                <span class="stat-value">{{ lib.keyPage.hp }}</span>
              </span>
              <span v-if="lib.keyPage.breakGauge != null" class="stat">
                <img src="/assets/stats/stagger.png" class="stat-icon" alt="Stagger" />
                <span class="stat-value">{{ lib.keyPage.breakGauge }}</span>
              </span>
              <span
                v-if="lib.keyPage.speedMin != null && lib.keyPage.speedMax != null"
                class="stat"
              >
                <img src="/assets/stats/speed.png" class="stat-icon" alt="Speed" />
                <span class="stat-value">
                  {{ lib.keyPage.speedMin }}–{{ lib.keyPage.speedMax }}
                </span>
              </span>
            </div>

            <!--
              Compact resistances strip: 3 damage types × 2 defenses (HP, BP).
              Renders icon + tier symbol (++ / + / · / − / −− / ∅); the full
              player-facing label is on aria-label / title for accessibility.
              resistStyle() applies the in-game-style brightness gradient —
              weaknesses glow, mid tiers dim progressively, Immune is flat grey.
            -->
            <div v-if="lib.keyPage.resistances" class="row-resists">
              <div class="resist-col">
                <span
                  class="resist-cell"
                  :style="resistStyle(lib.keyPage.resistances.slashHp, 'hp')"
                  :aria-label="`Slash damage: ${resistLabel(lib.keyPage.resistances.slashHp)}`"
                  :title="resistLabel(lib.keyPage.resistances.slashHp)"
                >
                  <img src="/assets/stats/sHpResist.png" class="resist-icon" alt="" />
                  <span class="resist-symbol">{{ resistSymbol(lib.keyPage.resistances.slashHp) }}</span>
                </span>
                <span
                  class="resist-cell"
                  :style="resistStyle(lib.keyPage.resistances.slashBp, 'bp')"
                  :aria-label="`Slash stagger: ${resistLabel(lib.keyPage.resistances.slashBp)}`"
                  :title="resistLabel(lib.keyPage.resistances.slashBp)"
                >
                  <img src="/assets/stats/sBpResist.png" class="resist-icon" alt="" />
                  <span class="resist-symbol">{{ resistSymbol(lib.keyPage.resistances.slashBp) }}</span>
                </span>
              </div>
              <div class="resist-col">
                <span
                  class="resist-cell"
                  :style="resistStyle(lib.keyPage.resistances.pierceHp, 'hp')"
                  :aria-label="`Pierce damage: ${resistLabel(lib.keyPage.resistances.pierceHp)}`"
                  :title="resistLabel(lib.keyPage.resistances.pierceHp)"
                >
                  <img src="/assets/stats/pHpResist.png" class="resist-icon" alt="" />
                  <span class="resist-symbol">{{ resistSymbol(lib.keyPage.resistances.pierceHp) }}</span>
                </span>
                <span
                  class="resist-cell"
                  :style="resistStyle(lib.keyPage.resistances.pierceBp, 'bp')"
                  :aria-label="`Pierce stagger: ${resistLabel(lib.keyPage.resistances.pierceBp)}`"
                  :title="resistLabel(lib.keyPage.resistances.pierceBp)"
                >
                  <img src="/assets/stats/pBpResist.png" class="resist-icon" alt="" />
                  <span class="resist-symbol">{{ resistSymbol(lib.keyPage.resistances.pierceBp) }}</span>
                </span>
              </div>
              <div class="resist-col">
                <span
                  class="resist-cell"
                  :style="resistStyle(lib.keyPage.resistances.bluntHp, 'hp')"
                  :aria-label="`Blunt damage: ${resistLabel(lib.keyPage.resistances.bluntHp)}`"
                  :title="resistLabel(lib.keyPage.resistances.bluntHp)"
                >
                  <img src="/assets/stats/bHpResist.png" class="resist-icon" alt="" />
                  <span class="resist-symbol">{{ resistSymbol(lib.keyPage.resistances.bluntHp) }}</span>
                </span>
                <span
                  class="resist-cell"
                  :style="resistStyle(lib.keyPage.resistances.bluntBp, 'bp')"
                  :aria-label="`Blunt stagger: ${resistLabel(lib.keyPage.resistances.bluntBp)}`"
                  :title="resistLabel(lib.keyPage.resistances.bluntBp)"
                >
                  <img src="/assets/stats/bBpResist.png" class="resist-icon" alt="" />
                  <span class="resist-symbol">{{ resistSymbol(lib.keyPage.resistances.bluntBp) }}</span>
                </span>
              </div>
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
      :on-set-customization="onSetCustomization"
      :on-set-gifts="onSetGifts"
      :on-equip-source-book="onEquipSourceBook"
      :on-unequip-source-book="onUnequipSourceBook"
      :on-attribute-passive="onAttributePassive"
      :on-remove-attributed-passive="onRemoveAttributedPassive"
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
  gap: var(--sp-3);
  padding: var(--sp-2) 0;
}

.lib-header {
  display: flex;
  align-items: baseline;
  gap: var(--sp-3);
}

.lib-title {
  font-family: var(--font-display);
  font-size: var(--fs-2xl);
  font-weight: 700;
  color: var(--gold);
  letter-spacing: 0.18em;
  text-transform: uppercase;
  text-shadow: 0 0 24px var(--gold-glow);
}

@media (min-width: 700px) {
  .lib-title {
    font-size: var(--fs-3xl);
    letter-spacing: 0.22em;
  }
}

.lib-empty {
  color: var(--text-2);
  font-size: var(--fs-sm);
  padding: var(--sp-5) 0;
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
  gap: var(--sp-2);
}

.floor-tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--sp-1);
  padding: var(--sp-2) var(--sp-3);
  background: var(--bg-card);
  border: 1px solid var(--border);
  margin-top: 1px;
  border-radius: var(--radius-sm);
  cursor: pointer;
  transition:
    border-top-color 0.15s,
    background 0.12s,
    opacity 0.15s;
  min-width: 4.5rem;
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
  width: 22px;
  height: 22px;
  object-fit: contain;
  opacity: 0.7;
  transition: opacity 0.15s;
}

@media (min-width: 700px) {
  .tile-icon {
    width: 28px;
    height: 28px;
  }
}

.floor-tile.active .tile-icon,
.floor-tile:hover .tile-icon {
  opacity: 1;
}

.tile-name {
  font-family: var(--font-display);
  font-size: var(--fs-sm);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-2);
  white-space: nowrap;
}

@media (min-width: 700px) {
  .tile-name {
    font-size: var(--fs-md);
  }
}

.floor-tile.active .tile-name {
  color: var(--floor-color);
}

.tile-level {
  font-family: var(--font-display);
  font-size: var(--fs-xs);
  color: var(--text-3);
  letter-spacing: 0.05em;
}

.floor-tile.active .tile-level {
  color: var(--floor-color);
  opacity: 0.85;
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
  align-items: baseline;
  gap: var(--sp-3);
  padding: var(--sp-2) var(--sp-3);
  border-left: 3px solid var(--border);
  background: var(--bg-card);
  border-radius: 0 var(--radius-md) var(--radius-md) 0;
}

.banner-name {
  font-family: var(--font-display);
  font-size: var(--fs-xl);
  font-weight: 700;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--text-1);
}

.banner-level {
  font-family: var(--font-display);
  font-size: var(--fs-md);
  color: var(--text-2);
  letter-spacing: 0.1em;
}

/* ── Abnormality / EGO collapsible sections ────────────────────────────────── */
/*
 * .section-block wraps the toggle banner and its expanded content so they
 * render as one continuous block (shared border + background). The toggle
 * gets a hairline divider above the content only when .open is applied.
 */
.section-block {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  overflow: hidden;
  transition: border-color var(--duration-base) var(--ease-out);
}

.section-block:hover {
  border-color: var(--border-mid);
}

.section-block.open {
  border-color: var(--border-mid);
}

.section-toggle {
  display: flex;
  align-items: center;
  gap: var(--sp-3);
  padding: var(--sp-2) var(--sp-3);
  cursor: pointer;
  user-select: none;
  background: transparent;
  border: none;
  border-radius: 0;
  transition: background var(--duration-fast) var(--ease-out);
}

.section-toggle:hover {
  background: var(--bg-card-2);
}

.section-block.open .section-toggle {
  border-bottom: 1px solid var(--border);
}

.section-toggle-label {
  font-family: var(--font-display);
  font-size: var(--fs-lg);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.14em;
  color: var(--text-1);
}

.section-toggle-count {
  font-family: var(--font-body);
  font-size: var(--fs-sm);
  color: var(--gold);
  background: var(--bg-card-2);
  border: 1px solid var(--border-mid);
  border-radius: var(--radius-sm);
  padding: 0 var(--sp-2);
  line-height: 1.6;
}

.chevron {
  font-size: var(--fs-md);
  color: var(--text-2);
  display: inline-block;
  transition: transform var(--duration-base) var(--ease-out);
  margin-left: auto;
}

.chevron.open {
  transform: rotate(90deg);
  color: var(--gold);
}

.emotion-groups {
  display: flex;
  flex-direction: column;
  gap: var(--sp-3);
  padding: var(--sp-3) var(--sp-3);
  background: var(--bg-card-2);
  border: none;
  border-radius: 0;
}

.emotion-group-label {
  font-family: var(--font-display);
  font-size: var(--fs-xs);
  text-transform: uppercase;
  letter-spacing: 0.14em;
  color: var(--text-2);
  margin-bottom: var(--sp-1);
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

/* ── Librarian roster list ─────────────────────────────────────────────────── */
/*
 * Full-width rows. The entire row is clickable (opens EditPanel), and the
 * explicit Edit button on the right stays as a keyboard/affordance anchor
 * with @click.stop so it doesn't fire openEdit twice.
 */
.lib-list {
  display: flex;
  flex-direction: column;
  gap: var(--sp-2);
  margin-top: var(--sp-2);
}

.lib-row {
  display: flex;
  align-items: center;
  gap: var(--sp-3);
  padding: var(--sp-2) var(--sp-3);
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-left: 3px solid var(--gold-dim);
  border-radius: var(--radius-md);
  cursor: pointer;
  transition: background var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out);
}

.lib-row:hover {
  background: var(--bg-card-2);
  border-color: var(--gold-dim);
  border-left-color: var(--gold-bright);
}

@media (min-width: 700px) {
  .lib-row {
    padding: var(--sp-3) var(--sp-4);
    gap: var(--sp-4);
  }
}

/*
 * Portrait column. Fixed width matching the AppearancePreview `size` prop
 * so the row reserves space even before the composite loads.
 */
.row-portrait {
  flex: 0 0 120px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.row-body {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  gap: var(--sp-1);
  min-width: 0;
}

/*
 * Top line of the row body: librarian name with the lock chip inline.
 * Both shrink (flex: 0 1 auto) so a long session name can't push the
 * name out of the row — each truncates with ellipsis instead.
 */
.row-title {
  display: flex;
  align-items: baseline;
  gap: var(--sp-2);
  min-width: 0;
}

.row-name {
  flex: 0 1 auto;
  font-family: var(--font-display);
  font-size: var(--fs-lg);
  font-weight: 700;
  color: var(--text-1);
  letter-spacing: 0.04em;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

@media (min-width: 700px) {
  .row-name {
    font-size: var(--fs-xl);
  }
}

.row-page {
  font-size: var(--fs-sm);
  color: var(--text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
}

/*
 * Stat strip: HP / stagger gauge / speed, each icon paired with its
 * numeric value. Icons match the assets KeyPageDetail uses so the roster
 * reads consistently with the detail panel. Hidden at narrow widths along
 * with the resistance grid — compact viewports show just name + lock.
 */
.row-stats {
  flex: 0 0 auto;
  display: none;
  align-items: center;
  gap: var(--sp-3);
}

@media (min-width: 700px) {
  .row-stats {
    display: flex;
  }
}

.stat {
  display: inline-flex;
  align-items: center;
  gap: var(--sp-1);
  font-family: var(--font-display);
}

.stat-icon {
  width: 1.1rem;
  height: 1.1rem;
  object-fit: contain;
  opacity: 0.9;
}

.stat-value {
  font-size: var(--fs-md);
  color: var(--text-1);
}

/*
 * Compact resistances column. Three sub-columns (slash / pierce / blunt),
 * each with HP on top and BP on the bottom. Cells get their colour and
 * brightness from resistStyle() so the in-game gradient (Fatal glows,
 * Immune is flat grey) is immediately legible at a glance.
 *
 * Resists are the lowest-priority row element, so they hide at a wider
 * breakpoint than the stat strip — when space gets tight the resists
 * drop first, then the stats, leaving name + lock + key page intact.
 */
.row-resists {
  flex: 0 0 auto;
  display: none;
  gap: var(--sp-2);
  align-items: center;
  padding: var(--sp-1) var(--sp-2);
  border-left: 1px solid var(--border);
}

@media (min-width: 900px) {
  .row-resists {
    display: flex;
  }
}

.resist-col {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.resist-cell {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  font-family: var(--font-body);
  font-weight: 700;
  line-height: 1.1;
}

.resist-icon {
  width: 1.1rem;
  height: 1.1rem;
  object-fit: contain;
  opacity: 0.9;
}

/*
 * Fixed-width tier symbol slot. min-width keeps the column consistent
 * across rows whether a 1-char (`+`, `−`, `·`, `∅`) or 2-char (`++`, `−−`)
 * symbol is rendered. Left-aligned so the lead character lines up vertically
 * across stacked cells regardless of symbol width.
 */
.resist-symbol {
  display: inline-block;
  min-width: 1.2rem;
  text-align: left;
  font-size: var(--fs-xs);
  line-height: 1;
}

/*
 * Lock indicator. Inline with the librarian name (after it) so the
 * "being edited by …" status reads as part of the title. flex: 0 1 auto
 * lets a long session name truncate via ellipsis rather than push the
 * librarian name out of the row.
 */
.row-lock {
  flex: 0 1 auto;
  min-width: 0;
  font-size: var(--fs-xs);
  color: var(--gold);
  letter-spacing: 0.04em;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
