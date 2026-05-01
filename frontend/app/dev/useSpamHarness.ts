import type { Ref } from "vue";
import type { GameState, SessionState, PlayerInfo, ActionResult } from "~/types/game";

/**
 * Spam-tap harness for reproducing deck-edit lockups under sustained
 * load. Exposes `window.__spamDeck(count, intervalMs?)` for ad-hoc
 * browser-console invocation.
 *
 * Gating: app.vue installs this only when the runtime debug flag is on
 * (URL `?debug=1` once, persisted via localStorage). The flag exists
 * because the mod's HTTP server only ever serves the production-generated
 * SPA, so `import.meta.dev` is always false at play-time and a build-flag
 * gate would never enable the harness.
 *
 * Prerequisites: the user must have already opened the EditPanel for a
 * librarian (so the lock is held by this session). The harness picks
 * the locked librarian and drives `addCardToDeck` / `removeCardFromDeck`
 * for `count` iterations, alternating between adds and removes. Each
 * call goes through the real WebSocket pipeline; no callsite-level
 * batching, no client-side throttling — the goal is to stress the
 * pipeline.
 */
export function installSpamHarness(deps: {
  gameState: Ref<GameState | null>;
  session: Ref<SessionState | null>;
  players: Ref<PlayerInfo[]>;
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
}) {
  async function spamDeck(count: number, intervalMs = 50) {
    const state = deps.gameState.value;
    const sess = deps.session.value;
    if (!state || !sess) {
      console.warn("[__spamDeck] no game state or session yet");
      return;
    }
    // The serializer emits `lockedBy` as the player's display *name*, not
    // their session ID, so resolve our own display name first via the
    // players list.
    const me = deps.players.value.find((p) => p.sessionId === sess.sessionId);
    const myName = me?.name;
    if (!myName) {
      console.warn(
        "[__spamDeck] could not resolve own display name — players list not yet populated",
      );
      return;
    }
    // find the librarian whose edit lock display-name matches mine.
    const myLib = state.floors
      ?.flatMap((f) =>
        f.librarians.map((l) => ({
          floorIndex: f.floorIndex,
          unitIndex: l.unitIndex,
          lockedBy: l.lockedBy,
        })),
      )
      .find((l) => l.lockedBy === myName);
    if (!myLib) {
      console.warn(
        "[__spamDeck] no librarian locked by this session — open the EditPanel for a librarian first",
      );
      return;
    }
    const cards = state.availableCards ?? [];
    if (!cards.length) {
      console.warn("[__spamDeck] no available cards in inventory");
      return;
    }

    console.log(
      `[__spamDeck] starting: ${count} ops every ${intervalMs}ms on librarian ` +
        `floor=${myLib.floorIndex} unit=${myLib.unitIndex}`,
    );
    const startedAt = Date.now();
    let adds = 0;
    let removes = 0;
    let okCount = 0;
    let failCount = 0;

    for (let i = 0; i < count; i++) {
      // pick a random card from inventory
      const card = cards[Math.floor(Math.random() * cards.length)];
      if (!card) continue;

      // alternate adds and removes; remove operates on the same random
      // card and is allowed to no-op server-side if the card isn't in
      // the deck. that's fine — the goal is wire-pipeline pressure.
      const isAdd = i % 2 === 0;
      const promise = isAdd
        ? deps.addCardToDeck(
            myLib.floorIndex,
            myLib.unitIndex,
            card.cardId.id,
            card.cardId.packageId,
          )
        : deps.removeCardFromDeck(
            myLib.floorIndex,
            myLib.unitIndex,
            card.cardId.id,
            card.cardId.packageId,
          );
      isAdd ? adds++ : removes++;
      // intentionally don't await — fire-and-forget so calls overlap.
      promise.then((r) => {
        if (r.ok) okCount++;
        else failCount++;
      });

      if (intervalMs > 0)
        await new Promise((r) => setTimeout(r, intervalMs));
    }

    console.log(
      `[__spamDeck] dispatched ${count} ops in ${Date.now() - startedAt}ms ` +
        `(adds=${adds}, removes=${removes}). awaiting responses...`,
    );
    // wait a bit for promises to settle, then report.
    setTimeout(() => {
      console.log(
        `[__spamDeck] settled: ok=${okCount}, fail=${failCount}, ` +
          `unaccounted=${count - okCount - failCount}`,
      );
    }, 6000);
  }

  // Exposing on window for ad-hoc console use; the global is intentional
  // (dev-only) and never referenced from any module — only typed in
  // a non-strict comment to keep the rest of the codebase clean.
  (window as unknown as { __spamDeck?: typeof spamDeck }).__spamDeck = spamDeck;
  console.log(
    "[__spamDeck] harness installed. Run __spamDeck(count, intervalMs?) in the console.",
  );
}
