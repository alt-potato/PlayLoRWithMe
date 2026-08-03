<!--
  DeckTab.vue

  Deck editor inside the librarian EditPanel.
  Left column: equipped deck cards (tap to remove).
  Right column: available card inventory with CardFilter above (tap to add).

  For multi-deck key pages (e.g. The Purple Tear) a tab strip above the deck
  column exposes all four deck slots with stance labels resolved client-side.
  Pending edits and cap math are tracked per (deckIndex, cardId+packageId).

  Props:
    lib              – librarian being edited
    state            – full game state (provides availableCards)
    editBusy         – true while an async action is in-flight
    onAddCard        – callback to add a card to the active deck slot
    onRemoveCard     – callback to remove one copy from the active deck slot
-->
<script setup lang="ts">
import type { LibrarianEntry, GameState, AvailableCard, DeckCardPreview, Card, ActionResult } from "~/types/game";
import { STATE_GENERATION } from "~/composables/useStateGeneration";
import { resolveDeckLabels } from "~/utils/multiDeckLabels";
import { DECK_MAX, pendingKey, dropOldest } from "~/composables/useDeckEditStaging";
import type { PendingDeckEdit } from "~/composables/useDeckEditStaging";

const props = defineProps<{
  lib: LibrarianEntry;
  state: GameState;
  editBusy: boolean;
  onAddCard: (card: AvailableCard, deckIndex: number) => Promise<ActionResult>;
  onRemoveCard: (card: DeckCardPreview, deckIndex: number) => Promise<ActionResult>;
}>();

/** True when the equipped key page exposes more than one deck slot. */
const isMultiDeck = computed(() => props.lib.keyPage.isMultiDeck === true);

// The pending-edit FIFO queues, the decks-diff reconciliation watcher, the
// active-tab clamp, the connection-reset cleanup, and the cap/limit math all
// live in the composable; this component only supplies server truth and the
// state-generation signal, and keeps presentation concerns (card view-model
// conversion, filtering, deck labels) local.
const stateGeneration = inject(STATE_GENERATION, ref(0));
const staging = useDeckEditStaging({
  lib: computed(() => props.lib),
  stateGeneration,
});
const {
  activeDeckIndex,
  decks,
  deckCardCounts,
  pendingAdds,
  pendingRemoves,
  pendingAddsForActive,
  pendingRemoveCounts,
  expandedDeck,
  renderedDeck,
  effectiveDeckCount,
  emptySlotCount,
  isAtLimit,
} = staging;

/**
 * Fallback stance/deck labels, indexed by deck index 0..3. Resolved
 * client-side via the small `multiDeckLabels` table when the wire payload
 * omits a per-deck `label` (the mod resolves that through
 * BattleEffectTextsXmlList for known books, so it carries the player's
 * game-language strings; unknown books fall back here).
 */
const fallbackDeckLabels = computed(() =>
  resolveDeckLabels(props.lib.keyPage.bookPackageId, props.lib.keyPage.bookId),
);

/** Label for a given deck index — prefers the wire label, then the fallback. */
function deckLabelFor(deckIndex: number): string {
  const fromWire = decks.value.find((d) => d.index === deckIndex)?.label;
  if (fromWire) return fromWire;
  return fallbackDeckLabels.value[deckIndex] ?? `Deck ${deckIndex + 1}`;
}

/**
 * Cards available to add to this librarian's deck.
 * Page-exclusive (OnlyPage) cards for this key page are prepended so they
 * appear first regardless of cost. The rest are range-filtered per BookXmlInfo.RangeType:
 * Melee blocks Far; Range blocks Near; Hybrid allows all.
 */
const allAvailableCards = computed(() => {
  const onlyCards = props.lib.onlyCards ?? [];
  const cards = props.state.availableCards ?? [];
  const rangeType = props.lib.keyPage.equipRangeType;
  let filtered: typeof cards;
  // Filter by equip range: Melee pages can't use Far cards; Range pages can't use Near cards.
  if (rangeType === "Melee")
    filtered = cards.filter((c) => c.range !== "Far");
  else if (rangeType === "Range")
    filtered = cards.filter((c) => c.range !== "Near");
  else
    filtered = cards;
  return [...onlyCards, ...filtered];
});
const filteredCards = ref<AvailableCard[]>([]);

