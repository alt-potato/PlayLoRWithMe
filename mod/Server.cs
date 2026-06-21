using System;
using System.IO;
using System.Net;
using System.Threading;
using LOR_DiceSystem;
using UnityEngine;

namespace PlayLoRWithMe
{
    public partial class Server
    {
        public const int Port = 8080;

        /// <summary>Builds the canonical lock key for a librarian slot.</summary>
        private static string LockKey(int floorIndex, int unitIndex) =>
            floorIndex + ":" + unitIndex;

        // DLL is in <mod root>/Assemblies/; wwwroot is a sibling of Assemblies/
        private static readonly string ModRootPath = Path.GetDirectoryName(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
        );

        internal static readonly string WwwRootPath = Path.Combine(ModRootPath, "wwwroot");

        private HttpListener _listener;
        private Thread _listenerThread;

        private readonly SessionManager _sessionManager = new SessionManager();
        private readonly DeltaEngine _deltaEngine = new DeltaEngine();

        // Last full (unfiltered) state snapshot, updated on the Unity main thread
        // by Broadcast(). Used to give new WebSocket clients an immediate initial
        // state without accessing Unity objects from the listener thread.
        private volatile bool _running = false;
        private volatile string _lastFullJson = null;

        // Set by claim/release handlers (listener thread) to request a filtered
        // broadcast from the Unity main thread on the next OnUpdate tick.
        // Uses int + Interlocked for atomic check-and-clear (0 = false, 1 = true).
        private int _pendingBroadcast = 0;

        /// <summary>
        /// When false, all players may control any librarian without claiming.
        ///
        /// Read from <c>AppData\LocalLow\Project Moon\LibraryOfRuina\ModConfigs\meconeko.playlorwithme.xml</c>;
        /// defaults to true.
        /// </summary>
        public bool ClaimsEnabled { get; private set; } = true;

        public static Server Instance { get; private set; }

        /// <summary>Exposes the session manager for read-only queries (e.g. lock lookups in serializer).</summary>
        internal SessionManager SessionManager => _sessionManager;

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        public void Start()
        {
            Instance = this;
            LoadConfig();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{Port}/");
            _listener.Start();
            _running = true;

            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "PlayLoRWithMe-HTTP",
            };
            _listenerThread.Start();

            Debug.Log($"[PRWM] Server listening on http://*:{Port}/");
        }

        public void Stop()
        {
            _running = false;
            _listener?.Stop();
            _listener?.Close();
        }

