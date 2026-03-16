import type { GameState, SessionState, PlayerInfo, ActionResult } from "~/types/game";
import { applyDelta } from "~/utils/deltaApply";

const SESSION_STORAGE_KEY = "plwm_session";

type Status = "connecting" | "connected" | "disconnected";

/**
 * Manages the WebSocket connection to the mod server. Handles session
 * persistence across reconnects, sequence-number tracking for delta
 * messages, and promise-based action dispatch with per-request IDs.
 *
 * Usage: call once in app.vue; pass the returned refs/functions down.
 */
export function useWebSocket() {
  const gameState = ref<GameState | null>(null);
  const session = ref<SessionState | null>(null);
  const status = ref<Status>("connecting");
  const players = ref<PlayerInfo[]>([]);

  let ws: WebSocket | null = null;
  let lastSeq = 0;

  // Pending action promises keyed by reqId. Each entry holds a resolve
  // callback and a safety timeout so callers never hang indefinitely.
  const pending = new Map<
    string,
    { resolve: (r: ActionResult) => void; timer: ReturnType<typeof setTimeout> }
  >();

  // -------------------------------------------------------------------------
  // Connection management
  // -------------------------------------------------------------------------

  function connect() {
    status.value = "connecting";

    // Resume an existing session if we have one stored locally.
    const storedId = localStorage.getItem(SESSION_STORAGE_KEY);
    const path = storedId ? `/ws?session=${encodeURIComponent(storedId)}` : "/ws";

    // Build an absolute WebSocket URL from the current page origin.
    const wsProto = location.protocol === "https:" ? "wss:" : "ws:";
    ws = new WebSocket(`${wsProto}//${location.host}${path}`);

    ws.onopen = () => {
      status.value = "connected";
    };

    ws.onmessage = (ev: MessageEvent) => {
      try {
        handleMessage(JSON.parse(ev.data as string));
      } catch {
        // malformed message — ignore
      }
    };

    ws.onclose = () => {
      status.value = "disconnected";
      ws = null;
      // Reject any outstanding action promises so callers don't hang.
      for (const [id, p] of pending) {
        clearTimeout(p.timer);
        p.resolve({ ok: false, error: "Connection lost" });
        pending.delete(id);
      }
      setTimeout(connect, 2000);
    };

    ws.onerror = () => {
      ws?.close();
    };
  }

  // -------------------------------------------------------------------------
  // Message dispatch
  // -------------------------------------------------------------------------

  function handleMessage(msg: Record<string, unknown>) {
    switch (msg.type) {
      case "hello":
        session.value = {
          sessionId: msg.sessionId as string,
          assignedUnits: (msg.assignedUnits as number[]) ?? [],
          claimsEnabled: (msg.claimsEnabled as boolean) ?? true,
        };
        localStorage.setItem(SESSION_STORAGE_KEY, msg.sessionId as string);
        break;

      case "state":
        lastSeq = msg.seq as number;
        gameState.value = msg.data as GameState;
        break;

      case "delta": {
        const seq = msg.seq as number;
        if (gameState.value && seq === lastSeq + 1) {
          gameState.value = applyDelta(gameState.value, msg.data as Record<string, unknown>);
          lastSeq = seq;
        } else {
          // Gap detected — request a full resync.
          send({ type: "resync" });
        }
        break;
      }

      case "sessionUpdate":
        if (session.value) {
          session.value = {
            ...session.value,
            assignedUnits: (msg.assignedUnits as number[]) ?? [],
          };
        }
        break;

      case "playerList":
        players.value = (msg.players as PlayerInfo[]) ?? [];
        break;

      case "actionResult": {
        const p = pending.get(msg.reqId as string);
        if (p) {
          clearTimeout(p.timer);
          pending.delete(msg.reqId as string);
          p.resolve({ ok: msg.ok as boolean, error: msg.error as string | undefined });
        }
        break;
      }

      // Server sends protocol-level pings; browser auto-responds with pong.
      // This case handles any leftover application-level pings from older builds.
      case "ping":
        send({ type: "pong" });
        break;
    }
  }

  // -------------------------------------------------------------------------
  // Outgoing messages
  // -------------------------------------------------------------------------

  function send(msg: Record<string, unknown>) {
    if (ws?.readyState === WebSocket.OPEN)
      ws.send(JSON.stringify(msg));
  }

  /**
   * Sends an action and returns a promise that resolves when the server
   * responds with an actionResult. Times out after 5 seconds.
   */
  function sendAction(action: Record<string, unknown>): Promise<ActionResult> {
    const reqId = crypto.randomUUID();
    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        pending.delete(reqId);
        resolve({ ok: false, error: "Action timed out" });
      }, 5000);
      pending.set(reqId, { resolve, timer });
      send({ ...action, reqId });
    });
  }

  function claimUnit(unitId: number): Promise<ActionResult> {
    return sendAction({ type: "claimUnit", unitId });
  }

  function releaseUnit(unitId: number): Promise<ActionResult> {
    return sendAction({ type: "releaseUnit", unitId });
  }

  function renamePlayer(name: string): Promise<ActionResult> {
    return sendAction({ type: "rename", name: name.trim() });
  }

  function lockLibrarian(floorIndex: number, unitIndex: number): Promise<ActionResult> {
    return sendAction({ type: "lockLibrarian", floorIndex, unitIndex });
  }

  function unlockLibrarian(floorIndex: number, unitIndex: number): Promise<ActionResult> {
    return sendAction({ type: "unlockLibrarian", floorIndex, unitIndex });
  }

  function renameLibrarian(
    floorIndex: number,
    unitIndex: number,
    name: string,
  ): Promise<ActionResult> {
    return sendAction({ type: "renameLibrarian", floorIndex, unitIndex, name: name.trim() });
  }

  function equipKeyPage(
    floorIndex: number,
    unitIndex: number,
    bookInstanceId: number,
  ): Promise<ActionResult> {
    return sendAction({ type: "equipKeyPage", floorIndex, unitIndex, bookInstanceId });
  }

  function addCardToDeck(
    floorIndex: number,
    unitIndex: number,
    cardId: number,
    packageId: string,
  ): Promise<ActionResult> {
    return sendAction({ type: "addCardToDeck", floorIndex, unitIndex, cardId, packageId });
  }

  function removeCardFromDeck(
    floorIndex: number,
    unitIndex: number,
    cardId: number,
    packageId: string,
  ): Promise<ActionResult> {
    return sendAction({ type: "removeCardFromDeck", floorIndex, unitIndex, cardId, packageId });
  }

  onMounted(connect);

  return {
    gameState,
    session,
    status,
    players,
    sendAction,
    claimUnit,
    releaseUnit,
    renamePlayer,
    lockLibrarian,
    unlockLibrarian,
    renameLibrarian,
    equipKeyPage,
    addCardToDeck,
    removeCardFromDeck,
  };
}
