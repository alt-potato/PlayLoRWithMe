import { z } from "zod/mini";
import type { GameState, SessionState, PlayerInfo, ActionResult, ServerMessage, ClientAction } from "~/types/game";
import { ServerMessageSchema } from "~/types/game";
import { applyDelta } from "~/utils/deltaApply";
import { applyTheme } from "~/utils/applyTheme";
import { resolveMockFixture } from "~/dev/resolveMockFixture";
import { useMockBackend } from "~/dev/useMockBackend";

const SESSION_STORAGE_KEY = "plwm_session";
const DISPLAY_NAME_STORAGE_KEY = "plwm_display_name";
// Exponential backoff (with jitter) for reconnect attempts. Without backoff a
// flaky AP/hotspot blip would have every connected client banging on the
// server every 2s in lockstep; the jitter spreads recovery and the cap keeps
// the worst-case re-acquire latency reasonable.
const RECONNECT_BASE_MS = 1000;
const RECONNECT_CAP_MS = 15000;
const ACTION_TIMEOUT_MS = 5000;

/**
 * Reads the player's chosen display name from localStorage, returning an
 * empty string if absent or if storage is unavailable (private mode, quota
 * exceeded, disabled by policy). Callers should treat empty as "no stored
 * name" — never send an empty rename on restore.
 */
export function loadStoredDisplayName(): string {
  try {
    return localStorage.getItem(DISPLAY_NAME_STORAGE_KEY) ?? "";
  } catch {
    return "";
  }
}

/**
 * Persists the player's chosen display name to localStorage. Silently
 * tolerates storage being unavailable — the feature degrades to the
 * pre-feature behavior (server keeps its current name) rather than
 * surfacing an error to the rename UI.
 */
export function saveStoredDisplayName(name: string): void {
  try {
    localStorage.setItem(DISPLAY_NAME_STORAGE_KEY, name);
  } catch {
    // intentionally swallowed — see jsdoc.
  }
}

/**
 * Pure decision helper for the on-connect restore path. Returns the name
 * to send via `rename`, or `null` when no rename is required.
 *
 * - Returns `null` if no name is stored locally.
 * - Returns `null` if the player list lacks an entry for this session
 *   (defensive — should never happen in practice).
 * - Returns `null` if the server already has the stored name (resumed
 *   session — no redundant rename).
 * - Otherwise returns the stored name so the caller can dispatch a rename.
 */
export function pickDisplayNameRestore(
  stored: string,
  sessionId: string,
  players: PlayerInfo[],
): string | null {
  if (!stored) return null;
  const me = players.find((p) => p.sessionId === sessionId);
  if (!me) return null;
  if (me.name === stored) return null;
  return stored;
}

// Watchdog tunables — defense-in-depth against future stuck-in-flight
// failures of the same shape as the spam-induced lockup. The pair is
// generous enough that normal bursts (where most entries resolve well
// inside the per-request timeout) never trip it; only systemic stalls
// where many requests sit unanswered past their own timeout do.
const LOCKUP_THRESHOLD = 20;
const LOCKUP_TIMEOUT_MS = 5000;
const WATCHDOG_INTERVAL_MS = 1000;

type Status = "connecting" | "connected" | "disconnected";

/**
 * Manages the WebSocket connection to the mod server. Handles session
 * persistence across reconnects, sequence-number tracking for delta
 * messages, and promise-based action dispatch with per-request IDs.
 *
 * Usage: call once in app.vue; pass the returned refs/functions down.
 */
