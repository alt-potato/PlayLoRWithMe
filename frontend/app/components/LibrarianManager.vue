<!--
  LibrarianManager.vue

  Read-only roster of all library floor librarians, shown in the main scene
  (any UIPhase except BattleSetting). Each card displays name, key page,
  passive count, and deck size. Tapping a card expands an inline detail panel
  with the resistance table, full passive list, and deck card preview.

  Props:
    state       – full game state (scene = 'main', librarians array present)
    session     – current session identity
    players     – connected player list
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
}>();

/** Human-readable floor names, indexed by floorIndex (matches SephirahType enum order). */
const FLOOR_NAMES = [
  "Malkuth",
  "Yesod",
  "Hod",
  "Netzach",
  "Tiphereth",
  "Gebura",
  "Chesed",
  "Binah",
  "Hokma",
  "Keter",
] as const;

const librarians = computed(() => props.state.librarians ?? []);

/** Group librarians by floorIndex, preserving insertion order. */
const floors = computed(() => {
  const map = new Map<number, LibrarianEntry[]>();
  for (const lib of librarians.value) {
    if (!map.has(lib.floorIndex)) map.set(lib.floorIndex, []);
    map.get(lib.floorIndex)!.push(lib);
  }
  return [...map.entries()].map(([idx, units]) => ({
    idx,
    name: FLOOR_NAMES[idx] ?? `Floor ${idx}`,
    units,
  }));
});

/** Key of the currently expanded librarian card, or null. */
const expandedKey = ref<string | null>(null);

function cardKey(lib: LibrarianEntry): string {
  return `${lib.floorIndex}:${lib.unitIndex}`;
}

function toggleExpand(lib: LibrarianEntry): void {
  const key = cardKey(lib);
  expandedKey.value = expandedKey.value === key ? null : key;
  detailCard.value = null;
}

/** Card currently shown in the full CardDetail overlay. */
const detailCard = ref<Card | null>(null);

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
      <span class="lib-sub">{{ librarians.length }} librarian{{
        librarians.length === 1 ? "" : "s"
      }}</span>
    </div>

    <div v-if="librarians.length === 0" class="lib-empty">
      No librarian data available.
    </div>

    <div v-else class="floors">
      <section v-for="floor in floors" :key="floor.idx" class="floor">
        <div class="floor-label">
          <span class="floor-name">{{ floor.name }}</span>
        </div>

        <div class="units">
          <div
            v-for="lib in floor.units"
            :key="cardKey(lib)"
            class="lib-card"
            :class="{ 'lib-card--expanded': expandedKey === cardKey(lib) }"
            @click="toggleExpand(lib)"
          >
            <!-- Card header row -->
            <div class="card-header">
              <div class="card-main">
                <span class="card-name">{{ lib.name }}</span>
                <span class="card-page">{{ lib.keyPage.name }}</span>
              </div>
              <div class="card-meta">
                <span class="meta-chip">
                  {{ lib.keyPage.speedMin }}–{{ lib.keyPage.speedMax }}
                </span>
                <span v-if="lib.passives.length" class="meta-chip meta-chip--passive">
                  {{ lib.passives.length }} passive{{ lib.passives.length === 1 ? "" : "s" }}
                </span>
                <span v-if="lib.deckPreview.length" class="meta-chip meta-chip--deck">
                  {{ lib.deckPreview.reduce((s, c) => s + c.count, 0) }} cards
                </span>
                <span
                  v-if="lib.lockedBy"
                  class="meta-chip meta-chip--locked"
                  title="Being edited"
                >
                  ✎
                </span>
                <span class="chevron" :class="{ open: expandedKey === cardKey(lib) }">
                  ▸
                </span>
              </div>
            </div>

            <!-- Expandable detail -->
            <div
              v-if="expandedKey === cardKey(lib)"
              class="card-detail"
              @click.stop
            >
              <!-- Speed range + resistance table -->
              <div class="section-label">Resistances</div>
              <UnitResistanceTable
                v-if="lib.keyPage.resistances"
                :resistances="lib.keyPage.resistances"
              />

              <!-- Passives -->
              <template v-if="lib.passives.length">
                <div class="section-label">Passives</div>
                <UnitPassiveList :passives="lib.passives" />
              </template>

              <!-- Deck card preview -->
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
            </div>
          </div>
        </div>
      </section>
    </div>

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
  gap: 1rem;
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

/* ── Floor sections ────────────────────────────────────────────────────────── */
.floors {
  display: flex;
  flex-direction: column;
  gap: 1.2rem;
}

.floor {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.floor-label {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding-bottom: 0.2rem;
  border-bottom: 1px solid var(--border);
}

.floor-name {
  font-family: var(--font-display);
  font-size: 0.62rem;
  text-transform: uppercase;
  letter-spacing: 0.14em;
  color: var(--gold-dim);
}

/* ── Unit grid ─────────────────────────────────────────────────────────────── */
.units {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
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

.chevron {
  font-size: 0.65rem;
  color: var(--text-3);
  display: inline-block;
  transition: transform 0.18s ease;
  margin-left: 0.1rem;
}

.chevron.open {
  transform: rotate(90deg);
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
</style>