const detailCard = ref<Card | null>(null);

function availableToCard(c: AvailableCard, i: number): Card {
  return {
    id: { id: c.cardId.id, packageId: Number(c.cardId.packageId) || 0 },
    index: i,
    name: c.name,
    cost: c.cost,
    range: c.range,
    rarity: c.rarity,
    dice: c.dice,
    abilityDesc: c.abilityDesc,
    rarityColor: c.rarityColor,
    rarityRangeIconColor: c.rarityRangeIconColor,
    rarityAbilityColor: c.rarityAbilityColor,
    rarityKeywordColor: c.rarityKeywordColor,
  };
}

// Pre-build the Card view-model for each filtered tile once per filter change.
// The grid template was previously calling availableToCard(card, i) twice per
// row (`:card` and `@detail`), allocating two fresh objects per render and
// defeating any memoization on `HandCard`. The aligned arrays let the template
// hand the same object reference to both bindings.
const filteredAsCards = computed(() => filteredCards.value.map(availableToCard));

/**
 * Optimistic add: pushes a pending-add entry before awaiting the server
 * response so the deck-editor reflects the change in the same render
 * cycle as the tap. The pending entry carries the active deck index so
 * the diff watcher only clears it when that slot's count increments.
 */
async function handleAddCard(card: AvailableCard) {
  const deckIndex = activeDeckIndex.value;
  const entry: PendingDeckEdit = {
    deckIndex,
    cardId: card.cardId.id,
    packageId: card.cardId.packageId,
    card: availableToCard(card, pendingAdds.value.length),
    addedAt: Date.now(),
  };
  pendingAdds.value.push(entry);
  const result = await props.onAddCard(card, deckIndex);
  if (!result.ok) {
    dropOldest(pendingAdds.value, pendingKey(deckIndex, entry.cardId, entry.packageId));
  }
}

// Pair each rendered deck preview with its Card view-model, built once per
// recompute so the template hands the same object to :card and @detail instead
// of calling previewToCard(preview, i) twice per tile (two allocations per
// render). Mirrors the filteredAsCards pattern used by the inventory grid.
const renderedDeckTiles = computed(() =>
  renderedDeck.value.map((preview, i) => ({
    preview,
    card: previewToCard(preview, i),
  })),
);

/**
 * Optimistic remove: dims the tile in place and dispatches the action.
 * Short-circuits when every confirmed copy of this card on the active
 * deck slot is already pending-remove, preventing duplicate requests on
 * rapid multi-tap.
 */
async function handleRemoveCard(preview: DeckCardPreview) {
  if (!preview.cardId) return;
  const deckIndex = activeDeckIndex.value;
  const cardKey = `${preview.cardId.id}_${preview.cardId.packageId}`;
  const fullKey = pendingKey(deckIndex, preview.cardId.id, preview.cardId.packageId);
  const confirmed = deckCardCounts.value.get(fullKey) ?? 0;
  const alreadyPending = pendingRemoveCounts.value.get(cardKey) ?? 0;
  if (alreadyPending >= confirmed) return;

  const entry: PendingDeckEdit = {
    deckIndex,
    cardId: preview.cardId.id,
    packageId: preview.cardId.packageId,
    card: previewToCard(preview, pendingRemoves.value.length),
    addedAt: Date.now(),
  };
  pendingRemoves.value.push(entry);
  const result = await props.onRemoveCard(preview, deckIndex);
  if (!result.ok) {
    dropOldest(pendingRemoves.value, fullKey);
  }
}
</script>

