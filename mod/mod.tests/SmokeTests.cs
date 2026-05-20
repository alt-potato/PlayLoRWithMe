using PlayLoRWithMe;
using Xunit;

namespace PlayLoRWithMe.Tests
{
    /// <summary>
    /// Sanity check that the pure source files compile and run in this assembly
    /// with no game assemblies on the load path. If this builds, the decoupling
    /// from Unity/Harmony held.
    /// </summary>
    public class SmokeTests
    {
        [Fact]
        public void PureTypesAreReachableWithoutUnity()
        {
            // Touch one type from each compiled-in source file.
            Assert.NotNull(new JsonWriter().Build());
            Assert.Null(new JsonReader("{}").GetString("missing"));
            Assert.NotNull(new DeltaEngine());
            Assert.NotNull(new SessionManager());
            Assert.Equal(WebSocketCodec.Opcode.Text, WebSocketCodec.Opcode.Text);
        }
    }
}
