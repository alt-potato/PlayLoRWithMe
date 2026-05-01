using System;
using System.Collections.Generic;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Resolves localized deck labels for known multi-deck key pages.
    ///
    /// LoR exposes no public mapping from a key page id to the four keyword
    /// ids that drive its stance bufs — those keywords (e.g. "StanceSlash")
    /// live inside the <c>BattleUnitBuf_purple*</c> classes that only
    /// instantiate at battle time. We hand-maintain a small table keyed by
    /// <c>(packageId, bookId)</c> so the serializer can look each keyword up
    /// via <see cref="BattleEffectTextsXmlList.GetEffectTextName"/> and emit
    /// the player's-language stance name on the wire. The frontend treats
    /// the label as authoritative when present; books not in this table or
    /// resolved before localization is loaded fall back to the frontend's
    /// generic "Deck 1-4" table.
    /// </summary>
    public static class MultiDeckLabels
    {
        // Maps (packageId, bookId) -> keyword ids in deck-index order 0..3.
        // For each entry the keyword ids correspond to the BattleUnitBuf
        // applied by that key page's ChangeStance_* methods (see
        // PassiveAbility_250127 for The Purple Tear).
        private static readonly Dictionary<KpKey, string[]> KeywordIds =
            new Dictionary<KpKey, string[]>
            {
                // The Purple Tear (PurpleStance enum: Slash, Penetrate, Hit, Defense).
                {
                    new KpKey("", 250035),
                    new[] { "StanceSlash", "StancePenetrate", "StanceHit", "StanceDefense" }
                },
            };

        /// <summary>
        /// Resolves the four localized deck labels for the given book.
        /// Returns false when the book is not a known multi-deck page, or
        /// when localization hasn't loaded yet (e.g. very early in the title
        /// scene). On false the caller SHOULD omit <c>label</c> from the
        /// wire payload — the frontend's fallback table handles the rest.
        /// </summary>
        public static bool TryGetLabels(BookModel book, out string[] labels)
        {
            labels = null;
            if (book == null || !book.IsMultiDeck())
                return false;

            var lid = book.GetBookClassInfoId();
            var key = new KpKey(lid.packageId ?? "", lid.id);
            if (!KeywordIds.TryGetValue(key, out var keywords))
                return false;

            BattleEffectTextsXmlList xmlList;
            try
            {
                xmlList = Singleton<BattleEffectTextsXmlList>.Instance;
            }
            catch
            {
                return false;
            }
            if (xmlList == null)
                return false;

            var resolved = new string[keywords.Length];
            for (int i = 0; i < keywords.Length; i++)
            {
                string name;
                try
                {
                    // GetEffectTextName returns "" for unknown ids once
                    // the dict is populated, but throws NullRef if Init
                    // hasn't run yet. We treat both as "not ready".
                    name = xmlList.GetEffectTextName(keywords[i]);
                }
                catch
                {
                    return false;
                }
                if (string.IsNullOrEmpty(name))
                    return false;
                resolved[i] = name;
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
