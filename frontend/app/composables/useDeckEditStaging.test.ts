/**
 * Tests for the optimistic deck-edit reconciliation composable behind the
 * librarian DeckTab.
 *
 * The composable is deliberately I/O-free: it takes server truth (the
 * librarian ref and a connection-generation counter) as refs, so the whole
 * FIFO reconciliation state machine runs under the default `node` vitest
 * environment with no component mount, no WebSocket, and no DOM. Every
 * behaviour the deck editor's cap math and optimistic tiles depend on —
 * FIFO ordering, per-deck-slot isolation, the active-tab clamp, and the
 * connection-reset cleanup — is exercised here because none of it is
 * visible to a type check.
 */

import { describe, it, expect } from "vitest";
import { nextTick, ref } from "vue";
import type { Ref } from "vue";
import type { AvailableCard, Card, DeckPreview, LibrarianEntry } from "~/types/game";

import { useDeckEditStaging, cardLimit, DECK_MAX } from "./useDeckEditStaging";
import type { PendingDeckEdit } from "./useDeckEditStaging";

// ---------------------------------------------------------------------------
// Fixture builders — only the fields the staging machine reads are
// meaningful; the rest carry representative defaults so the domain types are
// satisfied without burying the assertions in noise.
// ---------------------------------------------------------------------------

function librarian(overrides: Partial<LibrarianEntry> = {}): LibrarianEntry {
  return {
    floorIndex: 0,
    unitIndex: 0,
    name: "Tester",
    keyPage: { name: "Primary" },
    baseKeyPage: { name: "Primary" },
    passives: [],
    decks: [{ index: 0, cards: [] }],
    lockedBy: null,
    ...overrides,
  };
}

/** A confirmed deck-slot card entry with `count` copies of (id, packageId). */
function deckCard(
  id: number,
  packageId: string,
  count: number,
  rarity = "Common",
): DeckPreview["cards"][number] {
  return {
    cardId: { id, packageId },
    name: `Card ${id}`,
    cost: 1,
    range: "Near",
    rarity,
    count,
  };
}

function card(id: number, i: number): Card {
  return {
    id: { id, packageId: 0 },
    index: i,
    name: `Card ${id}`,
    cost: 1,
    range: "Near",
    rarity: "Common",
  };
}

function availableCard(
  id: number,
  packageId: string,
  overrides: Partial<AvailableCard> = {},
): AvailableCard {
  return {
    cardId: { id, packageId },
    name: `Card ${id}`,
    cost: 1,
    range: "Near",
    rarity: "Common",
    count: 1,
    ...overrides,
  };
}

function pendingEntry(
  deckIndex: number,
  cardId: number,
  packageId: string,
  addedAt: number,
): PendingDeckEdit {
  return { deckIndex, cardId, packageId, card: card(cardId, addedAt), addedAt };
}

function setup(lib: LibrarianEntry): {
  staging: ReturnType<typeof useDeckEditStaging>;
  libRef: Ref<LibrarianEntry>;
  generationRef: Ref<number>;
} {
  const libRef = ref(lib) as Ref<LibrarianEntry>;
  const generationRef = ref(0);
  const staging = useDeckEditStaging({ lib: libRef, stateGeneration: generationRef });
  return { staging, libRef, generationRef };
}

// ---------------------------------------------------------------------------