<template>
  <div class="deck-tab">
    <!-- Left: available cards — filter + click to add one copy -->
    <div class="deck-col deck-col--available">
      <div class="col-header">Add Cards</div>
      <LibrarianCardFilter :cards="allAvailableCards" @filtered="filteredCards = $event" />
      <div v-if="!filteredCards.length" class="col-empty">No cards match.</div>
      <div v-else class="card-grid">
        <HandCard
          v-for="(card, i) in filteredCards"
          :key="card.cardId.id + '_' + card.cardId.packageId"
          v-memo="[
            card.cardId.id,
            card.cardId.packageId,
            card.count,
            isAtLimit(card),
            editBusy,
            effectiveDeckCount >= DECK_MAX,
          ]"
          :card="filteredAsCards[i]!"
          :count="card.count"
          :unusable="
            editBusy ||
            card.count <= 0 ||
            isAtLimit(card) ||
            effectiveDeckCount >= DECK_MAX
          "
          @click="handleAddCard(card)"
          @detail="detailCard = filteredAsCards[i]!"
        />
      </div>
    </div>

    <!-- Right: equipped deck — click a card to remove one copy. Empty slots
         are surfaced as placeholders to communicate the 9-card cap and the
         fact that the engine will auto-fill them with default cards. -->
    <div class="deck-col deck-col--equipped">
      <div class="col-header">
        Deck
        <span class="deck-count">{{ effectiveDeckCount }} / {{ DECK_MAX }}</span>
      </div>
      <LibrarianKeyPageDetail class="deck-keypage" :key-page="lib.keyPage" :compact="true" />
      <!-- Multi-deck tab strip — only rendered for key pages with the
           BookOption.MultiDeck flag (e.g. The Purple Tear). Single-deck books
           (the 99% case) hide this row entirely so the editor visually matches
           its pre-multi-deck shape. The active tab's count is already shown
           in the column header's deck-count badge above. -->
      <div v-if="isMultiDeck" class="deck-tabs" role="tablist" aria-label="Deck slot">
        <button
          v-for="d in decks"
          :key="`deck-tab-${d.index}`"
          class="deck-tab-btn"
          :class="{ active: d.index === activeDeckIndex }"
          role="tab"
          :aria-selected="d.index === activeDeckIndex"
          @click="activeDeckIndex = d.index"
        >
          {{ deckLabelFor(d.index) }}
        </button>
      </div>
      <div class="card-grid">
        <HandCard
          v-for="(tile, i) in renderedDeckTiles"
          :key="`copy-${i}`"
          :card="tile.card"
          :unusable="editBusy || !tile.preview.cardId"
          @click="handleRemoveCard(tile.preview)"
          @detail="detailCard = tile.card"
        />
        <!-- pending-add tiles for the active tab only render after the
             confirmed deck so the user sees the new card "land" at the end
             of the deck while waiting for the server's delta on this slot. -->
        <div
          v-for="(p, i) in pendingAddsForActive"
          :key="`pending-add-${p.deckIndex}-${i}-${p.addedAt}`"
          class="pending-tile"
        >
          <HandCard
            :card="p.card"
            :readonly="true"
            @detail="detailCard = p.card"
          />
          <span class="pending-spinner" aria-label="Adding card" />
        </div>
        <div
          v-for="i in emptySlotCount"
          :key="`placeholder-${i}`"
          class="deck-placeholder"
          :title="`Empty slot ${expandedDeck.length + i} — auto-filled with a default card before combat.`"
        ></div>
      </div>
    </div>

    <CardDetail v-if="detailCard" :card="detailCard" @close="detailCard = null" />
  </div>
</template>

<style scoped>
.deck-tab {
  display: flex;
  flex-direction: column;
  gap: var(--sp-3);
  height: 100%;
  overflow: hidden;
  min-height: 0;
}

.deck-col {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  gap: var(--sp-2);
  flex: 1;
  min-height: 0;
}

/* base uppercase heading styling is shared via app.vue's global .col-header;
   this tab additionally lays it out as a flex row so the "N / MAX" count
   badge (.deck-count below) sits inline next to the heading text. */
.col-header {
  display: flex;
  align-items: baseline;
  gap: var(--sp-2);
}

/* Compact "N / MAX" indicator next to the Deck header. Muted so the
   header text remains the focal element and the count reads as metadata. */
