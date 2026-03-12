using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace PlayLoRWithMe
{
    public class Server
    {
        public const int Port = 8080;

        // DLL is in <mod root>/Assemblies/; wwwroot is a sibling of Assemblies/
        private static readonly string ModRootPath = Path.GetDirectoryName(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
        );

        internal static readonly string WwwRootPath = Path.Combine(ModRootPath, "wwwroot");

        private HttpListener _listener;
        private Thread _listenerThread;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<Guid, SseClient> _clients =
            new ConcurrentDictionary<Guid, SseClient>();

        private readonly SessionManager _sessionManager = new SessionManager();
        private readonly DeltaEngine _deltaEngine = new DeltaEngine();

        // Last full (unfiltered) state snapshot, updated on the Unity main thread
        // by Broadcast(). Used to give new WebSocket clients an immediate initial
        // state without accessing Unity objects from the listener thread.
        private volatile string _lastFullJson = null;

        // Set by claim/release handlers (listener thread) to request a filtered
        // broadcast from the Unity main thread on the next OnUpdate tick.
        private volatile bool _pendingBroadcast = false;

        /// <summary>
        /// When false, all players may control any librarian without claiming.
        ///
        /// Read from <c>AppData\LocalLow\Project Moon\LibraryOfRuina\ModConfigs\meconeko.playlorwithme.xml</c>;
        /// defaults to true.
        /// </summary>
        public bool ClaimsEnabled { get; private set; } = true;

        public static Server Instance { get; private set; }

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

            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "PlayLoRWithMe-HTTP",
            };
            _listenerThread.Start();

            Debug.Log($"[PlayLoRWithMe] Server listening on http://*:{Port}/");
        }

        public void Stop()
        {
            _cts.Cancel();
            _listener?.Stop();
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

            Debug.Log($"[PlayLoRWithMe] Config path: {path}");

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

                Debug.Log($"[PlayLoRWithMe] Config loaded: claimsEnabled={ClaimsEnabled}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayLoRWithMe] Failed to read config.xml: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------------
        // Accept loop
        // -------------------------------------------------------------------------

        private void ListenLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleContext(ctx));
                }
                catch (HttpListenerException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PlayLoRWithMe] Accept error: {ex}");
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
                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    WriteCorsHeaders(ctx.Response);
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                string path = ctx.Request.Url.AbsolutePath;
                string method = ctx.Request.HttpMethod;

                if (method == "GET" && path == "/ws")
                    HandleWebSocket(ctx); // blocks until client disconnects
                else if (method == "GET" && path == "/events")
                    HandleEvents(ctx); // blocks until client disconnects
                else if (method == "GET" && path == "/state")
                    SendJson(ctx, GameStateSerializer.Serialize());
                else if (method == "POST" && path == "/action")
                {
                    string body;
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                        body = reader.ReadToEnd();
                    var (ok, error) = ActionInjector.EnqueueAndWait(body);
                    if (ok)
                        SendJson(ctx, "{\"ok\":true}");
                    else
                    {
                        ctx.Response.StatusCode = 400;
                        string safe = (error ?? "invalid action")
                            .Replace("\\", "\\\\")
                            .Replace("\"", "\\\"");
                        SendJson(ctx, "{\"ok\":false,\"error\":\"" + safe + "\"}");
                    }
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
                Debug.LogError($"[PlayLoRWithMe] Handler error: {ex}");
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch { }
            }
        }

        // -------------------------------------------------------------------------
        // Server-Sent Events
        // -------------------------------------------------------------------------

        private void HandleEvents(HttpListenerContext ctx)
        {
            var id = Guid.NewGuid();
            var rsp = ctx.Response;

            WriteCorsHeaders(rsp);
            rsp.ContentType = "text/event-stream; charset=utf-8";
            rsp.SendChunked = true;
            rsp.Headers.Add("Cache-Control", "no-cache");
            rsp.Headers.Add("X-Accel-Buffering", "no");

            var client = new SseClient(id, rsp);
            _clients[id] = client;
            Debug.Log($"[PlayLoRWithMe] SSE client connected ({id})");

            client.Send(GameStateSerializer.Serialize());

            // Hold the thread open, sending a keepalive comment every 15 s.
            // WaitOne returns true when the token is cancelled, false on timeout.
            while (!_cts.IsCancellationRequested && client.IsAlive)
            {
                bool cancelled = _cts.Token.WaitHandle.WaitOne(millisecondsTimeout: 15_000);
                if (cancelled)
                    break;
                client.SendKeepAlive();
            }

            _clients.TryRemove(id, out _);
            try
            {
                rsp.Close();
            }
            catch { }
        }

        public void Broadcast(string json)
        {
            _lastFullJson = json;

            foreach (var kvp in _clients)
            {
                kvp.Value.Send(json);
                if (!kvp.Value.IsAlive)
                    _clients.TryRemove(kvp.Key, out _);
            }

            BroadcastFiltered();
        }

        public void BroadcastFiltered()
        {
            // All sessions receive the full unfiltered state; ownership only
            // controls interactivity on the frontend, not data visibility.
            string json = GameStateSerializer.Serialize();
            foreach (var session in _sessionManager.GetConnectedSessions())
            {
                string msg = _deltaEngine.BuildMessage(session.SessionId, json);
                if (msg != null)
                    session.Client?.Send(msg);
            }
        }

        /// <summary>
        /// Returns true (and clears the flag) if a broadcast was requested from a
        /// background thread (e.g. after claim/release or new connection). Called
        /// from the Unity main thread so the resulting serialization is safe.
        /// </summary>
        public bool ConsumePendingBroadcast()
        {
            if (!_pendingBroadcast)
                return false;
            _pendingBroadcast = false;
            return true;
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
                Debug.LogWarning($"[PlayLoRWithMe] WebSocket handshake failed: {ex.Message}");
                try
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                }
                catch { }
                return;
            }

            // Look up or create a persistent session from the ?session=<token> query param.
            string sessionToken = ctx.Request.QueryString["session"];
            var session = _sessionManager.GetOrCreate(sessionToken);

            var client = new WebSocketClient(session.SessionId, stream, OnWebSocketMessage);
            _deltaEngine.AddSession(session.SessionId);
            _sessionManager.Attach(session.SessionId, client);
            Debug.Log(
                $"[PlayLoRWithMe] WebSocket connected: {session.SessionId} ({session.DisplayName})"
            );

            client.Send(BuildHelloMessage(session));

            // Send the cached last-known state as the initial snapshot. This avoids
            // accessing Unity game objects from the listener thread (not thread-safe).
            // The next Broadcast() call from the Unity main thread will follow with a
            // properly per-session filtered view; _pendingBroadcast ensures it fires
            // soon even if no game event occurs.
            // Use the cached last-broadcast JSON if available (fast, main-thread-safe).
            // Fall back to serializing immediately if the cache is cold (first ever
            // connection before any game event has fired a broadcast).
            string cachedJson = _lastFullJson ?? GameStateSerializer.Serialize();
            string initialMsg = _deltaEngine.BuildMessage(session.SessionId, cachedJson);
            if (initialMsg != null)
                client.Send(initialMsg);

            // Request a fresh filtered broadcast on the next Unity tick so any
            // ownership-based data is sent promptly even if no game event fires.
            _pendingBroadcast = true;

            // Blocks until the connection closes.
            client.ReceiveLoop();

            _sessionManager.Detach(session.SessionId);
            _deltaEngine.RemoveSession(session.SessionId);
            Debug.Log($"[PlayLoRWithMe] WebSocket disconnected: {session.SessionId}");
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
                    HandleWsAction(client, json, reqId);
                    break;

                case "claimUnit":
                    if (r.TryGetInt("unitId", out int claimUnitId))
                    {
                        bool claimed = _sessionManager.ClaimUnit(client.SessionId, claimUnitId);
                        if (claimed)
                            _pendingBroadcast = true;
                        if (reqId != null)
                            client.Send(
                                BuildActionResult(
                                    reqId,
                                    claimed,
                                    claimed ? null : "Unit already claimed by another player"
                                )
                            );
                    }
                    break;

                case "releaseUnit":
                    if (r.TryGetInt("unitId", out int releaseUnitId))
                    {
                        _sessionManager.ReleaseUnit(client.SessionId, releaseUnitId);
                        _pendingBroadcast = true;
                        if (reqId != null)
                            client.Send(BuildActionResult(reqId, true, null));
                    }
                    break;

                case "resync":
                    // Client detected a missed sequence number; reset delta state and
                    // send a fresh full snapshot so the client can resync cleanly.
                    _deltaEngine.RemoveSession(client.SessionId);
                    _deltaEngine.AddSession(client.SessionId);
                    string resyncMsg = _deltaEngine.BuildMessage(
                        client.SessionId,
                        GameStateSerializer.Serialize()
                    );
                    if (resyncMsg != null)
                        client.Send(resyncMsg);
                    break;
            }
        }

        // Dispatches a game action to ActionInjector. Non-blocking: enqueues the
        // action and returns immediately; the actionResult is sent back via the
        // WebSocket on the Unity main thread when DrainQueue runs.
        private void HandleWsAction(WebSocketClient client, string json, string reqId)
        {
            // Unit-targeted actions require the session to own (or have unclaimed access to)
            // the unit. confirm and selectAbnormality have no unitId and are always allowed.
            var r = new JsonReader(json);
            if (
                ClaimsEnabled
                && r.TryGetInt("unitId", out int unitId)
                && !_sessionManager.IsAuthorized(client.SessionId, unitId)
            )
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Not authorized for this unit"));
                return;
            }

            ActionInjector.EnqueueWithCallback(
                json,
                (ok, error) =>
                {
                    if (reqId != null)
                        client.Send(BuildActionResult(reqId, ok, error));
                }
            );
        }

        private string BuildHelloMessage(PlayerSession session)
        {
            return new JsonWriter()
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
                )
                .Build();
        }

        // Wraps the raw game-state JSON object in the {type, seq, data} envelope
        // expected by the frontend. seq=0 is used for targeted sends (hello, resync)
        // where the client should not validate sequence continuity.
        private static string WrapStateMessage(string stateJson, int seq) =>
            "{\"type\":\"state\",\"seq\":" + seq + ",\"data\":" + stateJson + "}";

        private static string BuildActionResult(string reqId, bool ok, string error)
        {
            var w = new JsonWriter().Add("type", "actionResult").Add("reqId", reqId).Add("ok", ok);
            if (!ok && error != null)
                w.Add("error", error);
            return w.Build();
        }

        // -------------------------------------------------------------------------
        // SSE client wrapper
        // -------------------------------------------------------------------------

        private sealed class SseClient
        {
            public readonly Guid Id;
            public bool IsAlive { get; private set; } = true;

            private readonly HttpListenerResponse _response;
            private readonly object _lock = new object();

            public SseClient(Guid id, HttpListenerResponse response)
            {
                Id = id;
                _response = response;
            }

            public void Send(string json) => Write(Encoding.UTF8.GetBytes($"data: {json}\n\n"));

            public void SendKeepAlive() => Write(Encoding.UTF8.GetBytes(":\n\n"));

            private void Write(byte[] bytes)
            {
                if (!IsAlive)
                    return;
                try
                {
                    lock (_lock)
                    {
                        _response.OutputStream.Write(bytes, 0, bytes.Length);
                        _response.OutputStream.Flush();
                    }
                }
                catch
                {
                    IsAlive = false;
                }
            }
        }

        // -------------------------------------------------------------------------
        // HTTP helpers
        // -------------------------------------------------------------------------

        private static void SendJson(HttpListenerContext ctx, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            WriteCorsHeaders(ctx.Response);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static void ServeStaticFile(HttpListenerContext ctx, string urlPath)
        {
            string relative = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(relative))
                relative = "index.html";

            string filePath = Path.GetFullPath(Path.Combine(WwwRootPath, relative));

            if (!filePath.StartsWith(WwwRootPath, StringComparison.OrdinalIgnoreCase))
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

        private static void WriteCorsHeaders(HttpListenerResponse response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        }
    }
}