describe("useDeckEditStaging — FIFO reconciliation", () => {
  it("clears the oldest pending add first when two are queued for the same slot", async () => {
    const { staging, libRef } = setup(librarian({ decks: [{ index: 0, cards: [] }] }));

    const first = pendingEntry(0, 1, "", 1);
    const second = pendingEntry(0, 1, "", 2);
    staging.pendingAdds.value.push(first, second);

    // one copy confirmed server-side — should drop the oldest (first) entry only
    libRef.value = librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 1)] }] });
    await nextTick();

    expect(staging.pendingAdds.value).toEqual([second]);

    // second copy confirmed — the remaining pending add clears too
    libRef.value = librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 2)] }] });
    await nextTick();

    expect(staging.pendingAdds.value).toEqual([]);
  });

  it("resolves a pending add and a pending remove of the same card independently", async () => {
    const { staging, libRef } = setup(
      librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 1)] }] }),
    );

    staging.pendingAdds.value.push(pendingEntry(0, 1, "", 1));
    staging.pendingRemoves.value.push(pendingEntry(0, 1, "", 2));

    // confirmed count rises 1 -> 2: only the pending add should clear
    libRef.value = librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 2)] }] });
    await nextTick();

    expect(staging.pendingAdds.value).toEqual([]);
    expect(staging.pendingRemoves.value).toHaveLength(1);

    // confirmed count falls 2 -> 1: the pending remove clears
    libRef.value = librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 1)] }] });
    await nextTick();

    expect(staging.pendingRemoves.value).toEqual([]);
  });

  it("does not cross-clear the same card pending on two different deck slots", async () => {
    const { staging, libRef } = setup(
      librarian({
        decks: [
          { index: 0, cards: [] },
          { index: 1, cards: [] },
        ],
      }),
    );

    const onSlot0 = pendingEntry(0, 1, "", 1);
    const onSlot1 = pendingEntry(1, 1, "", 2);
    staging.pendingAdds.value.push(onSlot0, onSlot1);

    // only deck 0 gets a confirmed copy
    libRef.value = librarian({
      decks: [
        { index: 0, cards: [deckCard(1, "", 1)] },
        { index: 1, cards: [] },
      ],
    });
    await nextTick();

    expect(staging.pendingAdds.value).toEqual([onSlot1]);
  });

  it("leaves pending edits untouched by a delta that doesn't touch their key", async () => {
    const { staging, libRef } = setup(
      librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 1)] }] }),
    );

    const pending = pendingEntry(0, 1, "", 1);
    staging.pendingAdds.value.push(pending);

    // a confirmed copy of an unrelated card (id 2) lands on the same slot
    libRef.value = librarian({
      decks: [{ index: 0, cards: [deckCard(1, "", 1), deckCard(2, "", 1)] }],
    });
    await nextTick();

    expect(staging.pendingAdds.value).toEqual([pending]);
  });

  it("stays in sync across an identity-only decks replacement with unchanged content", async () => {
    // Mirrors what a mid-session resync produces: `gameState` is reassigned
    // wholesale from freshly-parsed wire data (see useWebSocket.ts), so
    // `lib.decks` always gets a brand-new array reference even when nothing
    // about this librarian's deck actually changed. The reconciliation
    // watcher is a non-deep `watch` on that reference, so it fires on the
    // reference change alone — this proves prevDeckCounts stays accurate
    // (and pending edits stay untouched) rather than going stale.
    const { staging, libRef } = setup(
      librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 1)] }] }),
    );

    const pending = pendingEntry(0, 1, "", 1);
    staging.pendingAdds.value.push(pending);

    // fresh object graph, byte-for-byte identical deck content
    libRef.value = librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 1)] }] });
    await nextTick();

    expect(staging.pendingAdds.value).toEqual([pending]);

    // the next genuine delta still reconciles correctly, proving the
    // no-op replacement above didn't leave prevDeckCounts stale
    libRef.value = librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 2)] }] });
    await nextTick();

    expect(staging.pendingAdds.value).toEqual([]);
  });
});

describe("useDeckEditStaging — cap and limit math", () => {
  it("caps a Unique card at 1 copy and other rarities at 3", () => {
    expect(cardLimit("Unique")).toBe(1);
    expect(cardLimit("Common")).toBe(3);
    expect(cardLimit("Rare")).toBe(3);
  });

  it("flags a Unique card at limit once one confirmed copy exists", () => {
    const { staging } = setup(
      librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 1, "Unique")] }] }),
    );

    expect(staging.isAtLimit(availableCard(1, "", { rarity: "Unique" }))).toBe(true);
  });

  it("counts a pending add toward the limit", () => {
    const { staging } = setup(librarian({ decks: [{ index: 0, cards: [] }] }));

    const inventoryCard = availableCard(1, "", { rarity: "Unique" });
    expect(staging.isAtLimit(inventoryCard)).toBe(false);

    staging.pendingAdds.value.push(pendingEntry(0, 1, "", 1));
    expect(staging.isAtLimit(inventoryCard)).toBe(true);
  });

  it("frees a limit slot while a copy is pending-removed", () => {
    const { staging } = setup(
      librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 3, "Rare")] }] }),
    );

    const inventoryCard = availableCard(1, "", { rarity: "Rare" });
    expect(staging.isAtLimit(inventoryCard)).toBe(true);

    staging.pendingRemoves.value.push(pendingEntry(0, 1, "", 1));
    expect(staging.isAtLimit(inventoryCard)).toBe(false);
  });
});

