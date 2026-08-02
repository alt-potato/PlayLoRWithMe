using System.Collections.Generic;
using System.Text;

namespace PlayLoRWithMe
{
    /// <summary>
    /// A single mirrored line from the game's cutscene dialogue log — either a spoken
    /// line or a story-choice outcome.
    /// </summary>
    internal sealed class StoryLogEntry
    {
        /// <summary>Speaker name. Null on choice rows.</summary>
        public string Teller;

        /// <summary>Speaker honorific/subtitle, rendered smaller and ahead of <see cref="Teller"/>.</summary>
        public string Title;

        public string Content;

        /// <summary>ASCII slug naming the extracted portrait; null when the speaker has none.</summary>
        public string Portrait;

        /// <summary>True for a story-choice outcome row rather than a spoken line.</summary>
        public bool IsChoice;

        /// <summary>Accent colour for a choice row. Meaningful only when <see cref="IsChoice"/>.</summary>
        public bool ChoiceIsRed;
    }

    /// <summary>
    /// Mirror of the game's <c>DialogLogManager</c> log for the cutscene currently on
    /// screen, kept so remote players can read at their own pace while the host clicks
    /// through.
    /// </summary>
    /// <remarks>
    /// Deliberately free of Unity and Assembly-CSharp types so this file can be linked
    /// into the headless <c>mod.tests</c> project. The Harmony patches in
    /// <see cref="StateBroadcaster"/> own the mapping from the game's <c>Dialog</c> type
    /// and the portrait sprite extraction; everything here takes primitives.
    ///
    /// Lifetime matches vanilla exactly: cleared per episode by <c>DialogLogManager.Init</c>
    /// and again when the cutscene closes, so a finished log never leaks into the
    /// following battle or library screen.
    /// </remarks>
    internal static class StoryLog
    {
        // Appends happen on the Unity main thread (Harmony postfixes) while reads can
        // happen on an HTTP thread serving a newly connected client's full-state hello.
        private static readonly object _gate = new object();
        private static readonly List<StoryLogEntry> _entries = new List<StoryLogEntry>();

        /// <summary>Longest sanitized portion of a portrait slug, before the hash suffix.</summary>
        private const int MaxSlugStemLength = 48;

        // FNV-1a 32-bit. Chosen over string.GetHashCode because that is explicitly not
        // stable across runtimes or process runs, and portrait filenames written in one
        // session must resolve in the next.
        private const uint FnvOffsetBasis = 2166136261;
        private const uint FnvPrime = 16777619;

        internal static bool IsEmpty
        {
            get
            {
                lock (_gate)
                    return _entries.Count == 0;
            }
        }

        /// <summary>Appends a spoken line. Text is normalized here, so callers pass the raw game value.</summary>
        internal static void Append(string teller, string title, string content, string portrait)
        {
            var entry = new StoryLogEntry
            {
                Teller = NullIfEmpty(teller),
                Title = NullIfEmpty(title),
                Content = StripRichText(content),
                Portrait = NullIfEmpty(portrait),
                IsChoice = false,
            };
            lock (_gate)
                _entries.Add(entry);
        }

        /// <summary>Appends a story-choice outcome row (the game's "extra log" entries).</summary>
        internal static void AppendChoice(string text, bool isRed)
        {
            var entry = new StoryLogEntry
            {
                Content = StripRichText(text),
                IsChoice = true,
                ChoiceIsRed = isRed,
            };
            lock (_gate)
                _entries.Add(entry);
        }

        internal static void Clear()
        {
            lock (_gate)
                _entries.Clear();
        }

        /// <summary>
        /// Emits the <c>storyLog</c> array, or nothing at all when no cutscene is on
        /// screen. Absence of the field is what tells the frontend to drop the panel.
        /// </summary>
        internal static void WriteTo(JsonWriter w)
        {
            lock (_gate)
            {
                if (_entries.Count == 0)
                    return;

                w.AddArray(
                    "storyLog",
                    arr =>
                    {
                        foreach (var e in _entries)
                        {
                            arr.AddObject(o =>
                            {
                                o.Add("content", e.Content);
                                if (e.Teller != null)
                                    o.Add("teller", e.Teller);
                                if (e.Title != null)
                                    o.Add("title", e.Title);
                                if (e.Portrait != null)
                                    o.Add("portrait", e.Portrait);
                                if (e.IsChoice)
                                {
                                    o.Add("isChoice", true);
                                    o.Add("choiceIsRed", e.ChoiceIsRed);
                                }
                            });
                        }
                    }
                );
            }
        }

        /// <summary>
        /// Removes Unity rich-text markup (<c>color</c>, <c>size</c>, <c>b</c>, ...) from a
        /// story line, which the game can embed because it assigns Content straight onto a
        /// rich-text-enabled uGUI Text. The web UI renders entry text as plain content, so
        /// unstripped markup would show up literally as angle-bracket tags.
        /// </summary>
        /// <remarks>
        /// A bracket pair only counts as a tag when its contents are non-empty and contain
        /// no whitespace, which is true of every Unity markup tag but false of prose like
        /// "5 &lt; 7 and 9 &gt; 3". An unclosed bracket is left alone rather than swallowing
        /// the rest of the line. Newlines are preserved — they are meaningful breaks in the
        /// source script.
        /// </remarks>
        internal static string StripRichText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            if (text.IndexOf('<') < 0)
                return text;

            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c != '<')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                int close = text.IndexOf('>', i + 1);
                if (close < 0 || !IsTagBody(text, i + 1, close))
                {
                    sb.Append(c);
                    i++;
                    continue;
                }
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>Whether text[start, end) looks like markup rather than prose between comparisons.</summary>
        private static bool IsTagBody(string text, int start, int end)
        {
            if (end <= start)
                return false;
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c == '<' || char.IsWhiteSpace(c))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Derives an ASCII-safe, filesystem- and URL-safe name for a portrait asset from
        /// the game's model key, which is not guaranteed to be ASCII.
        /// </summary>
        /// <remarks>
        /// The hash suffix is what keeps two keys that sanitize identically (common when a
        /// key is entirely non-ASCII, since every character maps to '_') from colliding on
        /// one filename.
        /// </remarks>
        internal static string SlugifyPortraitKey(string model)
        {
            if (string.IsNullOrEmpty(model))
                return null;

            var sb = new StringBuilder(MaxSlugStemLength + 9);
            int kept = 0;
            foreach (char c in model)
            {
                if (kept >= MaxSlugStemLength)
                    break;
                bool safe =
                    (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '_'
                    || c == '-';
                sb.Append(safe ? c : '_');
                kept++;
            }
            sb.Append('_').Append(Fnv1a(model).ToString("x8"));
            return sb.ToString();
        }

        private static uint Fnv1a(string value)
        {
            uint hash = FnvOffsetBasis;
            foreach (byte b in Encoding.UTF8.GetBytes(value))
            {
                hash ^= b;
                hash *= FnvPrime;
            }
            return hash;
        }

        private static string NullIfEmpty(string value) =>
            string.IsNullOrEmpty(value) ? null : value;
    }
}
