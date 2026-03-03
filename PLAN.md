# High-Level Implementation Plan: PlayLoRWithMe

## Context

PlayLoRWithMe is a co-op mod for Library of Ruina that lets multiple remote players collectively control a single game instance through a browser-based web UI. The project is currently a skeleton: a working mod entry point (`Initializer.cs`) and placeholder mod data XMLs exist, but zero functional code has been written. This document describes the architecture needed to fulfill the mod's stated goal.

---

## System Architecture Overview

The mod runs entirely inside the player's game process. Remote co-op players connect via a browser — no game install required on their end.

```
[Game Process]
  Initializer.cs
    ├── Embedded HTTP/WebSocket server  ←→  [Browser clients]
    ├── Game State Reader               ←→  serializes LoR game objects → JSON
    └── Input Injector (Harmony patches) ←  receives actions from clients
```

---

## Component 1: Embedded Web Server

**Where:** A new class, e.g. `Server.cs`, started from `Initializer.OnInitialize()`.

**What it does:**
- Uses `System.Net.HttpListener` (built into .NET 4.8, no extra deps) to serve:
  - `GET /` → serves the static web UI (HTML/JS bundled as embedded resources or served from the mod folder)
  - `GET /state` → returns current game state as JSON
  - `POST /action` → accepts a player action (e.g. play card X on unit Y)
- Upgrades connections to WebSocket for real-time push of game state changes

**Runs on:** A background thread so it never blocks Unity's main thread.

---

## Component 2: Game State Reader

**Where:** A new class, e.g. `GameStateSerializer.cs`.

**What it does:**
- Reads Library of Ruina's runtime objects (battle scene, librarian units, card hands, enemy units, turn phase) and serializes them to a plain JSON structure the web UI can display.
- Key LoR classes to read: `BattleObjectManager`, `BattleUnitModel`, `HandCardManager`, `BattleDiceCardModel`.
- Called on every state-change event (new turn, card played, phase change) to push an updated snapshot over WebSocket to all connected clients.

---

## Component 3: Game Action Injector (Harmony Patches)

**Where:** A new class, e.g. `ActionInjector.cs`, using Harmony (already available in LoR's mod ecosystem).

**What it does:**
- Maintains a thread-safe queue of pending player actions received from the web server.
- A Harmony `Prefix` or `Postfix` patch on the game's main update loop (or a coroutine) drains the queue each frame and feeds actions into the game's existing input/action system.
- This is the critical bridge: web requests → C# queue → Unity main thread → game logic.
- Key actions to support: assign a combat page to a unit, confirm/lock in selections, target selection.

---

## Component 4: Player Assignment & Session Management

**Where:** Part of `Server.cs` or a small `SessionManager.cs`.

**What it does:**
- Each connecting browser gets a session ID (cookie or URL token).
- The host assigns each session a subset of librarian units they are responsible for controlling.
- The server enforces that a client can only submit actions for their assigned units.
- Simple in-memory state is sufficient (no persistence needed across sessions).

---

## Component 5: Web UI (Frontend)

**Where:** Static files embedded in the mod assembly as resources, or placed in the mod's folder and served by the HTTP server.

**What it does:**
- Displays the current game state: librarians (with HP, stagger), their card hands, enemies (HP, intent), current turn phase.
- Lets a player drag/click to assign a card to their unit and hit "Confirm".
- Sends actions via `fetch()` POST to `/action`.
- Receives live state updates via WebSocket and re-renders.
- Card art can be shown via game asset paths or omitted in an MVP (text-only UI is fine for a first pass).

---

## Implementation Order (Recommended)

1. **Web server skeleton** — HTTP server starts on mod load, returns `{"status":"ok"}` from `/state`. Verify connection from browser.
2. **State serialization** — Serialize basic battle state (units, HP, hand) to JSON; display it in the browser.
3. **WebSocket push** — Push state updates to clients in real time.
4. **Action queue + Harmony patch** — Accept a card-play action from the browser, execute it in-game.
5. **Player assignment** — Multi-session support, unit ownership.
6. **Web UI polish** — Replace raw JSON display with a proper interactive UI.

---

## Key Files to Create/Modify

| File | Purpose |
|------|---------|
| `Initializer.cs` | Start server and Harmony patcher on init |
| `Server.cs` | `HttpListener` + WebSocket server |
| `GameStateSerializer.cs` | LoR game objects → JSON |
| `ActionInjector.cs` | Harmony patch + action queue |
| `SessionManager.cs` | Player ↔ librarian assignment |
| `wwwroot/index.html` | Browser UI |

---

## Verification

- Start the game with the mod loaded; confirm `http://localhost:<port>/` is reachable in a browser.
- Open two browser tabs; both should show live game state during a battle.
- Submit a card-play action from one tab; verify it executes in-game and both tabs reflect the updated state.
