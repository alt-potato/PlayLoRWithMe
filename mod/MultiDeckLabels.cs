using System;
using System.Collections.Generic;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Resolves localized deck labels for known multi-deck key pages.
    ///
    /// Vanilla wires the deck-editor tab labels via Unity prefabs (no
    /// public C# mapping from key page id to label keys), but the
    /// engine's prefab-side <c>UITextDataLoader</c> resolves through
    /// <see cref="TextDataModel.GetText"/> against four well-known ids
    /// (<c>ui_slash_form</c> / <c>ui_penetrate_form</c> / <c>ui_hit_form</c>
    /// / <c>ui_defense_form</c>). These ids ship with localized text in
    /// every supported language, so we can route Purple Tear's labels
    /// through them and the player sees the same strings the in-game
    /// editor renders. Mod-added multi-deck books that author custom
    /// labels can supply their own ids here (or, if the labels are
    /// language-conditional literals, fall back to omitting the entry
    /// and letting the frontend's generic <c>Deck N</c> placeholder
    /// stand in).
    /// </summary>
    public static class MultiDeckLabels
    {
        // The four TextDataModel ids the engine uses for the standard
        // stance tab labels — sourced from the Binah Multi-Deck mod's
        // SetDeckLayout pre-patch (workshop id 2788324005), which sets
        // these as the defaults before applying its own overrides.
        private static readonly string[] StanceFormTextIds =
            { "ui_slash_form", "ui_penetrate_form", "ui_hit_form", "ui_defense_form" };

        // Maps (packageId, bookId) -> array of TextDataModel ids in deck
        // index order 0..3. We resolve each id through TextDataModel at
        // serialization time so the wire payload carries strings in the
        // player's game language.
        private static readonly Dictionary<KpKey, string[]> LabelTextIds =
            new Dictionary<KpKey, string[]>
            {
                // The Purple Tear uses the engine's standard stance ids.
                { new KpKey("", 250035), StanceFormTextIds },
            };

        /// <summary>
        /// Resolves the four localized deck labels for the given book.
        /// Returns false when the book is not a known multi-deck page or
        /// when localization hasn't loaded yet (very early title-screen
        /// snapshots, before the language XML is parsed). On false the
        /// caller SHOULD omit <c>label</c> from the wire so the frontend's
        /// generic fallback handles the slot.
        /// </summary>
        public static bool TryGetLabels(BookModel book, out string[] labels)
        {
            labels = null;
            if (book == null || !book.IsMultiDeck())
                return false;

            var lid = book.GetBookClassInfoId();
            var key = new KpKey(lid.packageId ?? "", lid.id);
            if (!LabelTextIds.TryGetValue(key, out var textIds))
                return false;

            var resolved = new string[textIds.Length];
            for (int i = 0; i < textIds.Length; i++)
            {
                string text;
                try
                {
                    // GetText returns "" and logs an error if the id isn't
                    // known; it can also throw when textDic hasn't been
                    // populated yet. Both mean "not ready, skip the label".
                    text = TextDataModel.GetText(textIds[i]);
                }
                catch
                {
                    return false;
                }
                if (string.IsNullOrEmpty(text))
                    return false;
                resolved[i] = text;
            }
            labels = resolved;
            return true;
        }

        // Tuples as keys would need .NET 4.7+ ValueTuple; the mod targets
        // 4.8 but pinning a custom struct keeps the dictionary usage
        // self-contained and avoids depending on the framework's tuple impl.
        private struct KpKey : IEquatable<KpKey>
        {
            public readonly string PackageId;
            public readonly int BookId;

            public KpKey(string packageId, int bookId)
            {
                PackageId = packageId ?? "";
                BookId = bookId;
            }

            public bool Equals(KpKey other) =>
                BookId == other.BookId && PackageId == other.PackageId;

            public override bool Equals(object obj) =>
                obj is KpKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (PackageId.GetHashCode() * 397) ^ BookId;
                }
            }
        }
    }
}
