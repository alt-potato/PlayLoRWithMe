using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Wraps a raw WebSocket <see cref="Stream"/> (obtained from
    /// <see cref="WebSocketCodec"/>) with thread-safe sending, a blocking receive
    /// loop, and an application-level ping/pong keepalive. One instance is created
    /// per connected browser tab.
    /// </summary>
    internal sealed class WebSocketClient
    {
        // How often the server sends an application-level {"type":"ping"} frame.
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);

        // How long since the last pong before declaring the client dead.
        // Must be > PingInterval to allow at least one round-trip.
        private static readonly TimeSpan PongTimeout = TimeSpan.FromSeconds(30);

        public string SessionId { get; }
        public bool IsAlive => _closed == 0;

        private readonly Stream _stream;

        // Called on the receive-loop thread for every incoming text frame that is
        // not an application-level pong (those are consumed here to reset the timer).
        private readonly Action<WebSocketClient, string> _onMessage;

        private readonly object _sendLock = new object();
        private readonly Timer _pingTimer;

        // Outbound text frames are written by a single dedicated thread so callers
        // (notably the Unity main thread driving broadcasts) never block on socket
        // I/O. A slow or half-dead client can only back this queue up; it can no
        // longer stall the game loop. The dedicated writer also keeps frame order.
        private readonly BlockingCollection<string> _sendQueue =
            new BlockingCollection<string>();
        private readonly Thread _sendThread;

        // Interlocked.CompareExchange provides an atomic check-and-set in Close(),
        // preventing two threads from both sending close frames / disposing the stream.
        private int _closed = 0;

        // DateTime is an 8-byte struct — not guaranteed atomic on 32-bit runtimes.
        // Store as ticks (long) and use Interlocked for tear-free access.
        private long _lastPongTicks = DateTime.UtcNow.Ticks;

        /// <param name="sessionId">Session ID this connection belongs to.</param>
        /// <param name="stream">Raw duplex stream from <see cref="WebSocketCodec.PerformHandshake"/>.</param>
        /// <param name="onMessage">
        /// Invoked on the receive-loop thread for each incoming text frame except
        /// application-level pongs, which are handled internally.
        /// </param>
        public WebSocketClient(
            string sessionId,
            Stream stream,
            Action<WebSocketClient, string> onMessage
        )
        {
            SessionId = sessionId;
            _stream = stream;
            _onMessage = onMessage;
            _pingTimer = new Timer(_ => SendPing(), null, PingInterval, PingInterval);
            _sendThread = new Thread(SendLoop)
            {
                IsBackground = true,
                Name = $"PRWM-WS-Send-{sessionId}",
            };
            _sendThread.Start();
        }

        // -------------------------------------------------------------------------
        // Sending
        // -------------------------------------------------------------------------

        /// <summary>
        /// Queues a UTF-8 JSON text frame for the dedicated writer thread. Thread-safe,
        /// non-blocking, and a no-op if the client is dead. The actual socket write
        /// happens off the calling thread in <see cref="SendLoop"/>.
        /// </summary>
        public void Send(string json)
        {
            if (_closed != 0)
                return;
            try
            {
                _sendQueue.Add(json);
            }
            catch (InvalidOperationException)
            {
                // CompleteAdding ran concurrently (client closing) — drop the frame.
            }
        }

        /// <summary>
        /// Drains the outbound queue on the dedicated writer thread, writing each frame
        /// under the send lock so it can't interleave with ping/pong/close frames. Ends
        /// when <see cref="Close"/> calls CompleteAdding and the queue empties.
        /// </summary>
        private void SendLoop()
        {
            try
            {
                foreach (string json in _sendQueue.GetConsumingEnumerable())
                {
                    if (_closed != 0)
                        break;
                    try
                    {
                        lock (_sendLock)
                            WebSocketCodec.SendText(_stream, json);
                    }
                    catch (Exception ex)
                    {
                        ModLog.Warn(
                            $"[PRWM] WebSocket send failed for {SessionId}: {ex.Message}"
                        );
                        Close();
                        break;
                    }
                }
            }
            catch (Exception)
            {
                // GetConsumingEnumerable can throw if the collection is completed/disposed
                // mid-enumeration during Close — nothing left to send, so just exit.
            }
        }

        // -------------------------------------------------------------------------
        // Receive loop
        // -------------------------------------------------------------------------

        /// <summary>
        /// Blocks on the stream, dispatching incoming frames until the connection
        /// closes or an unrecoverable error occurs. Run this on a dedicated thread;
        /// it returns only when the WebSocket session ends.
        /// </summary>
        public void ReceiveLoop()
        {
            try
            {
                while (_closed == 0)
                {
                    var (opcode, payload) = WebSocketCodec.ReadFrame(_stream);

                    switch (opcode)
                    {
                        case WebSocketCodec.Opcode.Text:
                            HandleTextFrame(payload);
                            break;

                        // RFC 6455 §5.5.2: must echo payload back as a pong.
                        case WebSocketCodec.Opcode.Ping:
                            lock (_sendLock)
                                WebSocketCodec.WriteFrame(
                                    _stream,
                                    WebSocketCodec.Opcode.Pong,
                                    payload
                                );
                            break;

                        // Protocol-level pong: counts as activity.
                        case WebSocketCodec.Opcode.Pong:
                            Interlocked.Exchange(ref _lastPongTicks, DateTime.UtcNow.Ticks);
                            break;

                        case WebSocketCodec.Opcode.Close:
                            Close();
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                if (_closed == 0)
                    ModLog.Warn($"[PRWM] WebSocket receive error ({SessionId}): {ex.Message}");
            }
            finally
            {
                Close();
            }
        }

        // -------------------------------------------------------------------------
        // Keepalive
        // -------------------------------------------------------------------------

        private void SendPing()
        {
            if (_closed != 0)
                return;

            // If pong hasn't been heard in time, the connection is stale.
            var lastPong = new DateTime(Interlocked.Read(ref _lastPongTicks), DateTimeKind.Utc);
            if (DateTime.UtcNow - lastPong > PongTimeout)
            {
                ModLog.Info($"[PRWM] WebSocket ping timeout ({SessionId}), closing.");
                Close();
                return;
            }

            try
            {
                // Protocol-level ping frame (opcode 0x9): browsers respond automatically
                // with a pong frame (opcode 0xA) without any frontend JavaScript needed.
                // This is preferable to application-level JSON ping/pong because it works
                // before the frontend composable (Batch 8) is implemented.
                lock (_sendLock)
                    WebSocketCodec.WriteFrame(_stream, WebSocketCodec.Opcode.Ping, null);
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[PRWM] WebSocket ping failed for {SessionId}: {ex.Message}");
                Close();
            }
        }

        // -------------------------------------------------------------------------
        // Text frame dispatch
        // -------------------------------------------------------------------------

        private void HandleTextFrame(byte[] payload)
        {
            string json = Encoding.UTF8.GetString(payload);

            // Application-level pongs are consumed here and never forwarded.
            // Using JsonReader keeps this consistent with the rest of the codebase.
            if (new JsonReader(json).GetString("type") == "pong")
            {
                Interlocked.Exchange(ref _lastPongTicks, DateTime.UtcNow.Ticks);
                return;
            }

            _onMessage?.Invoke(this, json);
        }

        // -------------------------------------------------------------------------
        // Cleanup
        // -------------------------------------------------------------------------

        /// <summary>
        /// Sends a close frame and disposes the stream and ping timer.
        /// Safe to call from any thread; subsequent calls are no-ops.
        /// </summary>
        public void Close()
        {
            // Atomic check-and-set: only the first caller proceeds.
            if (Interlocked.CompareExchange(ref _closed, 1, 0) != 0)
                return;

            // Stop accepting new frames and let the writer thread drain and exit.
            _sendQueue.CompleteAdding();
            _pingTimer?.Dispose();

            try
            {
                lock (_sendLock)
                    WebSocketCodec.SendClose(_stream);
            }
            catch (System.Exception ex)
            {
                ModLog.Warn($"[PRWM] WebSocket close-frame send failed: {ex.Message}");
            }

            try
            {
                _stream.Close();
            }
            catch (System.Exception ex)
            {
                ModLog.Warn($"[PRWM] WebSocket stream close failed: {ex.Message}");
            }
        }
    }
}
