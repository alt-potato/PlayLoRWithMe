using PlayLoRWithMe;
using Xunit;

namespace PlayLoRWithMe.Tests
{
    /// <summary>
    /// Coverage for <see cref="StoryLog"/> — the two pure text helpers that normalize
    /// captured cutscene dialogue, plus the store's append/clear/emit behaviour.
    /// </summary>
    /// <remarks>
    /// The store is static process-wide state, so every test clears it first rather than
    /// relying on xUnit's per-class isolation (which does not extend to statics shared
    /// across collections).
    /// </remarks>
    public class StoryLogTests
    {
        public StoryLogTests() => StoryLog.Clear();

        // ── StripRichText ────────────────────────────────────────────────────────

        [Fact]
        public void StripRichText_RemovesColourMarkup()
        {
            Assert.Equal("Stop.", StoryLog.StripRichText("<color=#ff0000>Stop.</color>"));
        }

        [Fact]
        public void StripRichText_RemovesNestedMarkup()
        {
            Assert.Equal(
                "Do not follow me.",
                StoryLog.StripRichText("<b><size=36>Do not follow me.</size></b>")
            );
        }

        [Fact]
        public void StripRichText_PreservesComparisonBrackets()
        {
            // No closing bracket at all, so nothing here can be a tag.
            Assert.Equal("5 < 7", StoryLog.StripRichText("5 < 7"));
        }

        [Fact]
        public void StripRichText_PreservesProseSpanningBothBrackets()
        {
            // A closing bracket exists, but the span between contains whitespace, which no
            // Unity markup tag does — so it is prose, not a tag.
            Assert.Equal("a < b and c > d", StoryLog.StripRichText("a < b and c > d"));
        }

        [Fact]
        public void StripRichText_ToleratesUnclosedTagWithoutLosingRemainder()
        {
            Assert.Equal(
                "she said <color=#fff",
                StoryLog.StripRichText("she said <color=#fff")
            );
        }

        [Fact]
        public void StripRichText_PreservesEmbeddedNewlines()
        {
            Assert.Equal("one\ntwo", StoryLog.StripRichText("<b>one\ntwo</b>"));
        }

