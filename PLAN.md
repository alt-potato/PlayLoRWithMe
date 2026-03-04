# High-Level Implementation Plan: PlayLoRWithMe

## Context

PlayLoRWithMe is a co-op mod for Library of Ruina that lets multiple remote players collectively control a single game instance through a browser-based web UI. The project is currently a skeleton: a working mod entry point (`Initializer.cs`) and placeholder mod data XMLs exist, but zero functional code has been written. This document describes the architecture needed to fulfill the mod's stated goal.

---

## System Architecture Overview

The mod runs entirely inside the player's game process. Remote co-op players connect via a browser — no game install required on their end.

```
[Game Process]
  Initializer.cs
    |- Embedded HTTP/SSE server  <->  [Browser clients]
    |- Game State Reader          <->  serializes LoR game objects -> JSON
    '- Input Injector (Harmony patches) <-  receives actions from clients
```

> **Note:** Real-time push is implemented via Server-Sent Events (SSE) rather than WebSocket.
> `HttpListenerContext.AcceptWebSocketAsync` is not reliably implemented in Unity's Mono runtime.

---

## Component 1: Embedded Web Server [x]

**Where:** `Server.cs`, started from `Initializer.OnInitialize()`.

**What it does:**
- Uses `System.Net.HttpListener` to serve:
  - `GET /` and all static paths -> serves the Nuxt frontend from `wwwroot/`
  - `GET /state` -> returns current game state as JSON
  - `GET /events` -> SSE stream; pushes state updates to all connected clients
  - `POST /action` -> accepts a player action and enqueues it in `ActionInjector`
- Runs on a background thread so it never blocks Unity's main thread.

**Known limitation:** Currently binds to `localhost` only. LAN/mobile access will require
binding to `*` (needs a `netsh urlacl` entry or admin rights) — deferred.

---

## Component 2: Game State Reader [ ]

**Where:** `GameStateSerializer.cs` (stub — returns `{"status":"ok"}`).

**What it does:**
- Reads Library of Ruina's runtime objects (battle scene, librarian units, card hands, enemy units, turn phase) and serializes them to a plain JSON structure the web UI can display.
- Key LoR classes to read: `BattleObjectManager`, `BattleUnitModel`, `HandCardManager`, `BattleDiceCardModel`.
- Called on every state-change event (new turn, card played, phase change) to push an updated snapshot via `Server.Instance.Broadcast()`.

---

## Component 3: Game Action Injector (Harmony Patches) [~]

**Where:** `ActionInjector.cs` — queue structure exists; Harmony patch not yet written.

**What it does:**
- Maintains a thread-safe `ConcurrentQueue` of pending player actions received from the web server.
- A Harmony `Prefix` or `Postfix` patch on the game's main update loop (or a coroutine) drains the queue each frame and feeds actions into the game's existing input/action system.
- This is the critical bridge: web requests -> C# queue -> Unity main thread -> game logic.
- Key actions to support: assign a combat page to a unit, confirm/lock in selections, target selection.

---

## Component 4: Player Assignment & Session Management [ ]

**Where:** `SessionManager.cs` (not yet created).

**What it does:**
- Each connecting browser gets a session ID (cookie or URL token).
- The host assigns each session a subset of librarian units they are responsible for controlling.
- The server enforces that a client can only submit actions for their assigned units.
- Simple in-memory state is sufficient (no persistence needed across sessions).

---

## Component 5: Web UI (Frontend) [~]

**Where:** `frontend/` — Nuxt (Vue 3) project; built output served from `wwwroot/`.

**Current state:** Placeholder page connects via SSE and displays raw JSON state.

**What remains:**
- Display game state meaningfully: librarians (HP, stagger), card hands, enemies (HP, intent), turn phase.
- Let a player click/drag to assign a card to their unit and confirm.
- Send actions via `fetch()` POST to `/action`.
- Re-render on each incoming SSE event.
- Card art: omit in MVP (text-only is fine for a first pass).

---

## Implementation Order

1. [x] **Web server skeleton** — HTTP + SSE server starts on mod load. Verified working in browser.
2. [x] **Real-time push** — SSE pushes state to all connected clients.
3. [ ] **State serialization** — Read actual LoR battle state and serialize to JSON.
4. [ ] **Action queue + Harmony patch** — Accept a card-play action from the browser, execute it in-game.
5. [ ] **Player assignment** — Multi-session support, unit ownership.
6. [ ] **Web UI polish** — Replace raw JSON display with a proper interactive UI.

---

## Key Files

| File | Purpose | Status |
|------|---------|--------|
| `mod/Initializer.cs` | Starts server on mod init | done |
| `mod/Server.cs` | `HttpListener` + SSE server | done |
| `mod/GameStateSerializer.cs` | LoR game objects -> JSON | stub |
| `mod/ActionInjector.cs` | Action queue (Harmony patch pending) | partial |
| `mod/SessionManager.cs` | Player <-> librarian assignment | not started |
| `frontend/` | Nuxt/Vue 3 browser UI | placeholder only |

---

## Verification

- [x] Start the game with the mod loaded; confirm `http://localhost:8080/` is reachable in a browser.
- [ ] Open two browser tabs; both should show live game state during a battle.
- [ ] Submit a card-play action from one tab; verify it executes in-game and both tabs reflect the updated state.
