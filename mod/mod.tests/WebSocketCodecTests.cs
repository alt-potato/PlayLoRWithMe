using System.IO;
using System.Text;
using PlayLoRWithMe;
using Xunit;

namespace PlayLoRWithMe.Tests
{
    /// <summary>
    /// Frame-level encode/decode and handshake coverage for <see cref="WebSocketCodec"/>.
    /// Frames are exercised over an in-memory stream, so no sockets or game context
    /// are needed.
    /// </summary>
    public class WebSocketCodecTests
    {
        // Payload lengths chosen to hit every length-encoding branch:
        // 0/125 -> 7-bit inline, 126/65535 -> 16-bit extended, 65536 -> 64-bit extended.
        [Theory]
        [InlineData(0)]
        [InlineData(125)]
        [InlineData(126)]
        [InlineData(65535)]
        [InlineData(65536)]
        public void WriteFrame_ReadFrame_RoundTripsAcrossLengthBoundaries(int length)
        {
            byte[] payload = new byte[length];
            for (int i = 0; i < length; i++)
                payload[i] = (byte)(i % 251); // deterministic, spans full byte range

            using (var stream = new MemoryStream())
            {
                WebSocketCodec.WriteFrame(stream, WebSocketCodec.Opcode.Binary, payload);
                stream.Position = 0;

                var (opcode, decoded) = WebSocketCodec.ReadFrame(stream);

                Assert.Equal(WebSocketCodec.Opcode.Binary, opcode);
                Assert.Equal(payload, decoded);
            }
        }

        [Fact]
        public void ReadFrame_UnmasksClientFrame()
        {
            byte[] plaintext = Encoding.UTF8.GetBytes("hello");
            byte[] maskKey = { 0x12, 0x34, 0x56, 0x78 };

            using (var stream = new MemoryStream())
            {
                // Hand-build a masked client text frame (RFC 6455 §5.2).
                stream.WriteByte(0x80 | (byte)WebSocketCodec.Opcode.Text); // FIN + Text
                stream.WriteByte((byte)(0x80 | plaintext.Length)); // MASK bit + 7-bit length
                stream.Write(maskKey, 0, 4);
                for (int i = 0; i < plaintext.Length; i++)
                    stream.WriteByte((byte)(plaintext[i] ^ maskKey[i % 4]));
                stream.Position = 0;

                var (opcode, decoded) = WebSocketCodec.ReadFrame(stream);

                Assert.Equal(WebSocketCodec.Opcode.Text, opcode);
                Assert.Equal(plaintext, decoded);
                Assert.Equal("hello", Encoding.UTF8.GetString(decoded));
            }
        }

        [Fact]
        public void SendText_ProducesTextFrameWithUtf8Payload()
        {
            using (var stream = new MemoryStream())
            {
                WebSocketCodec.SendText(stream, "pingé"); // includes a multi-byte char
                stream.Position = 0;

                var (opcode, decoded) = WebSocketCodec.ReadFrame(stream);

                Assert.Equal(WebSocketCodec.Opcode.Text, opcode);
                Assert.Equal("pingé", Encoding.UTF8.GetString(decoded));
            }
        }

        [Fact]
        public void SendClose_ProducesCloseFrameWithStatusCode()
        {
            const ushort status = 1000;
            using (var stream = new MemoryStream())
            {
                WebSocketCodec.SendClose(stream, status);
                stream.Position = 0;

                var (opcode, decoded) = WebSocketCodec.ReadFrame(stream);

                Assert.Equal(WebSocketCodec.Opcode.Close, opcode);
                Assert.Equal(2, decoded.Length);
                // Big-endian status code per RFC 6455 §5.5.1.
                int code = (decoded[0] << 8) | decoded[1];
                Assert.Equal(status, code);
            }
        }

        [Fact]
        public void ComputeAcceptKey_MatchesRfc6455CanonicalExample()
        {
            // RFC 6455 §1.3 worked example.
            string accept = WebSocketCodec.ComputeAcceptKey("dGhlIHNhbXBsZSBub25jZQ==");
            Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", accept);
        }
    }
}
