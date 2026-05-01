using System;
using System.Collections.Generic;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Resolves localized deck labels for known multi-deck key pages.
    ///
    /// Two label sources, checked in order:
    ///
    /// 1. <c>RecordLabels</c> cache, populated by
    ///    <see cref="MultiDeckLabelHook"/> after every
    ///    <c>UIEquipDeckCardList.SetDeckLayout</c> invocation. This captures
    ///    whatever any mod's Harmony patches resolved <em>after</em> the
    ///    engine's defaults — including custom literal strings written
    ///    directly to <c>TabName.text</c> (e.g. the Binah Multi-Deck mod's
    ///    "Philosophy" / "Arbiter"). Only fires once the player has opened
    ///    the in-game deck editor for that librarian, so the cache is empty
    ///    until then.
    ///
    /// 2. <c>LabelTextIds</c> static table, mapping known key pages to
    ///    <see cref="TextDataModel"/> ids that ship with localized strings
    ///    in every supported language. The engine's deck-editor prefab uses
    ///    these ids by default (see the Binah mod's
    ///    <c>SetDeckLayout</c> pre-patch, workshop 2788324005), so vanilla
    ///    multi-deck books like The Purple Tear resolve correctly here even
    ///    before the player has opened the editor.
    ///
    /// Books missing from both sources fall through to the frontend's
    /// generic <c>Deck N</c> placeholder.
    /// </summary>
    public static class MultiDeckLabels
    {
        // Cache populated by MultiDeckLabelHook on each SetDeckLayout call.
        // Length-4 arrays; individual entries may be null/empty when a
        // multi-deck book hides some of its tabs (the engine constructs the
        // tab strip with all four buttons but mods deactivate unused ones).
        // Locked because Harmony patches and the broadcast thread can both
        // read; writes only come from the main-thread postfix.
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<KpKey, string[]> Cache =
            new Dictionary<KpKey, string[]>();

        // Length of a multi-deck label vector; mirrors BookModel._deckList's
        // fixed size (4 slots regardless of how many a key page actually uses).
        public const int LabelCount = 4;

        /// <summary>
        /// Records the tab labels observed for a book by
        /// <see cref="MultiDeckLabelHook"/>. <paramref name="tabLabels"/> may
        /// be shorter than <see cref="LabelCount"/> or contain null/empty
        /// entries for hidden tabs; both are preserved as-is.
        /// </summary>
        public static void RecordLabels(BookModel book, string[] tabLabels)
        {
            if (book == null || tabLabels == null)
                return;
            var lid = book.GetBookClassInfoId();
            var k = new KpKey(lid.packageId ?? "", lid.id);

            var snapshot = new string[LabelCount];
            for (int i = 0; i < LabelCount && i < tabLabels.Length; i++)
                snapshot[i] = tabLabels[i];

            lock (CacheLock)
                Cache[k] = snapshot;
        }

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
        /// Resolves the deck labels for the given book. Returns false when
        /// no source has any label for this book. On true,
        /// <paramref name="labels"/> is always length <see cref="LabelCount"/>
        /// but individual entries may be <c>null</c> or empty — callers
        /// SHOULD check each entry before emitting <c>label</c> on the wire.
        ///
        /// Resolution order: cache (populated by the SetDeckLayout hook;
        /// captures custom mod overrides) wins where present; otherwise
        /// the static text-id table runs through TextDataModel for known
        /// vanilla pages.
        /// </summary>
        public static bool TryGetLabels(BookModel book, out string[] labels)
        {
            labels = null;
            if (book == null || !book.IsMultiDeck())
                return false;

            var lid = book.GetBookClassInfoId();
            var k = new KpKey(lid.packageId ?? "", lid.id);

            string[] cached;
            lock (CacheLock)
                Cache.TryGetValue(k, out cached);
            if (cached != null)
            {
                // Defensive copy so callers can't mutate the cached array.
                labels = (string[])cached.Clone();
                return true;
            }

            if (!LabelTextIds.TryGetValue(k, out var textIds))
                return false;

            var resolved = new string[LabelCount];
            for (int i = 0; i < textIds.Length && i < LabelCount; i++)
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