export function useWebSocket() {
  // dev-only fixture mode — when a fixture name is resolvable from the URL
  // or localStorage, hand back a mock backend and never open a socket. The
  // `import.meta.dev` guard collapses to `if (false)` in production, which
  // lets Rollup tree-shake this branch (and every symbol it references).
  if (import.meta.dev) {
    const mockName = resolveMockFixture();
    if (mockName) return useMockBackend(mockName);
  }

  // shallowRef — every patch path (initial state, delta, resync) replaces
  // gameState.value with a fresh top-level object. Deep reactivity would walk
  // the entire game tree (nested allies, decks, slottedCards…) on every send;
  // shallow tracking is sufficient because consumers re-derive from the new
  // root reference rather than mutating in place.
  const gameState = shallowRef<GameState | null>(null);
  const session = ref<SessionState | null>(null);
  const status = ref<Status>("connecting");
  const players = ref<PlayerInfo[]>([]);
  // bumps each time a fresh full-state payload arrives (initial connect,
  // reconnect, resync). consumers with optimistic UI state watch this and
  // discard pending edits when it changes — those edits would be phantoms
  // against the new authoritative state.
  const stateGeneration = ref(0);

  // Dev-time diagnostic mirrors. Updated alongside the closure variables
  // they shadow so DiagnosticPanel can render them reactively. Tree-shakes
  // out of production along with the panel that consumes them.
  const inflightCount = ref(0);
  const lastSeqRef = ref(0);
  const resyncCount = ref(0);
  const lastResyncAt = ref<number | null>(null);

  let ws: WebSocket | null = null;
  let lastSeq = 0;
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  let closing = false;
  // Counts consecutive failed connect attempts (without an intervening open).
  // Drives the exponential-backoff calculation and resets to zero once we get
  // a fresh open event.
  let reconnectAttempt = 0;
  // Tracks whether the next "state" message arrives across a connection
  // boundary (initial connect / reconnect) versus a mid-session resync. Only
  // the former bumps `stateGeneration` — on a gap-resync the server is still
  // alive and any pending optimistic edits can be reconciled by per-feature
  // diff watchers instead of being wiped wholesale.
  let expectingConnectionState = true;
  // True once the per-connection display-name restore has been attempted
  // (whether it sent a rename or skipped because the server already had
  // the right name). Reset on disconnect so a reconnect that lands on a
  // newly-created server-side session re-runs the check.
  let nameRestoreAttempted = false;
  // The server sends `playerList` BEFORE `hello` on connect (the attach
  // step broadcasts the roster, then the per-client hello goes out), so
  // the restore can't run from either handler in isolation — it has to
  // wait until both have arrived. This flag closes the gap.
  let playerListReceived = false;
  // True while a resync has been requested but the replacement full-state
  // message hasn't arrived yet. Debounces the gap-recovery path so a burst of
  // gapped/out-of-order deltas requests one resync instead of N. Cleared when
  // the next full "state" message lands (the resync response) or on disconnect.
  let resyncPending = false;

  // Runs the on-connect display-name restore once both prerequisites are
  // satisfied: we know our session id (from `hello`) and we have the
  // server's view of our current name (from `playerList`). Idempotent —
  // safe to call from either handler regardless of arrival order.
  function tryRestoreDisplayName() {
    if (nameRestoreAttempted) return;
    if (!session.value || !playerListReceived) return;
    const restoreName = pickDisplayNameRestore(
      loadStoredDisplayName(),
      session.value.sessionId,
      players.value,
    );
    if (restoreName) send({ type: "rename", name: restoreName });
    nameRestoreAttempted = true;
  }

  // Pending action promises keyed by reqId. Each entry holds a resolve
  // callback, a safety timeout so callers never hang indefinitely, and
  // the enqueue timestamp so the watchdog can detect entries stalled
  // past LOCKUP_TIMEOUT_MS.
  const pending = new Map<
    string,
    {
      resolve: (r: ActionResult) => void;
      timer: ReturnType<typeof setTimeout>;
      enqueuedAt: number;
    }
  >();

  // -------------------------------------------------------------------------
  // Connection management
  // -------------------------------------------------------------------------

  function connect() {
    // Guard against re-entry (fast open/error cycles, HMR): clear any pending
    // reconnect timer and fully detach + close the previous socket first, so a
    // late event from an orphaned socket can't schedule a second reconnect or
    // leave a leaked connection open.
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }
    if (ws) {
      ws.onopen = ws.onmessage = ws.onerror = ws.onclose = null;
      try {
        ws.close();
      } catch {
        // already closing/closed — nothing to do.
      }
      ws = null;
    }

    status.value = "connecting";

    // Resume an existing session if we have one stored locally.
    const storedId = localStorage.getItem(SESSION_STORAGE_KEY);
    const path = storedId ? `/ws?session=${encodeURIComponent(storedId)}` : "/ws";

    // Build an absolute WebSocket URL from the current page origin.
    const wsProto = location.protocol === "https:" ? "wss:" : "ws:";
    ws = new WebSocket(`${wsProto}//${location.host}${path}`);

    ws.onopen = () => {
      status.value = "connected";
      reconnectAttempt = 0;
      expectingConnectionState = true;
    };

    ws.onmessage = (ev: MessageEvent) => {
      try {
        const raw = JSON.parse(ev.data as string);
        // dev-only schema check: log structured drift to the console without
        // throwing, so the developer keeps working while the mismatch is
        // diagnosed. tree-shaken from production by `import.meta.dev`.
        if (import.meta.dev) {
          const result = z.safeParse(ServerMessageSchema, raw);
          if (!result.success) {
            console.error("[wire-contract] WebSocket payload violates schema:", result.error.issues);
          }
        }
        handleMessage(raw);
      } catch {
        // malformed message — ignore
      }
    };

    ws.onclose = () => {
      // Detach this (now dead) socket's handlers so a late buffered event
      // can't re-enter handleMessage or schedule a duplicate reconnect.
      if (ws) ws.onopen = ws.onmessage = ws.onerror = ws.onclose = null;
      status.value = "disconnected";
      ws = null;
      nameRestoreAttempted = false;
      playerListReceived = false;
      resyncPending = false;
      // Reject any outstanding action promises so callers don't hang.
      for (const [id, p] of pending) {
        clearTimeout(p.timer);
        p.resolve({ ok: false, error: "Connection lost" });
        pending.delete(id);
      }
      inflightCount.value = pending.size;
      if (!closing) {
        const expo = Math.min(
          RECONNECT_CAP_MS,
          RECONNECT_BASE_MS * 2 ** reconnectAttempt,
        );
        // jitter in [0.5, 1.0] spreads simultaneous client reconnects when a
        // shared upstream (router, ap) flaps.
        const delay = Math.floor(expo * (0.5 + Math.random() * 0.5));
        reconnectAttempt += 1;
        reconnectTimer = setTimeout(connect, delay);
      }
    };

    ws.onerror = () => {
      ws?.close();
    };
  }

  // -------------------------------------------------------------------------
  // Message dispatch
  // -------------------------------------------------------------------------

  function handleMessage(msg: ServerMessage) {
    switch (msg.type) {
      case "hello":
        session.value = {
          sessionId: msg.sessionId,
          assignedUnits: msg.assignedUnits ?? [],
          claimsEnabled: msg.claimsEnabled ?? true,
        };
        localStorage.setItem(SESSION_STORAGE_KEY, msg.sessionId);
        applyTheme(msg.theme, document.documentElement);
        // The post-attach playerList typically arrives before hello, so
        // run the restore from here in case we're the second to arrive.
        tryRestoreDisplayName();
        break;

      case "state":
        lastSeq = msg.seq;
        lastSeqRef.value = lastSeq;
        applyTheme(msg.data?.theme, document.documentElement);
        gameState.value = msg.data;
        // A full state is the authoritative baseline (initial, reconnect, or
        // resync response), so any outstanding resync request is now satisfied.
        resyncPending = false;
        // Only bump on the first state arrival per connection; mid-session
        // resyncs are reconciled by per-feature diff watchers instead of
        // wiping all optimistic UI state (e.g. pending deck-edit tiles).
        if (expectingConnectionState) {
          stateGeneration.value += 1;
          expectingConnectionState = false;
        }
        break;

      case "delta": {
        // Stale/duplicate delta (retransmit or out-of-order arrival): not a gap,
        // so drop it silently rather than triggering a needless resync.
        if (msg.seq <= lastSeq) break;
        if (gameState.value && msg.seq === lastSeq + 1) {
          gameState.value = applyDelta(gameState.value, msg.data);
          applyTheme(gameState.value.theme, document.documentElement);
          lastSeq = msg.seq;
          lastSeqRef.value = lastSeq;
        } else {
          // Genuine forward gap — request a full resync (debounced).
          requestResync();
        }
        break;
      }

      case "sessionUpdate":
        if (session.value) {
          session.value = {
            ...session.value,
            assignedUnits: msg.assignedUnits ?? [],
          };
        }
        break;

      case "playerList":
        players.value = msg.players ?? [];
        playerListReceived = true;
        // First playerList per connection carries this session's current
        // server-side name. When the server's name differs from the
        // locally stored one, the server is using its auto-assigned
        // default ("Player N") — which means the session was freshly
        // created (first visit, expiry, or server restart). Restore
        // silently. Resumed sessions where the server already has the
        // right name skip this — no redundant rename traffic on reload.
        tryRestoreDisplayName();
        break;

      case "actionResult": {
        const p = pending.get(msg.reqId);
        if (p) {
          clearTimeout(p.timer);
          pending.delete(msg.reqId);
          inflightCount.value = pending.size;
          p.resolve({ ok: msg.ok, error: msg.error });
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

  // Requests a full-state resync at most once until the response arrives,
  // so a run of gapped deltas can't trigger a resync storm.
  function requestResync() {
    if (resyncPending) return;
    resyncPending = true;
    send({ type: "resync" });
    resyncCount.value += 1;
    lastResyncAt.value = Date.now();
  }

  /**
   * Sends an action and returns a promise that resolves when the server
   * responds with an actionResult. Times out after 5 seconds.
   */
  function sendAction(action: ClientAction | Record<string, unknown>): Promise<ActionResult> {
    const reqId = crypto.randomUUID();
    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        pending.delete(reqId);
        inflightCount.value = pending.size;
        resolve({ ok: false, error: "Action timed out" });
      }, ACTION_TIMEOUT_MS);
      pending.set(reqId, { resolve, timer, enqueuedAt: Date.now() });
      inflightCount.value = pending.size;
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

  function unequipKeyPage(
    floorIndex: number,
    unitIndex: number,
  ): Promise<ActionResult> {
    return sendAction({ type: "unequipKeyPage", floorIndex, unitIndex });
  }

  function addCardToDeck(
    floorIndex: number,
    unitIndex: number,
    cardId: number,
    packageId: string,
    deckIndex?: number,
  ): Promise<ActionResult> {
    const payload: Record<string, unknown> = {
      type: "addCardToDeck",
      floorIndex,
      unitIndex,
      cardId,
      packageId,
    };
    if (deckIndex != null) payload.deckIndex = deckIndex;
    return sendAction(payload);
  }

  function removeCardFromDeck(
    floorIndex: number,
    unitIndex: number,
    cardId: number,
    packageId: string,
    deckIndex?: number,
  ): Promise<ActionResult> {
    const payload: Record<string, unknown> = {
      type: "removeCardFromDeck",
      floorIndex,
      unitIndex,
      cardId,
      packageId,
    };
    if (deckIndex != null) payload.deckIndex = deckIndex;
    return sendAction(payload);
  }

  function equipSourceBook(
    floorIndex: number,
    unitIndex: number,
    bookInstanceId: number,
  ): Promise<ActionResult> {
    return sendAction({ type: "equipSourceBook", floorIndex, unitIndex, bookInstanceId });
  }

  function unequipSourceBook(
    floorIndex: number,
    unitIndex: number,
    bookInstanceId: number,
  ): Promise<ActionResult> {
    return sendAction({ type: "unequipSourceBook", floorIndex, unitIndex, bookInstanceId });
  }

  function attributePassive(
    floorIndex: number,
    unitIndex: number,
    sourceInstanceId: number,
    passiveId: number,
    passivePackageId: string,
  ): Promise<ActionResult> {
    return sendAction({ type: "attributePassive", floorIndex, unitIndex, sourceInstanceId, passiveId, passivePackageId });
  }

  function removeAttributedPassive(
    floorIndex: number,
    unitIndex: number,
    sourceInstanceId: number,
    passiveId: number,
    passivePackageId: string,
  ): Promise<ActionResult> {
    return sendAction({ type: "removeAttributedPassive", floorIndex, unitIndex, sourceInstanceId, passiveId, passivePackageId });
  }

  onMounted(connect);

  // Watchdog: if many requests are stalled past their own per-request
  // timeout, force-resolve them with ok:false and request a resync.
  // The receive-loop crash that motivated this change is fixed
  // (server-side handlers now marshal to the Unity main thread), so
  // this is purely defense-in-depth — a regression of the same shape,
  // or any other systemic stall, surfaces visibly rather than leaving
  // the deck editor unresponsive.
  const watchdogTimer = setInterval(() => {
    if (pending.size <= LOCKUP_THRESHOLD) return;
    const now = Date.now();
    let oldest = Number.POSITIVE_INFINITY;
    for (const entry of pending.values()) {
      if (entry.enqueuedAt < oldest) oldest = entry.enqueuedAt;
    }
    if (now - oldest <= LOCKUP_TIMEOUT_MS) return;

    console.warn(
      `[deck-edit-watchdog] stalled: inflight=${pending.size} ` +
        `oldestAgeMs=${now - oldest} lastSeq=${lastSeq} status=${status.value}`,
    );
    for (const [id, p] of pending) {
      clearTimeout(p.timer);
      p.resolve({ ok: false, error: "watchdog: requests stalled" });
      pending.delete(id);
    }
    inflightCount.value = pending.size;
    send({ type: "resync" });
    resyncCount.value += 1;
    lastResyncAt.value = now;
  }, WATCHDOG_INTERVAL_MS);

  onBeforeUnmount(() => {
    closing = true;
    clearInterval(watchdogTimer);
    if (reconnectTimer) clearTimeout(reconnectTimer);
    if (ws) {
      // Detach handlers before closing so an in-flight message can't run
      // handleMessage (mutating refs / touching the DOM) after teardown.
      ws.onopen = ws.onmessage = ws.onerror = ws.onclose = null;
      ws.close();
      ws = null;
    }
  });

  return {
    gameState,
    session,
    status,
    players,
    stateGeneration,
    inflightCount,
    lastSeqRef,
    resyncCount,
    lastResyncAt,
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