.deck-count {
  font-family: var(--font-body);
  font-size: var(--fs-xs);
  letter-spacing: 0;
  text-transform: none;
  color: var(--text-3);
}


.card-grid {
  /* explicit shorthand: horizontal is clipped (no scrollbar even when a
     hovered card's absolute-positioned detail overlay extends past the
     grid); vertical scrolls as before. without explicit overflow-x the
     browser promotes it to `auto` alongside overflow-y, producing a
     horizontal scrollbar whenever the overlay pokes out. */
  overflow: hidden auto;
  scrollbar-gutter: stable;
  display: flex;
  flex-wrap: wrap;
  gap: var(--sp-2);
  align-content: flex-start;
  flex: 1;
  min-height: 0;
}

.deck-keypage {
  flex-shrink: 0;
  padding-top: 0;
  padding-bottom: var(--sp-1);
}

/*
 * Multi-deck tab strip — matches the EditPanel's `.tab-bar` underline
 * pattern (transparent background, gold underline when active) so the
 * deck selector reads as navigation rather than a control. Tabs wrap on
 * narrow viewports because Purple Tear's four stance labels can blow past
 * the equipped column width at mobile sizes.
 */
.deck-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 0;
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

.deck-tab-btn {
  padding: var(--sp-2) var(--sp-3);
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
  transition:
    color var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out);
}

.deck-tab-btn:hover {
  color: var(--text-1);
}

.deck-tab-btn.active {
  color: var(--gold-bright);
  border-bottom-color: var(--gold-bright);
}

/*
 * Pending-add tile wrapper. The inner HandCard renders normally; the
 * wrapper provides reduced opacity and a corner spinner so the user
 * sees the card while it's being committed. `pointer-events: none` on
 * the spinner keeps long-press detail open (HandCard handles its own
 * touch events through its root div).
 */
.pending-tile {
  position: relative;
  opacity: 0.5;
  flex-shrink: 0;
}

.pending-spinner {
  position: absolute;
  top: 0.2rem;
  right: 0.2rem;
  width: 0.7rem;
  height: 0.7rem;
  border: 2px solid var(--gold-bright);
  border-top-color: transparent;
  border-radius: 50%;
  animation: pending-spin 0.7s linear infinite;
  pointer-events: none;
}

@keyframes pending-spin {
  to { transform: rotate(360deg); }
}

/*
 * Empty deck slot tile. Shape and width match HandCard's preview pane
 * (5.5rem wide, 5:7 aspect ratio) so equipped cards and placeholders
 * line up on a shared baseline grid. Dashed border + muted fill mark
 * the tile as a non-card slot rather than an unusable card.
 */
.deck-placeholder {
  flex-shrink: 0;
  width: 5.5rem;
  aspect-ratio: 5 / 7;
  border: 1px dashed var(--border-mid);
  background: var(--bg-card-2);
  opacity: 0.55;
  cursor: default;
  user-select: none;
  /* HandCard's border wraps the 5.5rem preview from outside, so the visible
     card occupies 5.5rem + 2px. Opting into content-box makes the dashed
     border sit outside the 5.5rem box too, matching the card footprint
     instead of shrinking 2px under the global border-box default. */
  box-sizing: content-box;
}

/*
 * Side-by-side at >=700px. Layout mirrors KeyPageTab: browse on the left
 * (filter + many tiles), details on the right (equipped deck — capped at
 * 9 cards so it only needs a narrow strip). Hairline divider between.
 */
@media (min-width: 700px) {
  .deck-tab {
    flex-direction: row;
    gap: var(--sp-3);
  }

  .deck-col--available {
    flex: 1;
  }

  .deck-col--equipped {
    flex: 0 0 35%;
    border-left: 1px solid var(--border);
    padding-left: var(--sp-3);
  }
}

/* Roomier breathing space at the wide desktop breakpoint. */
@media (min-width: 1200px) {
  .deck-tab {
    gap: var(--sp-3);
    padding: var(--sp-4);
  }

  .deck-col--equipped {
    flex: 0 0 30%;
    padding-left: var(--sp-4);
  }
}
</style>
