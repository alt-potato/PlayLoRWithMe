using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Minimal RFC 6455 WebSocket codec. Handles the HTTP->WebSocket upgrade
    /// handshake and provides frame-level read/write for text, ping, pong, and
    /// close frames. Server-to-client frames are not masked (per spec);
    /// client-to-server frames are always masked (per spec) and are unmasked here.
    /// Fragmented messages (FIN=0) are not supported — all expected client
    /// messages are small and arrive in a single frame.
    /// </summary>
    /// <seealso href="https://www.rfc-editor.org/rfc/rfc6455.html#section-1.3"/>
    internal static class WebSocketCodec
    {
        // Magic GUID from RFC 6455 §1.3, concatenated with the client key to
        // produce the Sec-WebSocket-Accept response header.
        private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        // Guard against malformed or hostile frames with unexpectedly large payloads.
        private const int MaxPayloadBytes = 1024 * 1024; // 1 MB

        /// <summary>WebSocket frame opcodes (RFC 6455 §5.2).</summary>
        public enum Opcode : byte
        {
            Continuation = 0x0,
            Text = 0x1,
            Binary = 0x2,
            Close = 0x8,
            Ping = 0x9,
            Pong = 0xA,
        }

        // -------------------------------------------------------------------------
        // Handshake
        // -------------------------------------------------------------------------

        /// <summary>
        /// Performs the RFC 6455 opening handshake on <paramref name="ctx"/> and
        /// returns the raw duplex stream to use for subsequent frame I/O.
        /// Throws <see cref="InvalidOperationException"/> if the request is not a
        /// valid WebSocket upgrade.
        /// </summary>
        /// <remarks>
        /// We bypass <see cref="HttpListenerResponse"/> entirely and write the
        /// 101 response directly to the raw TCP stream. HttpListenerResponse does
        /// not support status 101 and would prepend chunked-encoding headers that
        /// would corrupt the WebSocket framing.
        /// Do NOT touch <paramref name="ctx"/>.Response after calling this method.
        /// </remarks>
        public static Stream PerformHandshake(HttpListenerContext ctx)
        {
            var req = ctx.Request;

            if (
                !string.Equals(
                    req.Headers["Upgrade"],
                    "websocket",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new InvalidOperationException("Not a WebSocket upgrade request.");

            string key = req.Headers["Sec-WebSocket-Key"];
            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException("Missing Sec-WebSocket-Key header.");

            string acceptKey = ComputeAcceptKey(key);
            Stream stream = GetRawStream(ctx);

            // Write the 101 Switching Protocols response directly to the raw stream.
            string response =
                "HTTP/1.1 101 Switching Protocols\r\n"
                + "Upgrade: websocket\r\n"
                + "Connection: Upgrade\r\n"
                + "Sec-WebSocket-Accept: "
                + acceptKey
                + "\r\n"
                + "\r\n";

            byte[] bytes = Encoding.ASCII.GetBytes(response);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();

            return stream;
        }

        private static string ComputeAcceptKey(string clientKey)
        {
            // RFC 6455 §4.2.2: accept key = base64(SHA-1(clientKey + WsGuid))
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(clientKey + WsGuid));
                return Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// Extracts the underlying duplex <see cref="Stream"/> from a Mono
        /// <see cref="HttpListenerContext"/> via reflection.
        /// </summary>
        /// <remarks>
        /// Mono's HttpListener stores the raw socket stream in a private field
        /// <c>HttpConnection.stream</c>, reached via the context's private
        /// <c>cnc</c> field. Confirmed against the game's bundled System.dll
        /// (decompiled with ilspycmd). There is no public API to retrieve it;
        /// reflection is the only dependency-free option.
        /// </remarks>
        private static Stream GetRawStream(HttpListenerContext ctx)
        {
            var cncField = typeof(HttpListenerContext).GetField(
                "cnc",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (cncField == null)
                throw new InvalidOperationException(
                    "Cannot locate HttpListenerContext.cnc — unsupported Mono runtime version."
                );

            object cnc = cncField.GetValue(ctx);

            // The field is private and lowercase ("stream"), not a public property.
            var streamField = cnc.GetType()
                .GetField("stream", BindingFlags.NonPublic | BindingFlags.Instance);
            if (streamField == null)
                throw new InvalidOperationException(
                    "Cannot locate HttpConnection.stream — unsupported Mono runtime version."
                );

            return (Stream)streamField.GetValue(cnc);
        }

        // -------------------------------------------------------------------------
        // Frame reading
        // -------------------------------------------------------------------------

        /// <summary>
        /// Reads one complete WebSocket frame from <paramref name="stream"/>.
        /// Blocks until a full frame arrives. Returns opcode
        /// <see cref="Opcode.Close"/> with a null payload when the stream ends.
        /// </summary>
        public static (Opcode opcode, byte[] payload) ReadFrame(Stream stream)
        {
            // Byte 0: FIN flag (bit 7) + opcode (bits 0-3). RSV bits ignored.
            int b0 = stream.ReadByte();
            if (b0 < 0)
                return (Opcode.Close, null); // stream closed cleanly

            var opcode = (Opcode)(b0 & 0x0F);

            // Byte 1: MASK flag (bit 7) + 7-bit base payload length.
            int b1 = stream.ReadByte();
            if (b1 < 0)
                return (Opcode.Close, null);

            bool masked = (b1 & 0x80) != 0;
            long payloadLen = b1 & 0x7F;

            if (payloadLen == 126)
            {
                // 16-bit extended length, big-endian.
                byte[] ext = ReadExactly(stream, 2);
                payloadLen = (ext[0] << 8) | ext[1];
            }
            else if (payloadLen == 127)
            {
                // 64-bit extended length, big-endian.
                byte[] ext = ReadExactly(stream, 8);
                payloadLen = 0;
                for (int i = 0; i < 8; i++)
                    payloadLen = (payloadLen << 8) | ext[i];
            }

            if (payloadLen > MaxPayloadBytes)
                throw new InvalidDataException(
                    $"WebSocket payload too large: {payloadLen} bytes (max {MaxPayloadBytes})."
                );

            // Masking key is present only when the MASK bit is set.
            // Clients must always mask; servers must never mask.
            byte[] maskKey = masked ? ReadExactly(stream, 4) : null;

            byte[] payload = ReadExactly(stream, (int)payloadLen);

            if (masked)
            {
                for (int i = 0; i < payload.Length; i++)
                    payload[i] ^= maskKey[i % 4];
            }

            return (opcode, payload);
        }

        // -------------------------------------------------------------------------
        // Frame writing
        // -------------------------------------------------------------------------

        /// <summary>
        /// Writes a single WebSocket frame to <paramref name="stream"/>.
        /// Frames are always FIN=1 (unfragmented). Server frames are never masked
        /// per RFC 6455 §5.1.
        /// </summary>
        public static void WriteFrame(Stream stream, Opcode opcode, byte[] payload)
        {
            if (payload == null)
                payload = new byte[0];

            int len = payload.Length;

            // Header is at most 10 bytes: 1 (opcode) + 1 (len) + 8 (extended len).
            byte[] header = new byte[10];
            int headerLen = 0;

            // Byte 0: FIN=1, RSV1-3=0, opcode.
            header[headerLen++] = (byte)(0x80 | (byte)opcode);

            // Byte(s) 1+: payload length. No mask bit for server-to-client frames.
            if (len < 126)
            {
                header[headerLen++] = (byte)len;
            }
            else if (len < 65536)
            {
                header[headerLen++] = 126;
                header[headerLen++] = (byte)(len >> 8);
                header[headerLen++] = (byte)(len & 0xFF);
            }
            else
            {
                header[headerLen++] = 127;
                for (int i = 7; i >= 0; i--)
                    header[headerLen++] = (byte)(len >> (i * 8));
            }

            stream.Write(header, 0, headerLen);
            if (len > 0)
                stream.Write(payload, 0, len);
            stream.Flush();
        }

        // -------------------------------------------------------------------------
        // Convenience helpers
        // -------------------------------------------------------------------------

        /// <summary>Sends a UTF-8 text frame.</summary>
        public static void SendText(Stream stream, string text) =>
            WriteFrame(stream, Opcode.Text, Encoding.UTF8.GetBytes(text));

        /// <summary>
        /// Sends a close frame with the given status code.
        /// The caller is responsible for closing the stream afterwards.
        /// </summary>
        public static void SendClose(Stream stream, ushort statusCode = 1000)
        {
            // Close frame payload: 2-byte big-endian status code.
            byte[] payload = { (byte)(statusCode >> 8), (byte)(statusCode & 0xFF) };
            WriteFrame(stream, Opcode.Close, payload);
        }

        // -------------------------------------------------------------------------
        // Internal helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes, blocking until all arrive.
        /// Throws <see cref="EndOfStreamException"/> if the stream ends early.
        /// </summary>
        private static byte[] ReadExactly(Stream stream, int count)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buf, read, count - read);
                if (n == 0)
                    throw new EndOfStreamException(
                        $"Stream ended after {read} of {count} expected bytes."
                    );
                read += n;
            }
            return buf;
        }
    }
}
