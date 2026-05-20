using PlayLoRWithMe;
using Xunit;

namespace PlayLoRWithMe.Tests
{
    /// <summary>
    /// Coverage for <see cref="DeltaEngine"/>: first-message-full, changed-only
    /// diffs for fields and unit arrays, removal reporting, monotonic per-session
    /// sequence numbers, and isolation between sessions at different stream points.
    /// </summary>
    public class DeltaEngineTests
    {
        [Fact]
        public void FirstMessage_IsFullState()
        {
            var engine = new DeltaEngine();
            engine.AddSession("s1");

            string state = "{\"phase\":\"A\",\"light\":3}";
            string msg = engine.BuildMessage("s1", state);

            Assert.Contains("\"type\":\"state\"", msg);
            Assert.Contains("\"seq\":1", msg);
            Assert.Contains("\"data\":" + state, msg);
        }

        [Fact]
        public void SecondMessage_ContainsOnlyChangedFieldsAndUnits()
        {
            var engine = new DeltaEngine();
            engine.AddSession("s1");

            string first =
                "{\"phase\":\"A\",\"allies\":[{\"id\":1,\"hp\":10},{\"id\":2,\"hp\":20}],"
                + "\"enemies\":[{\"id\":5,\"hp\":50}]}";
            string second =
                "{\"phase\":\"A\",\"allies\":[{\"id\":1,\"hp\":8},{\"id\":2,\"hp\":20}],"
                + "\"enemies\":[{\"id\":5,\"hp\":50}]}";

            engine.BuildMessage("s1", first);
            string msg = engine.BuildMessage("s1", second);

            Assert.Contains("\"type\":\"delta\"", msg);
            // Only the changed ally appears.
            Assert.Contains("\"allies\":[{\"id\":1,\"hp\":8}]", msg);
            // Unchanged field, unchanged ally, and unchanged enemy array are omitted.
            Assert.DoesNotContain("phase", msg);
            Assert.DoesNotContain("\"id\":2", msg);
            Assert.DoesNotContain("enemies", msg);
        }

        [Fact]
        public void UnchangedState_ProducesNoMessage()
        {
            var engine = new DeltaEngine();
            engine.AddSession("s1");

            string state = "{\"phase\":\"A\"}";
            engine.BuildMessage("s1", state);

            Assert.Null(engine.BuildMessage("s1", state));
        }

        [Fact]
        public void RemovedUnit_IsReportedInDelta()
        {
            var engine = new DeltaEngine();
            engine.AddSession("s1");

            string first = "{\"allies\":[{\"id\":1,\"hp\":10},{\"id\":2,\"hp\":20}]}";
            string second = "{\"allies\":[{\"id\":1,\"hp\":10}]}";

            engine.BuildMessage("s1", first);
            string msg = engine.BuildMessage("s1", second);

            Assert.Contains("\"type\":\"delta\"", msg);
            Assert.Contains("\"_removed_allies\":[2]", msg);
        }

        [Fact]
        public void SequenceNumbers_IncreaseMonotonicallyPerSession()
        {
            var engine = new DeltaEngine();
            engine.AddSession("s1");

            int seq1 = SeqOf(engine.BuildMessage("s1", "{\"phase\":\"A\"}"));
            int seq2 = SeqOf(engine.BuildMessage("s1", "{\"phase\":\"B\"}"));
            int seq3 = SeqOf(engine.BuildMessage("s1", "{\"phase\":\"C\"}"));

            Assert.True(seq1 < seq2, $"{seq1} < {seq2}");
            Assert.True(seq2 < seq3, $"{seq2} < {seq3}");
        }

        [Fact]
        public void Sessions_DiffAgainstTheirOwnLastSeenState()
        {
            var engine = new DeltaEngine();
            engine.AddSession("a");
            engine.AddSession("b");

            string s1 = "{\"phase\":\"A\"}";
            string s2 = "{\"phase\":\"B\"}";
            string s3 = "{\"phase\":\"C\"}";

            // Session "a" advances to s3; "b" stays at s1.
            engine.BuildMessage("a", s1);
            engine.BuildMessage("a", s2);
            engine.BuildMessage("a", s3);

            engine.BuildMessage("b", s1);

            // "b" jumps straight to s3 — it must diff against its own last-seen (s1),
            // and its sequence counter is independent of "a"'s.
            string bMsg = engine.BuildMessage("b", s3);

            Assert.Contains("\"type\":\"delta\"", bMsg);
            Assert.Contains("\"phase\":\"C\"", bMsg);
            Assert.Equal(2, SeqOf(bMsg)); // b's own second message, not a's 4th
        }

        // Pulls the top-level "seq" out of a state/delta envelope.
        private static int SeqOf(string message)
        {
            Assert.NotNull(message);
            Assert.True(new JsonReader(message).TryGetInt("seq", out int seq));
            return seq;
        }
    }
}