describe("useDeckEditStaging — DECK_MAX and empty-slot count", () => {
  it("reports empty slots for a partially-filled deck", () => {
    const { staging } = setup(
      librarian({
        decks: [{ index: 0, cards: [deckCard(1, "", 7)] }],
      }),
    );

    staging.pendingAdds.value.push(pendingEntry(0, 2, "", 1));

    expect(staging.effectiveDeckCount.value).toBe(8);
    expect(staging.emptySlotCount.value).toBe(DECK_MAX - 8);
  });

  it("never reports a negative empty-slot count for an over-full deck", () => {
    const { staging } = setup(
      librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 10)] }] }),
    );

    expect(staging.effectiveDeckCount.value).toBe(10);
    expect(staging.emptySlotCount.value).toBe(0);
  });
});

describe("useDeckEditStaging — active deck clamp", () => {
  it("resets activeDeckIndex to 0 when the deck slot it points to disappears", async () => {
    const { staging, libRef } = setup(
      librarian({
        decks: [
          { index: 0, cards: [] },
          { index: 1, cards: [] },
          { index: 2, cards: [] },
          { index: 3, cards: [] },
        ],
      }),
    );

    staging.activeDeckIndex.value = 2;
    expect(staging.activeDeckIndex.value).toBe(2);

    // re-equip collapses to a single-deck key page
    libRef.value = librarian({ decks: [{ index: 0, cards: [] }] });
    await nextTick();

    expect(staging.activeDeckIndex.value).toBe(0);
  });

  it("leaves activeDeckIndex alone when the pointed-to slot still exists", async () => {
    const { staging, libRef } = setup(
      librarian({
        decks: [
          { index: 0, cards: [] },
          { index: 1, cards: [] },
        ],
      }),
    );

    staging.activeDeckIndex.value = 1;

    libRef.value = librarian({
      decks: [
        { index: 0, cards: [deckCard(1, "", 1)] },
        { index: 1, cards: [] },
      ],
    });
    await nextTick();

    expect(staging.activeDeckIndex.value).toBe(1);
  });
});

describe("useDeckEditStaging — connection-boundary reset", () => {
  it("discards all pending edits when stateGeneration bumps", async () => {
    const { staging, generationRef } = setup(
      librarian({ decks: [{ index: 0, cards: [] }] }),
    );

    staging.pendingAdds.value.push(pendingEntry(0, 1, "", 1));
    staging.pendingRemoves.value.push(pendingEntry(0, 2, "", 2));
    expect(staging.pendingAdds.value).toHaveLength(1);
    expect(staging.pendingRemoves.value).toHaveLength(1);

    generationRef.value += 1;
    await nextTick();

    expect(staging.pendingAdds.value).toEqual([]);
    expect(staging.pendingRemoves.value).toEqual([]);
  });

  it("does not let a stale prevDeckCounts snapshot resurrect after a reset", async () => {
    // Regression guard: a reset must not leave a subsequent no-op decks tick
    // misreading the post-reset baseline as a delta and dropping edits queued
    // *after* the reset.
    const { staging, libRef, generationRef } = setup(
      librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 1)] }] }),
    );

    generationRef.value += 1;
    await nextTick();

    const postReset = pendingEntry(0, 1, "", 1);
    staging.pendingAdds.value.push(postReset);

    // identical content, fresh reference — should be a no-op diff
    libRef.value = librarian({ decks: [{ index: 0, cards: [deckCard(1, "", 1)] }] });
    await nextTick();

    expect(staging.pendingAdds.value).toEqual([postReset]);
  });
});
