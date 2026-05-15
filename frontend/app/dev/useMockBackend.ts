import { ref, watchEffect } from "vue";
import type {
  GameState,
  SessionState,
  PlayerInfo,
  ActionResult,
  ClientAction,
} from "../types/game";
import { applyTheme } from "../utils/applyTheme";
import { FIXTURE_LOADERS } from "./fixtures";

type Status = "connecting" | "connected" | "disconnected";

declare global {
  interface Window {
    __plwmMock?: {
      setFixture: (name: string) => void;
      listFixtures: () => string[];
    };
  }
}

/**
 * Dev-only drop-in replacement for `useWebSocket` that drives `gameState`
 * from a static fixture instead of a live WebSocket. Returns the same public
 * shape so downstream components cannot tell mock mode is active.
 *
 * - Fixtures are swapped at runtime by mutating `currentFixture` (typically
 *   via `window.__plwmMock.setFixture(name)` which the dev picker calls).
 * - Every action function resolves `{ok: true}` and logs the payload with a
 *   `[mock]` prefix — interactions do not hang, but also do not mutate state.
 *
 * The entire module is tree-shaken from production because its only caller
 * (`useWebSocket`) wraps the import in `if (import.meta.dev)`.
 */
export function useMockBackend(fixtureName: string) {
  const gameState = ref<GameState | null>(null);
  const session = ref<SessionState | null>({
    sessionId: "mock-session",
    assignedUnits: [],
    // claims disabled so every interaction is reachable without having to
    // prearrange ownership from the mock side.
    claimsEnabled: false,
  });
  const status = ref<Status>("connected");
  const players = ref<PlayerInfo[]>([
    { sessionId: "mock-session", name: "You (mock)", units: [] },
  ]);

  const currentFixture = ref(fixtureName);

  watchEffect(() => {
    const name = currentFixture.value;
    const loader = FIXTURE_LOADERS[name];
    if (!loader) {
      const known = Object.keys(FIXTURE_LOADERS).join(", ") || "<none>";
      console.error(`[mock] unknown fixture "${name}". known: ${known}`);
      gameState.value = null;
      return;
    }
    const state = loader();
    gameState.value = state;
    // mirror useWebSocket so fixtures that include a theme block exercise the
    // CSS-var handshake; absent theme leaves the :root defaults in place.
    if (typeof document !== "undefined") {
      applyTheme(state.theme, document.documentElement);
    }
  });

  if (typeof window !== "undefined") {
    window.__plwmMock = {
      setFixture(name: string) {
        currentFixture.value = name;
      },
      listFixtures: () => Object.keys(FIXTURE_LOADERS),
    };
  }

  // every action handler shares identical behaviour: log with a [mock] prefix
  // and resolve ok:true without mutating state.
  function log(payload: Record<string, unknown>): Promise<ActionResult> {
    console.log(`[mock] action: ${JSON.stringify(payload)}`);
    return Promise.resolve({ ok: true });
  }

  const sendAction = (action: ClientAction | Record<string, unknown>) =>
    log(action as Record<string, unknown>);
  const claimUnit = (unitId: number) => log({ type: "claimUnit", unitId });
  const releaseUnit = (unitId: number) => log({ type: "releaseUnit", unitId });
  const renamePlayer = (name: string) => log({ type: "rename", name: name.trim() });
  const lockLibrarian = (floorIndex: number, unitIndex: number) =>
    log({ type: "lockLibrarian", floorIndex, unitIndex });
  const unlockLibrarian = (floorIndex: number, unitIndex: number) =>
    log({ type: "unlockLibrarian", floorIndex, unitIndex });
  const renameLibrarian = (floorIndex: number, unitIndex: number, name: string) =>
    log({ type: "renameLibrarian", floorIndex, unitIndex, name: name.trim() });
  const equipKeyPage = (floorIndex: number, unitIndex: number, bookInstanceId: number) =>
    log({ type: "equipKeyPage", floorIndex, unitIndex, bookInstanceId });
  const unequipKeyPage = (floorIndex: number, unitIndex: number) =>
    log({ type: "unequipKeyPage", floorIndex, unitIndex });
  const addCardToDeck = (
    floorIndex: number,
    unitIndex: number,
    cardId: number,
    packageId: string,
    deckIndex?: number,
  ) =>
    log({
      type: "addCardToDeck",
      floorIndex,
      unitIndex,
      cardId,
      packageId,
      ...(deckIndex != null ? { deckIndex } : {}),
    });
  const removeCardFromDeck = (
    floorIndex: number,
    unitIndex: number,
    cardId: number,
    packageId: string,
    deckIndex?: number,
  ) =>
    log({
      type: "removeCardFromDeck",
      floorIndex,
      unitIndex,
      cardId,
      packageId,
      ...(deckIndex != null ? { deckIndex } : {}),
    });
  const equipSourceBook = (
    floorIndex: number,
    unitIndex: number,
    bookInstanceId: number,
  ) => log({ type: "equipSourceBook", floorIndex, unitIndex, bookInstanceId });
  const unequipSourceBook = (
    floorIndex: number,
    unitIndex: number,
    bookInstanceId: number,
  ) => log({ type: "unequipSourceBook", floorIndex, unitIndex, bookInstanceId });
  const attributePassive = (
    floorIndex: number,
    unitIndex: number,
    sourceInstanceId: number,
    passiveId: number,
    passivePackageId: string,
  ) =>
    log({
      type: "attributePassive",
      floorIndex,
      unitIndex,
      sourceInstanceId,
      passiveId,
      passivePackageId,
    });
  const removeAttributedPassive = (
    floorIndex: number,
    unitIndex: number,
    sourceInstanceId: number,
    passiveId: number,
    passivePackageId: string,
  ) =>
    log({
      type: "removeAttributedPassive",
      floorIndex,
      unitIndex,
      sourceInstanceId,
      passiveId,
      passivePackageId,
    });

  return {
    gameState,
    session,
    status,
    players,
    // mock backend never reconnects, so the generation never bumps.
    // exposed only to keep the return shape parity with useWebSocket.
    stateGeneration: ref(0),
    // Diagnostic mirrors are static in mock mode (no real WebSocket
    // pipeline); exposed only for return-shape parity with useWebSocket.
    inflightCount: ref(0),
    lastSeqRef: ref(0),
    resyncCount: ref(0),
    lastResyncAt: ref<number | null>(null),
    sendAction,
    claimUnit,
    releaseUnit,
    renamePlayer,
    lockLibrarian,
    unlockLibrarian,
    renameLibrarian,
    equipKeyPage,
    unequipKeyPage,
    addCardToDeck,
    removeCardFromDeck,
    equipSourceBook,
    unequipSourceBook,
    attributePassive,
    removeAttributedPassive,
  };
}