        /// <summary>
        /// Reads optional settings from the mod's XML config file.
        ///
        /// The file is at: <c>&lt;persistentDataPath&gt;/ModConfigs/meconeko.playlorwithme.xml</c>.
        /// Missing file or missing elements silently fall back to defaults.
        /// </summary>
        private void LoadConfig()
        {
            string path = System.IO.Path.Combine(
                Application.persistentDataPath,
                "ModConfigs",
                Initializer.packageId + ".xml"
            );

            Debug.Log($"[PRWM] Config path: {path}");

            if (!File.Exists(path))
                return;
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.Load(path);
                var root = doc.DocumentElement;

                var node = root?.SelectSingleNode("ClaimsEnabled");
                if (node != null && bool.TryParse(node.InnerText, out bool ce))
                    ClaimsEnabled = ce;

                Debug.Log($"[PRWM] Config loaded: claimsEnabled={ClaimsEnabled}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PRWM] Failed to read config.xml: {ex}");
            }
        }

        // -------------------------------------------------------------------------
        // Accept loop
        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleContext(ctx));
                }
                catch (HttpListenerException) when (!_running)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PRWM] Accept error: {ex}");
                }
            }
        }

        // -------------------------------------------------------------------------
        // Request dispatch
        // -------------------------------------------------------------------------

        private void HandleContext(HttpListenerContext ctx)
        {
            try
            {
                string path = ctx.Request.Url.AbsolutePath;
                string method = ctx.Request.HttpMethod;

                if (method == "GET" && path == "/ws")
                {
                    // Run the long-lived WebSocket connection on a dedicated background
                    // thread rather than tying up a shared ThreadPool worker for the
                    // whole session, which would starve the pool (and stall static-file
                    // requests / new accepts) as clients accumulate.
                    var wsThread = new Thread(() => HandleWebSocket(ctx))
                    {
                        IsBackground = true,
                        Name = "PRWM-WS-Recv",
                    };
                    wsThread.Start();
                    return;
                }
                else if (method == "GET")
                    ServeStaticFile(ctx, path);
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PRWM] Handler error: {ex}");
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch (Exception ex2)
                {
                    Debug.LogWarning($"[PRWM] Failed to send 500 response: {ex2}");
                }
            }
        }

        /// <summary>
        /// Serializes the current game state and pushes a per-session delta to every
        /// connected client. All sessions receive the same unfiltered state; ownership
        /// only controls interactivity on the frontend, not data visibility.
        /// </summary>
        public void Broadcast()
        {
            string json = GameStateSerializer.Serialize();
            _lastFullJson = json;
            foreach (var session in _sessionManager.GetConnectedSessions())
            {
                string msg = _deltaEngine.BuildMessage(session.SessionId, json);
                if (msg != null)
                    session.Client?.Send(msg);
            }
        }

        // Tracks whether BattleSetting claim IDs have been translated to battle unit IDs
        // for the current battle. Reset each time the battle scene activates.
        private bool _claimsTranslated = false;

        /// <summary>
        /// Resets the claims-translation flag so the next battle start triggers
        /// a fresh translation. Call this when the battle scene activates.
        /// </summary>
        public void ResetClaimsTranslation() => _claimsTranslated = false;

        /// <summary>
        /// Translates each session's BattleSetting position-indices (0, 1, 2…) to
        /// the actual <c>BattleUnitModel.id</c> values now that the battle has loaded.
        /// Runs at most once per battle; no-ops until <c>BattleObjectManager</c> has units.
        /// Must be called from the Unity main thread.
        /// </summary>
        public void TryTranslateClaimsForBattle()
        {
            if (_claimsTranslated)
                return;

            var bom = BattleObjectManager.instance;
            if (bom == null)
                return;

            var allies = bom.GetList(Faction.Player);
            if (allies == null || allies.Count == 0)
                return;

            var map = new System.Collections.Generic.Dictionary<int, int>();
            for (int i = 0; i < allies.Count; i++)
                map[i] = allies[i].id;

            _sessionManager.TranslateUnitIds(map);
            _claimsTranslated = true;
            Debug.Log($"[PRWM] Translated {map.Count} unit claim IDs for battle.");
        }

        /// <summary>
        /// Returns true (and clears the flag) if a broadcast was requested from a
        /// background thread (e.g. after claim/release or new connection). Called
        /// from the Unity main thread so the resulting serialization is safe.
        /// </summary>
        public bool ConsumePendingBroadcast()
        {
            return Interlocked.Exchange(ref _pendingBroadcast, 0) != 0;
        }

        private void HandleWebSocket(HttpListenerContext ctx)
        {
            Stream stream;
            try
            {
                stream = WebSocketCodec.PerformHandshake(ctx);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PRWM] WebSocket handshake failed: {ex.Message}");
                try
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                }
                catch (Exception ex2)
                {
                    Debug.LogWarning($"[PRWM] Failed to send 400 response: {ex2.Message}");
                }
                return;
            }

            // Look up or create a persistent session from the ?session=<token> query param.
            string sessionToken = ctx.Request.QueryString["session"];
            var session = _sessionManager.GetOrCreate(sessionToken);

            var client = new WebSocketClient(session.SessionId, stream, OnWebSocketMessage);
            _deltaEngine.AddSession(session.SessionId);
            _sessionManager.Attach(session.SessionId, client);
            Debug.Log($"[PRWM] WebSocket connected: {session.SessionId} ({session.DisplayName})");

            client.Send(BuildHelloMessage(session));

            // Send the cached last-known state as the initial snapshot. This avoids
            // accessing Unity game objects from the listener thread (not thread-safe).
            // The next Broadcast() call from the Unity main thread will follow with a
            // fresh snapshot; _pendingBroadcast ensures it fires soon even if no game
            // event occurs.
            // Use the cached last-broadcast JSON if available (fast, main-thread-safe).
            // If no broadcast has occurred yet, send a minimal loading stub instead of
            // calling Serialize() — that accesses Unity objects which aren't thread-safe.
            // The _pendingBroadcast flag below ensures a real snapshot follows promptly.
            string cachedJson = _lastFullJson ?? "{\"scene\":\"loading\"}";
            string initialMsg = _deltaEngine.BuildMessage(session.SessionId, cachedJson);
            if (initialMsg != null)
                client.Send(initialMsg);

            // Request a fresh broadcast on the next Unity tick so the new client
            // gets a real snapshot promptly even if no game event fires.
            Interlocked.Exchange(ref _pendingBroadcast, 1);

            // Blocks until the connection closes.
            client.ReceiveLoop();

            _sessionManager.Detach(session.SessionId, client);
            _deltaEngine.RemoveSession(session.SessionId);
            Debug.Log($"[PRWM] WebSocket disconnected: {session.SessionId}");
        }

        private void OnWebSocketMessage(WebSocketClient client, string json)
        {
            var r = new JsonReader(json);
            string type = r.GetString("type");
            if (type == null)
                return;

            string reqId = r.GetString("reqId");

            switch (type)
            {
                case "playCard":
                case "removeCard":
                case "confirm":
                case "selectAbnormality":
                case "selectEgo":
                    HandleWsAction(client, r, json, reqId);
                    break;

                case "claimUnit":
                    if (r.TryGetInt("unitId", out int claimUnitId))
                    {
                        bool claimed = _sessionManager.ClaimUnit(client.SessionId, claimUnitId);
                        if (claimed)
                            Interlocked.Exchange(ref _pendingBroadcast, 1);
                        SendResult(
                            client,
                            reqId,
                            claimed,
                            claimed ? null : "Unit already claimed by another player"
                        );
                    }
                    break;

                case "releaseUnit":
                    if (r.TryGetInt("unitId", out int releaseUnitId))
                    {
                        _sessionManager.ReleaseUnit(client.SessionId, releaseUnitId);
                        Interlocked.Exchange(ref _pendingBroadcast, 1);
                        SendResult(client, reqId, true, null);
                    }
                    break;

                case "rename":
                    string newName = r.GetString("name");
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        _sessionManager.RenameSession(client.SessionId, newName.Trim());
                        SendResult(client, reqId, true, null);
                    }
                    break;

                case "lockLibrarian":
                    if (
                        r.TryGetInt("floorIndex", out int lockFi)
                        && r.TryGetInt("unitIndex", out int lockUi)
                    )
                    {
                        string lockKey = LockKey(lockFi, lockUi);
                        bool locked = _sessionManager.TryLockLibrarian(lockKey, client.SessionId);
                        if (locked)
                            StateBroadcaster.Broadcast();
                        SendResult(
                            client,
                            reqId,
                            locked,
                            locked ? null : "Librarian is being edited by another player"
                        );
                    }
                    break;

                case "unlockLibrarian":
                    if (
                        r.TryGetInt("floorIndex", out int ulFi)
                        && r.TryGetInt("unitIndex", out int ulUi)
                    )
                    {
                        _sessionManager.UnlockLibrarian(LockKey(ulFi, ulUi), client.SessionId);
                        StateBroadcaster.Broadcast();
                        SendResult(client, reqId, true, null);
                    }
                    break;

                // Librarian-edit handlers touch Unity model collections
                // (BookInventoryModel, BookModel, deck/passive lists) that
                // are not thread-safe. Marshal each onto the Unity main
                // thread so concurrent receive-thread dispatches under
                // load can't enumerate-while-mutating and crash the
                // WebSocket receive loop with InvalidOperationException.
                case "renameLibrarian":
                    StateBroadcaster.RunOnMainThread(() => HandleRenameLibrarian(client, r, reqId));
                    break;

                case "equipKeyPage":
                    StateBroadcaster.RunOnMainThread(() => HandleEquipKeyPage(client, r, reqId));
                    break;

                case "unequipKeyPage":
                    StateBroadcaster.RunOnMainThread(() => HandleUnequipKeyPage(client, r, reqId));
                    break;

                case "addCardToDeck":
                    StateBroadcaster.RunOnMainThread(() => HandleAddCardToDeck(client, r, reqId));
                    break;

                case "removeCardFromDeck":
                    StateBroadcaster.RunOnMainThread(() =>
                        HandleRemoveCardFromDeck(client, r, reqId)
                    );
                    break;

                case "equipSourceBook":
                    StateBroadcaster.RunOnMainThread(() => HandleEquipSourceBook(client, r, reqId));
                    break;

                case "unequipSourceBook":
                    StateBroadcaster.RunOnMainThread(() =>
                        HandleUnequipSourceBook(client, r, reqId)
                    );
                    break;

                case "attributePassive":
                    StateBroadcaster.RunOnMainThread(() =>
                        HandleAttributePassive(client, r, reqId)
                    );
                    break;

                case "removeAttributedPassive":
                    StateBroadcaster.RunOnMainThread(() =>
                        HandleRemoveAttributedPassive(client, r, reqId)
                    );
                    break;

                case "setCustomization":
                    StateBroadcaster.RunOnMainThread(() =>
                        HandleSetCustomization(client, r, reqId)
                    );
                    break;

                case "setGifts":
                    StateBroadcaster.RunOnMainThread(() => HandleSetGifts(client, r, reqId));
                    break;

                case "resync":
                    // Client detected a missed sequence number; reset delta state and
                    // send a fresh full snapshot so the client can resync cleanly.
                    // Serialize() enumerates Unity model collections, so it must
                    // run on the main thread — defer the whole rebuild.
                    var resyncSessionId = client.SessionId;
                    var resyncClient = client;
                    StateBroadcaster.RunOnMainThread(() =>
                    {
                        _deltaEngine.RemoveSession(resyncSessionId);
                        _deltaEngine.AddSession(resyncSessionId);
                        string resyncMsg = _deltaEngine.BuildMessage(
                            resyncSessionId,
                            GameStateSerializer.Serialize()
                        );
                        if (resyncMsg != null)
                            resyncClient.Send(resyncMsg);
                    });
                    break;

                default:
                    Debug.Log($"[PRWM] Unknown WebSocket message type: {type}");
                    break;
            }
        }

        // Dispatches a game action to ActionInjector. Non-blocking: enqueues the
        // action and returns immediately; the actionResult is sent back via the
        // WebSocket on the Unity main thread when DrainQueue runs.
        private void HandleWsAction(
            WebSocketClient client,
            JsonReader r,
            string json,
            string reqId
        )
        {
            // Authorization policy:
            //   claims disabled → any session may act on any unit
            //   claims enabled  → only the session that has claimed the unit may act on it;
            //                     unclaimed units are rejected
            // Actions without a unitId (confirm, selectAbnormality) bypass this gate.
            // The reader is reused from OnWebSocketMessage; json is still needed to
            // enqueue the raw action for the Unity main thread.
            if (
                ClaimsEnabled
                && r.TryGetInt("unitId", out int unitId)
                && !_sessionManager.IsAuthorized(client.SessionId, unitId)
            )
            {
                SendResult(client, reqId, false, "Not authorized for this unit");
                return;
            }

            ActionInjector.EnqueueWithCallback(
                json,
                (ok, error) => SendResult(client, reqId, ok, error)
            );
        }

        private string BuildHelloMessage(PlayerSession session)
        {
            var w = new JsonWriter()
                .Add("type", "hello")
                .Add("sessionId", session.SessionId)
                .Add("claimsEnabled", ClaimsEnabled)
                .AddArray(
                    "assignedUnits",
                    arr =>
                    {
                        foreach (int uid in session.AssignedUnitIds)
                            arr.AddInt(uid);
                    }
                );
            // theme block is one-shot — present only when ThemeProbe has bound
            // both colours by hello-send time. Late-probe retries arrive via
            // the next state push instead.
            GameStateSerializer.WriteTheme(w);
            return w.Build();
        }

        private static string BuildActionResult(string reqId, bool ok, string error)
        {
            var w = new JsonWriter().Add("type", "actionResult").Add("reqId", reqId).Add("ok", ok);
            if (!ok && error != null)
                w.Add("error", error);
            return w.Build();
        }

        /// <summary>
        /// Sends an actionResult frame back to <paramref name="client"/> if a reqId is
        /// present. All handler call sites can use this directly instead of guarding
        /// <c>client.Send(BuildActionResult(...))</c> with <c>if (reqId != null)</c>.
        /// </summary>
        private static void SendResult(WebSocketClient client, string reqId, bool ok, string error)
        {
            if (reqId != null)
                client.Send(BuildActionResult(reqId, ok, error));
        }

        // -------------------------------------------------------------------------
        // HTTP helpers
        // -------------------------------------------------------------------------

        private static void ServeStaticFile(HttpListenerContext ctx, string urlPath)
        {
            string relative = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(relative))
                relative = "index.html";

            // Canonicalize the root and require a trailing separator on the prefix so a
            // sibling directory (e.g. "wwwroot_secret") can't satisfy a bare StartsWith
            // against "wwwroot" and escape the served folder via "..".
            string root = Path.GetFullPath(WwwRootPath);
            string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? root
                : root + Path.DirectorySeparatorChar;
            string filePath = Path.GetFullPath(Path.Combine(root, relative));

            if (!filePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                return;
            }

            if (!File.Exists(filePath))
                filePath = Path.Combine(WwwRootPath, "index.html");

            if (!File.Exists(filePath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            ctx.Response.ContentType = MimeType(Path.GetExtension(filePath));
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static string MimeType(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".html":
                    return "text/html; charset=utf-8";
                case ".js":
                case ".mjs":
                    return "application/javascript; charset=utf-8";
                case ".css":
                    return "text/css; charset=utf-8";
                case ".json":
                    return "application/json; charset=utf-8";
                case ".ico":
                    return "image/x-icon";
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".svg":
                    return "image/svg+xml";
                case ".woff":
                    return "font/woff";
                case ".woff2":
                    return "font/woff2";
                default:
                    return "application/octet-stream";
            }
        }
    }
}