        [Fact]
        public void StripRichText_TreatsEmptyBracketPairAsProse()
        {
            Assert.Equal("<>", StoryLog.StripRichText("<>"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void StripRichText_ReturnsEmptyForNullOrEmpty(string input)
        {
            Assert.Equal(string.Empty, StoryLog.StripRichText(input));
        }

        [Fact]
        public void StripRichText_ReturnsInputUnchangedWhenNoMarkupPresent()
        {
            const string plain = "Nothing to strip here.";
            Assert.Same(plain, StoryLog.StripRichText(plain));
        }

        // ── SlugifyPortraitKey ───────────────────────────────────────────────────

        [Fact]
        public void SlugifyPortraitKey_KeepsAsciiStemAndAppendsHash()
        {
            string slug = StoryLog.SlugifyPortraitKey("Roland");

            Assert.StartsWith("Roland_", slug);
            Assert.Equal("Roland_".Length + 8, slug.Length);
        }

        [Fact]
        public void SlugifyPortraitKey_ProducesOnlyUrlSafeCharacters()
        {
            string slug = StoryLog.SlugifyPortraitKey("로 랜드/앙헬라.png");

            Assert.All(
                slug,
                c =>
                    Assert.True(
                        (c >= 'A' && c <= 'Z')
                            || (c >= 'a' && c <= 'z')
                            || (c >= '0' && c <= '9')
                            || c == '_'
                            || c == '-',
                        $"unsafe character '{c}' in slug '{slug}'"
                    )
            );
        }

        [Fact]
        public void SlugifyPortraitKey_IsDeterministic()
        {
            Assert.Equal(
                StoryLog.SlugifyPortraitKey("앙헬라"),
                StoryLog.SlugifyPortraitKey("앙헬라")
            );
        }

        [Fact]
        public void SlugifyPortraitKey_DistinguishesKeysThatSanitizeIdentically()
        {
            // Both stems sanitize to "___" — only the hash suffix keeps them apart, which
            // is the whole reason it exists.
            Assert.NotEqual(
                StoryLog.SlugifyPortraitKey("앙헬라"),
                StoryLog.SlugifyPortraitKey("로랜드")
            );
        }

        [Fact]
        public void SlugifyPortraitKey_BoundsStemLengthForOverlongKeys()
        {
            string slug = StoryLog.SlugifyPortraitKey(new string('a', 500));

            // 48-char stem cap + '_' + 8 hex digits.
            Assert.Equal(48 + 9, slug.Length);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void SlugifyPortraitKey_ReturnsNullForNullOrEmpty(string input)
        {
            Assert.Null(StoryLog.SlugifyPortraitKey(input));
        }

        // ── Store behaviour ──────────────────────────────────────────────────────

        [Fact]
        public void Store_StartsEmpty()
        {
            Assert.True(StoryLog.IsEmpty);
        }

        [Fact]
        public void Clear_EmptiesAppendedEntries()
        {
            StoryLog.Append("Roland", "Ex-Grade 1 Fixer", "Hello.", "roland_0");
            Assert.False(StoryLog.IsEmpty);

            StoryLog.Clear();

            Assert.True(StoryLog.IsEmpty);
        }

        [Fact]
        public void WriteTo_EmitsNothingWhenEmpty()
        {
            string json = WriteState();

            Assert.Equal("{\"scene\":\"story\"}", json);
        }

        [Fact]
        public void WriteTo_EmitsFullDialogueEntry()
        {
            StoryLog.Append("Roland", "Ex-Grade 1 Fixer", "Hello.", "roland_0");

            Assert.Contains(
                "\"storyLog\":[{\"content\":\"Hello.\",\"teller\":\"Roland\","
                    + "\"title\":\"Ex-Grade 1 Fixer\",\"portrait\":\"roland_0\"}]",
                WriteState()
            );
        }

        [Fact]
        public void WriteTo_OmitsAbsentOptionalFields()
        {
            StoryLog.Append("Monologue", null, "A quiet moment.", null);

            string json = WriteState();

            Assert.Contains("\"teller\":\"Monologue\"", json);
            Assert.DoesNotContain("\"title\"", json);
            Assert.DoesNotContain("\"portrait\"", json);
            Assert.DoesNotContain("\"isChoice\"", json);
        }

        [Fact]
        public void WriteTo_EmitsChoiceFlagsOnlyForChoiceRows()
        {
            StoryLog.AppendChoice("Forgive", isRed: false);

            string json = WriteState();

            Assert.Contains("\"content\":\"Forgive\"", json);
            Assert.Contains("\"isChoice\":true", json);
            Assert.Contains("\"choiceIsRed\":false", json);
            Assert.DoesNotContain("\"teller\"", json);
        }

        [Fact]
        public void WriteTo_PreservesAppendOrder()
        {
            StoryLog.Append("A", null, "first", null);
            StoryLog.AppendChoice("second", isRed: true);
            StoryLog.Append("B", null, "third", null);

            string json = WriteState();

            Assert.True(
                json.IndexOf("first") < json.IndexOf("second")
                    && json.IndexOf("second") < json.IndexOf("third"),
                $"entries out of order: {json}"
            );
        }

        [Fact]
        public void Append_NormalizesContentMarkup()
        {
            StoryLog.Append("Roland", null, "<b>Enough.</b>", null);

            Assert.Contains("\"content\":\"Enough.\"", WriteState());
        }

        [Fact]
        public void Append_EscapesNewlinesThroughJsonEncoding()
        {
            StoryLog.Append("Roland", null, "one\ntwo", null);

            Assert.Contains("\"content\":\"one\\ntwo\"", WriteState());
        }

        [Fact]
        public void Append_TreatsEmptySpeakerFieldsAsAbsent()
        {
            StoryLog.Append("", "", "Just text.", "");

            string json = WriteState();

            Assert.DoesNotContain("\"teller\"", json);
            Assert.DoesNotContain("\"title\"", json);
            Assert.DoesNotContain("\"portrait\"", json);
        }

        /// <summary>Builds a minimal state object the way the serializer does, for assertion.</summary>
        private static string WriteState()
        {
            var w = new JsonWriter().Add("scene", "story");
            StoryLog.WriteTo(w);
            return w.Build();
        }
    }
}
