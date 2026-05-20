using PlayLoRWithMe;
using Xunit;

namespace PlayLoRWithMe.Tests
{
    /// <summary>
    /// Coverage for the hand-rolled <see cref="JsonWriter"/> / <see cref="JsonReader"/>
    /// pair: escaping, structural balance, Build() idempotency, and writer->reader
    /// value recovery.
    /// </summary>
    public class JsonTests
    {
        [Fact]
        public void Writer_EscapesSpecialCharacters()
        {
            // Quote, backslash, newline, tab, carriage return, and a sub-0x20 control char.
            string raw = "a\"b\\c\nd\te\rfg";
            string json = new JsonWriter().Add("k", raw).Build();

            Assert.Equal("{\"k\":\"a\\\"b\\\\c\\nd\\te\\rf\\u0001g\"}", json);
        }

        [Fact]
        public void Writer_EscapedString_RoundTripsThroughReader()
        {
            string raw = "quote:\" backslash:\\ newline:\n tab:\t";
            string json = new JsonWriter().Add("text", raw).Build();

            Assert.Equal(raw, new JsonReader(json).GetString("text"));
        }

        [Fact]
        public void Writer_NestedObjectsAndArrays_AreBalancedAndWellFormed()
        {
            string json = new JsonWriter()
                .Add("type", "state")
                .AddObject("inner", o => o.Add("n", 7).Add("flag", true))
                .AddArray("ids", a => a.AddInt(1).AddInt(2).AddInt(3))
                .Build();

            Assert.Equal(
                "{\"type\":\"state\",\"inner\":{\"n\":7,\"flag\":true},\"ids\":[1,2,3]}",
                json
            );
            Assert.True(IsBalanced(json), "braces/brackets should be balanced");
        }

        [Fact]
        public void Build_IsIdempotent()
        {
            var writer = new JsonWriter().Add("a", 1).Add("b", "two");

            string first = writer.Build();
            string second = writer.Build();

            Assert.Equal(first, second);
            // Exactly one closing brace — Close() must be guarded against re-running.
            Assert.Equal('}', first[first.Length - 1]);
            Assert.NotEqual("}}", first.Substring(first.Length - 2));
        }

        [Fact]
        public void WriterToReader_RecoversStringAndIntValues()
        {
            string json = new JsonWriter()
                .Add("type", "playCard")
                .Add("unitId", 42)
                .Build();

            var reader = new JsonReader(json);
            Assert.Equal("playCard", reader.GetString("type"));
            Assert.True(reader.TryGetInt("unitId", out int unitId));
            Assert.Equal(42, unitId);
            Assert.False(reader.TryGetInt("type", out _)); // non-numeric value
            Assert.Null(reader.GetString("absent"));
        }

        // Verifies brace/bracket nesting balance, ignoring delimiters inside strings.
        private static bool IsBalanced(string json)
        {
            int depth = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (c == '\\')
                        i++; // skip escaped char
                    else if (c == '"')
                        inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                    case '[':
                        depth++;
                        break;
                    case '}':
                    case ']':
                        depth--;
                        if (depth < 0)
                            return false;
                        break;
                }
            }
            return depth == 0 && !inString;
        }
    }
}
