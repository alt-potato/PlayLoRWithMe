using System;
using System.Collections.Generic;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Resolves deck labels for known multi-deck key pages.
    ///
    /// The deck-editor tab labels in vanilla are configured on Unity
    /// prefabs via <c>UITextDataLoader.key</c> + <c>TextDataModel.GetText</c>,
    /// so there is no public mapping from a key page id to the
    /// <c>TextDataModel</c> keys driving its tab bar. The buf keyword ids
    /// (e.g. <c>StanceSlash</c>) round-trip through
    /// <see cref="BattleEffectTextsXmlList"/> but resolve to the buf names
    /// ("Slashing Stance" etc.) — not the short deck-editor labels.
    ///
    /// Until we identify the right <c>TextDataModel</c> keys (or scan the
    /// loaded text dictionary at runtime to discover them), the labels
    /// here are authored as English literals matching the in-game UI.
    /// Mods that author additional multi-deck books and want labels can
    /// add an entry here without a frontend change.
    /// </summary>
    public static class MultiDeckLabels
    {
        // Maps (packageId, bookId) -> deck labels in deck-index order 0..3.
        // Strings here mirror the labels shown on the in-game deck-editor's
        // tab bar (English) for the corresponding stance.
        private static readonly Dictionary<KpKey, string[]> Labels =
            new Dictionary<KpKey, string[]>
            {
                // The Purple Tear (PurpleStance enum: Slash, Penetrate, Hit, Defense).
                // In-game tab labels: Slash / Pierce / Blunt / Guard.
                {
                    new KpKey("", 250035),
                    new[] { "Slash", "Pierce", "Blunt", "Guard" }
                },
            };

        /// <summary>
        /// Resolves the four deck labels for the given book. Returns false
        /// when the book is not a known multi-deck page; on false the caller
        /// SHOULD omit <c>label</c> from the wire payload so the frontend's
        /// generic fallback handles the slot.
        /// </summary>
        public static bool TryGetLabels(BookModel book, out string[] labels)
        {
            labels = null;
            if (book == null || !book.IsMultiDeck())
                return false;

            var lid = book.GetBookClassInfoId();
            var key = new KpKey(lid.packageId ?? "", lid.id);
            if (!Labels.TryGetValue(key, out var entry))
                return false;

            // Defensive copy so callers can't mutate the cached array.
            labels = (string[])entry.Clone();
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
