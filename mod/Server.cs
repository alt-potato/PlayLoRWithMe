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

        public static Server Instance { get; private set; }

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        public void Start()
        {
            Instance = this;
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

                if (method == "GET" && path == "/events")
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
            Debug.Log($"[PlayLoRWithMe] SSE client disconnected ({id})");
        }

        /// <summary>
        /// Pushes a JSON state snapshot to every connected SSE client.
        /// Dead clients are removed lazily on the next send.
        /// Safe to call from any thread.
        /// </summary>
        public void Broadcast(string json)
        {
            foreach (var kvp in _clients)
            {
                kvp.Value.Send(json);
                if (!kvp.Value.IsAlive)
                    _clients.TryRemove(kvp.Key, out _);
            }
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
