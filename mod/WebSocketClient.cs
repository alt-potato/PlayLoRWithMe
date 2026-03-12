using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

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
        public bool IsAlive => !_closed;

        private readonly Stream _stream;

        // Called on the receive-loop thread for every incoming text frame that is
        // not an application-level pong (those are consumed here to reset the timer).
        private readonly Action<WebSocketClient, string> _onMessage;

        private readonly object _sendLock = new object();
        private readonly Timer _pingTimer;

        // volatile so IsAlive reads on other threads see the current value
        // without a full memory barrier.
        private volatile bool _closed = false;

        private DateTime _lastPongTime = DateTime.UtcNow;

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
        }

        // -------------------------------------------------------------------------
        // Sending
        // -------------------------------------------------------------------------

        /// <summary>
        /// Sends a UTF-8 JSON text frame. Thread-safe; no-op if the client is dead.
        /// </summary>
        public void Send(string json)
        {
            if (_closed)
                return;
            try
            {
                lock (_sendLock)
                    WebSocketCodec.SendText(_stream, json);
            }
            catch
            {
                Close();
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
                while (!_closed)
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
                            _lastPongTime = DateTime.UtcNow;
                            break;

                        case WebSocketCodec.Opcode.Close:
                            Close();
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_closed)
                    Debug.LogWarning(
                        $"[PlayLoRWithMe] WebSocket receive error ({SessionId}): {ex.Message}"
                    );
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
            if (_closed)
                return;

            // If pong hasn't been heard in time, the connection is stale.
            if (DateTime.UtcNow - _lastPongTime > PongTimeout)
            {
                Debug.Log($"[PlayLoRWithMe] WebSocket ping timeout ({SessionId}), closing.");
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
            catch
            {
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
                _lastPongTime = DateTime.UtcNow;
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
            if (_closed)
                return;
            _closed = true;

            _pingTimer?.Dispose();

            try
            {
                lock (_sendLock)
                    WebSocketCodec.SendClose(_stream);
            }
            catch { }

            try
            {
                _stream.Close();
            }
            catch { }
        }
    }
}
