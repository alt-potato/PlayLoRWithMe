using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PlayLoRWithMe
{
    public class Server
    {
        public const int Port = 8080;

        // Path resolution: DLL is in ModData/Assemblies/, wwwroot is in ModData/wwwroot/
        private static readonly string ModRootPath = Path.GetDirectoryName(
            Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location));

        private HttpListener _listener;
        private Thread _listenerThread;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<Guid, WebSocket> _clients =
            new ConcurrentDictionary<Guid, WebSocket>();

        public static Server Instance { get; private set; }

        public void Start()
        {
            Instance = this;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();

            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "PlayLoRWithMe-HTTP"
            };
            _listenerThread.Start();

            Debug.Log($"[PlayLoRWithMe] Server listening at http://localhost:{Port}/");
        }

        public void Stop()
        {
            _cts.Cancel();
            _listener?.Stop();
        }

        // --- Main accept loop (background thread) ---

        private void ListenLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var ctx = _listener.GetContext(); // blocks until a request arrives
                    ThreadPool.QueueUserWorkItem(_ => HandleContext(ctx));
                }
                catch (HttpListenerException) when (_cts.IsCancellationRequested)
                {
                    break; // expected on shutdown
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PlayLoRWithMe] Listener error: {ex}");
                }
            }
        }

        // --- Request dispatch ---

        private void HandleContext(HttpListenerContext ctx)
        {
            try
            {
                // Handle CORS preflight
                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    WriteCorsHeaders(ctx.Response);
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                if (ctx.Request.IsWebSocketRequest)
                {
                    _ = HandleWebSocketAsync(ctx);
                    return;
                }

                string path = ctx.Request.Url.AbsolutePath;
                string method = ctx.Request.HttpMethod;

                if (method == "GET" && (path == "/" || path == "/index.html"))
                {
                    ServeFile(ctx, "index.html", "text/html; charset=utf-8");
                }
                else if (method == "GET" && path == "/state")
                {
                    SendJson(ctx, GameStateSerializer.Serialize());
                }
                else if (method == "POST" && path == "/action")
                {
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        string body = reader.ReadToEnd();
                        ActionInjector.Enqueue(body);
                    }
                    SendJson(ctx, "{\"ok\":true}");
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayLoRWithMe] Request handler error: {ex}");
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch { /* response already started */ }
            }
        }

        // --- WebSocket ---

        private async Task HandleWebSocketAsync(HttpListenerContext ctx)
        {
            HttpListenerWebSocketContext wsCtx;
            try
            {
                wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayLoRWithMe] WebSocket upgrade failed: {ex}");
                return;
            }

            var id = Guid.NewGuid();
            var ws = wsCtx.WebSocket;
            _clients[id] = ws;
            Debug.Log($"[PlayLoRWithMe] WebSocket client connected ({id})");

            // Send current state immediately on connect
            await SendWsAsync(ws, GameStateSerializer.Serialize());

            // Hold the connection open, receiving frames until the client closes
            var buffer = new byte[4096];
            try
            {
                while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayLoRWithMe] WebSocket receive error ({id}): {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(id, out _);
                try
                {
                    await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing",
                        CancellationToken.None);
                }
                catch { }
                Debug.Log($"[PlayLoRWithMe] WebSocket client disconnected ({id})");
            }
        }

        /// <summary>
        /// Broadcasts a JSON string to every connected WebSocket client.
        /// Safe to call from any thread.
        /// </summary>
        public void Broadcast(string json)
        {
            foreach (var kvp in _clients)
            {
                if (kvp.Value.State == WebSocketState.Open)
                    _ = SendWsAsync(kvp.Value, json);
            }
        }

        private static async Task SendWsAsync(WebSocket ws, string json)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken: CancellationToken.None);
            }
            catch { /* client gone */ }
        }

        // --- HTTP helpers ---

        private static void SendJson(HttpListenerContext ctx, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            WriteCorsHeaders(ctx.Response);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static void ServeFile(HttpListenerContext ctx, string filename, string contentType)
        {
            string filePath = Path.Combine(ModRootPath, "wwwroot", filename);
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[PlayLoRWithMe] Static file not found: {filePath}");
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static void WriteCorsHeaders(HttpListenerResponse response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        }
    }
}
